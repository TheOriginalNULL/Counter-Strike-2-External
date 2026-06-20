using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using CounterStrike2.Entities;
using CounterStrike2.Memory;
using CounterStrike2.Rendering;
using CounterStrike2.Skins;
using System.IO;

namespace CounterStrike2
{
    public partial class OverlayWindow : Window
    {
        // ── Win32: click-through ──
        private const int GWL_EXSTYLE      = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED     = 0x00080000;
        private const int WS_EX_TOOLWINDOW  = 0x00000080;

        [DllImport("user32.dll")] static extern int  GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int  SetWindowLong(IntPtr h, int i, int v);

        // ── Win32: game-window tracking ──
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lp);
        [StructLayout(LayoutKind.Sequential)] struct RECT  { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        // ── Frame bundle: updated by background thread, read by UI thread ──
        private sealed class Frame
        {
            public readonly List<PlayerEntity>        Players;
            public readonly List<GrenadeEntity>       Grenades;
            public readonly List<DroppedWeaponEntity> Loot;
            public readonly BombEntity?               Bomb;
            public readonly List<string>              Spectators;
            public readonly Matrix4x4                 ViewMatrix;
            public readonly bool                      HitThisFrame;
            public Frame(List<PlayerEntity> p, List<GrenadeEntity> g,
                         List<DroppedWeaponEntity> l, BombEntity? b,
                         List<string> spec, Matrix4x4 vm, bool hit)
            { Players = p; Grenades = g; Loot = l; Bomb = b; Spectators = spec; ViewMatrix = vm; HitThisFrame = hit; }
        }
        private volatile Frame? _frame;
        private SpectatorWindow? _specWindow;
        private RadarWindow?     _radarWindow;

        private readonly CancellationTokenSource _cts = new();

        // ── FPS counter ──
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private int    _frames;
        private double _lastTick;

        // ── Hit marker flash duration ──
        private double _hitMarkerUntil;

        // ── Game-window rect in overlay WPF DIPs (updated every ~250 ms) ──
        private Rect   _gameRect = new(0, 0, 1920, 1080);
        private IntPtr _gameHwnd;
        private double _dpiX = 1, _dpiY = 1;   // cached once from PresentationSource
        private bool   _dpiResolved;
        private double _lastWindowRefresh = double.MinValue;

        private readonly EnumWindowsProc _enumCb;

        // ── Constructor ──

        public OverlayWindow()
        {
            InitializeComponent();
            DataContext = Config.Current;

            Left   = 0;
            Top    = 0;
            Width  = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            _enumCb = EnumWindowsCallback;

            Loaded += OnLoaded;
            Task.Run(FrameLoop,  _cts.Token);
            Task.Run(AuxLoop,    _cts.Token);
            Task.Run(RenderLoop, _cts.Token);
            _specWindow  = new SpectatorWindow();
            _radarWindow = new RadarWindow();

            RebuildCrosshair();
            Config.Current.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Config.CrosshairColor) or nameof(Config.CrosshairSize)
                    or nameof(Config.CrosshairThickness) or nameof(Config.CrosshairGap) or nameof(Config.CrosshairDot))
                    RebuildCrosshair();
            };
        }

        // ── Crosshair shape (rebuilt only when its settings change, not every frame) ──

        private void RebuildCrosshair()
        {
            Crosshair.Children.Clear();

            Brush brush;
            try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Config.Current.CrosshairColor)); }
            catch { brush = Brushes.White; }

            double gap   = Config.Current.CrosshairGap;
            double size  = Config.Current.CrosshairSize;
            double thick = Config.Current.CrosshairThickness;
            double end   = gap + size;

            Crosshair.Children.Add(new Line { X1 = -end, Y1 = 0, X2 = -gap, Y2 = 0, Stroke = brush, StrokeThickness = thick });
            Crosshair.Children.Add(new Line { X1 = gap,  Y1 = 0, X2 = end,  Y2 = 0, Stroke = brush, StrokeThickness = thick });
            Crosshair.Children.Add(new Line { X1 = 0, Y1 = -end, X2 = 0, Y2 = -gap, Stroke = brush, StrokeThickness = thick });
            Crosshair.Children.Add(new Line { X1 = 0, Y1 = gap,  X2 = 0, Y2 = end,  Stroke = brush, StrokeThickness = thick });

            if (Config.Current.CrosshairDot)
            {
                var dot = new Ellipse { Width = 2, Height = 2, Fill = brush };
                Canvas.SetLeft(dot, -1);
                Canvas.SetTop(dot,  -1);
                Crosshair.Children.Add(dot);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);

            Canvas.SetLeft(FpsBox,    14);
            Canvas.SetTop(FpsBox,     48);
            Canvas.SetLeft(Crosshair, ActualWidth  / 2);
            Canvas.SetTop(Crosshair,  ActualHeight / 2);
            Canvas.SetLeft(HitMarker, ActualWidth  / 2);
            Canvas.SetTop(HitMarker,  ActualHeight / 2);
        }

        // ── Game-window tracking (called every 250 ms, not every render frame) ──

        private bool EnumWindowsCallback(IntPtr hwnd, IntPtr lp)
        {
            if (!IsWindowVisible(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if ((int)pid == (int)lp) { _gameHwnd = hwnd; return false; }
            return true;
        }

        private void RefreshGameWindow()
        {
            try
            {
                // Find the game HWND once; re-search only if it's destroyed.
                if (_gameHwnd == IntPtr.Zero)
                {
                    if (!GameContext.IsAttached) return;
                    var proc = GameContext.Memory?.Process;
                    if (proc == null || proc.HasExited) return;
                    EnumWindows(_enumCb, new IntPtr(proc.Id));
                }

                if (_gameHwnd == IntPtr.Zero) return;

                // GetClientRect failure means the window was destroyed → reset.
                if (!GetClientRect(_gameHwnd, out RECT cr)) { _gameHwnd = IntPtr.Zero; return; }
                if (cr.R <= 0 || cr.B <= 0) return;

                var tl = new POINT();
                ClientToScreen(_gameHwnd, ref tl);

                // Resolve DPI once; it only changes if the window moves monitors.
                if (!_dpiResolved)
                {
                    var src = PresentationSource.FromVisual(this);
                    if (src?.CompositionTarget == null) return;
                    _dpiX = src.CompositionTarget.TransformToDevice.M11;
                    _dpiY = src.CompositionTarget.TransformToDevice.M22;
                    if (_dpiX <= 0 || _dpiY <= 0) return;
                    _dpiResolved = true;
                }

                double x = tl.X / _dpiX;
                double y = tl.Y / _dpiY;
                double w = cr.R / _dpiX;
                double h = cr.B / _dpiY;

                if (w <= 0 || h <= 0) return;

                bool moved = Math.Abs(Left - x) > 0.5 || Math.Abs(Top - y) > 0.5 ||
                             Math.Abs(Width - w) > 0.5 || Math.Abs(Height - h) > 0.5;
                if (moved)
                {
                    Left   = x;  Top    = y;
                    Width  = w;  Height = h;
                    Canvas.SetLeft(Crosshair, w / 2);
                    Canvas.SetTop(Crosshair,  h / 2);
                    Canvas.SetLeft(HitMarker, w / 2);
                    Canvas.SetTop(HitMarker,  h / 2);
                    _dpiResolved = false;   // moving monitors resets DPI
                }

                _gameRect = new Rect(0, 0, w, h);
            }
            catch { }
        }

        // ── Background frame loop: reads entities + view matrix, bundles as Frame ──

        private async Task FrameLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (GameContext.IsAttached && Offsets.IsReady)
                    {
                        var players  = EntityReader.ReadPlayers(GameContext.Memory);
                        // Captured immediately after players, before anything below that could
                        // block this thread for any meaningful time (notably SkinChanger.Tick's
                        // mesh-mask retry loop, which can Thread.Sleep up to ~50ms when a
                        // weapon's item state gets reprocessed) — so the view matrix used for
                        // this frame's ESP projection is never staler than it has to be.
                        var vm = GameContext.Memory.Read<Matrix4x4>(Offsets.ViewMatrix);
                        // Also needed by the radar's grenade markers, independent of the ESP toggle.
                        var grenades = (Config.Current.EspGrenades || Config.Current.RadarEnabled)
                            ? GrenadeReader.ReadGrenades(GameContext.Memory)
                            : new List<GrenadeEntity>(0);
                        // Loot/bomb are also needed by the radar's C4 marker, independent of
                        // their own ESP toggles — read them whenever either consumer wants them.
                        var loot = (Config.Current.EspLoot || Config.Current.RadarEnabled)
                            ? DroppedWeaponReader.ReadLoot(GameContext.Memory)
                            : new List<DroppedWeaponEntity>(0);
                        var bomb = (Config.Current.EspBomb || Config.Current.RadarEnabled)
                            ? BombReader.Read(GameContext.Memory)
                            : null;

                        // Kill/hit/weapon-equip sounds and the skin changer all live on AuxLoop
                        // now — SkinChanger.Tick's mesh-mask retry loop can block its thread for
                        // tens of ms, and none of this needs the same cadence as player tracking.
                        // Pick up whatever AuxLoop has produced most recently.
                        bool hit = Interlocked.Exchange(ref _pendingHit, 0) == 1;

                        _frame = new Frame(players, grenades, loot, bomb, _spectatorsCache, vm, hit);
                    }
                    else
                    {
                        _frame = null;
                    }
                }
                catch { _frame = null; }

                try { await Task.Delay(8, _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }

        // ── Auxiliary loop: sounds, skin changer, spectator list — anything that can tolerate
        // a slower, less precise cadence and must never be allowed to block the fast loop above.

        private volatile List<string> _spectatorsCache = new(0);
        private int _pendingHit;   // 0/1 via Interlocked — set here, consumed by FrameLoop

        private async Task AuxLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (GameContext.IsAttached && Offsets.IsReady)
                    {
                        // Kill sound
                        if (Config.Current.KillSound)
                        {
                            int kills = KillTracker.Update(GameContext.Memory);
                            if (kills > 0)
                                KillTracker.PlaySound(Config.Current.KillSoundPath);
                        }
                        else
                        {
                            KillTracker.Reset();
                        }

                        // Weapon equip sound
                        if (Config.Current.WeaponEquipSound)
                        {
                            if (WeaponEquipTracker.Check(GameContext.Memory))
                                KillTracker.PlaySound(Config.Current.WeaponEquipSoundPath);
                        }
                        else
                        {
                            WeaponEquipTracker.Reset();
                        }

                        // Hit indicator / hit sound — damage WE dealt this round.
                        if (Config.Current.HitIndicator || Config.Current.HitSound)
                        {
                            if (DamageTracker.Update(GameContext.Memory) > 0f)
                            {
                                Interlocked.Exchange(ref _pendingHit, 1);
                                if (Config.Current.HitSound)
                                    KillTracker.PlaySound(Config.Current.HitSoundPath);
                            }
                        }
                        else
                        {
                            DamageTracker.Reset();
                        }

                        // Spectator list
                        _spectatorsCache = Config.Current.SpectatorList
                            ? SpectatorReader.Read(GameContext.Memory, EntityReader.LocalPawnPtr)
                            : EmptySpectators;

                        SkinChanger.Tick(GameContext.Memory, EntityReader.LocalPawnPtr);
                        // Glove changer disabled — caused crashes on match enter/leave. The backend
                        // (SkinChanger.TickGloves and friends) is left in place to pick back up later.
                    }
                }
                catch { }

                try { await Task.Delay(16, _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static readonly List<string> EmptySpectators = new(0);

        // ── High-frequency render loop: submits frames to WPF/DWM at up to ~500Hz ──
        // CompositionTarget.Rendering is locked to the PRIMARY monitor's DWM rate (often
        // 60/75Hz even when the gaming monitor is 144Hz+). Running our own loop bypasses
        // that cap and lets DWM pick up our latest frame each time it composites.

        private async Task RenderLoop()
        {
            const double targetMs = 1000.0 / 250; // cap at 250fps — more than any 240Hz monitor needs
            var sw   = Stopwatch.StartNew();
            double next = sw.Elapsed.TotalMilliseconds;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Dispatcher.InvokeAsync(OnFrame, System.Windows.Threading.DispatcherPriority.Render);

                    next += targetMs;
                    int sleep = (int)(next - sw.Elapsed.TotalMilliseconds);
                    if (sleep > 0)
                        await Task.Delay(sleep, _cts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch { /* swallow transient errors */ }
            }
        }

        // ── Per-render-frame callback (UI thread — zero kernel calls) ──

        private void OnFrame()
        {
            if (!IsLoaded) return;

            // Throttle the expensive Win32 window-tracking to 4 Hz.
            double now = _sw.Elapsed.TotalSeconds;
            if (now - _lastWindowRefresh >= 0.25)
            {
                RefreshGameWindow();
                _lastWindowRefresh = now;
            }

            // FPS counter
            _frames++;
            if (now - _lastTick >= 0.5)
            {
                int fps  = (int)(_frames / (now - _lastTick));
                var f    = _frame;
                int ents = f?.Players.Count ?? 0;

                string diag = f != null
                    ? $" | ptrs:{EntityReader.LastPtrCount} raw:{EntityReader.LastRawCount}"
                    : "";

                FpsText.Text = $"{fps} fps | {ents} ents{diag}";
                _frames   = 0;
                _lastTick = now;
            }

            // Hit marker — kept independent of EspEnabled so it still works with ESP boxes off.
            var frame = _frame;
            if (Config.Current.HitIndicator && frame != null && frame.HitThisFrame)
                _hitMarkerUntil = now + 0.25;
            HitMarker.Visibility = (Config.Current.HitIndicator && now < _hitMarkerUntil)
                ? Visibility.Visible : Visibility.Collapsed;

            // ESP — UI thread does zero RPM calls, just draws cached data.
            if (frame == null || !Config.Current.EspEnabled)
            {
                EspLayer.Clear();
                return;
            }

            EspLayer.Redraw(frame.Players, frame.Grenades, frame.Loot, frame.Bomb, frame.ViewMatrix, Config.Current, _gameRect);

            // Spectator window
            bool showSpec = Config.Current.SpectatorList && frame != null;
            if (_specWindow != null)
            {
                if (showSpec)
                {
                    if (!_specWindow.IsVisible) _specWindow.Show();
                    _specWindow.UpdateNames(frame!.Spectators);
                }
                else if (_specWindow.IsVisible)
                {
                    _specWindow.Hide();
                }
            }

            // Radar window
            bool showRadar = Config.Current.RadarEnabled && frame != null;
            if (_radarWindow != null)
            {
                if (showRadar)
                {
                    if (!_radarWindow.IsVisible) _radarWindow.Show();
                    _radarWindow.Opacity = Config.Current.RadarOpacity;
                    float yaw = WorldToScreen.ExtractYaw(frame!.ViewMatrix);
                    _radarWindow.UpdateRadar(frame!.Players, yaw, frame!.Bomb, frame!.Loot, frame!.Grenades);
                }
                else if (_radarWindow.IsVisible)
                {
                    _radarWindow.Hide();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _cts.Cancel();
            _specWindow?.Close();
            _radarWindow?.Close();
            base.OnClosed(e);
        }
    }
}
