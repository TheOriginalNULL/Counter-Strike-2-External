using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CounterStrike2.Debug;
using CounterStrike2.Entities;
using CounterStrike2.Memory;
using CounterStrike2.Skins;

namespace CounterStrike2
{
    public partial class MainWindow : Window
    {
        // ── Global low-level keyboard hook ──────────────────────────────────────
        // WH_KEYBOARD_LL fires even when a fullscreen/exclusive game has focus.
        // The delegate must be a field; a local would be GC'd while the hook is live.

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int VK_INSERT      = 0x2D;
        private const int VK_END         = 0x23;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] private static extern bool   UnhookWindowsHookEx(IntPtr hk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hk, int n, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] private static extern bool   ClipCursor(IntPtr rect); // IntPtr.Zero = release
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string? name);

        private readonly LowLevelKeyboardProc _kbProc;
        private IntPtr _kbHook;

        // ───────────────────────────────────────────────────────────────────────

        private bool _panelsReady;

        public MainWindow()
        {
            Config.Load();
            InitializeComponent();
            _panelsReady = true;
            DataContext = Config.Current;

            if (Config.Current.StartMinimized)
                WindowState = WindowState.Minimized;

            ApplyStatus(GameContext.Current);

            // Wire the debug offset table (x:Static can silently fail, code-behind is safer).
            OffsetTable.ItemsSource = CounterStrike2.Debug.DebugView.Entries;

            // Sync color swatches with loaded config.
            UpdateColorSwatches();

            // Install global keyboard hook for INSERT toggle.
            _kbProc = KeyboardHook;
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(null), 0);

            StartSkinTimer();
            RefreshSkinList();
            RefreshProfileList();
        }

        // ---- Config profiles ----

        private void ProfileSave_Click(object sender, RoutedEventArgs e)
        {
            string name = ProfileNameBox.Text.Trim();
            if (name.Length == 0) return;
            Config.Current.SaveAsProfile(name);
            ProfileNameBox.Text = string.Empty;
            RefreshProfileList();
        }

        private void ProfileLoad_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string name && Config.LoadProfile(name))
            {
                SkinChanger.ForceReapply();
                RefreshSkinList();
            }
        }

        private void ProfileDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string name)
            {
                Config.DeleteProfile(name);
                RefreshProfileList();
            }
        }

        private void ProfileExport_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is not string name) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title           = "Export profile",
                FileName        = name + ".json",
                Filter          = "Profile JSON|*.json",
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() == true)
                Config.ExportProfile(name, dlg.FileName);
        }

        private void ProfileImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title           = "Import profile",
                Filter          = "Profile JSON|*.json",
                RestoreDirectory = true
            };
            if (dlg.ShowDialog() != true) return;

            string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            if (Config.ImportProfile(dlg.FileName, name))
                RefreshProfileList();
        }

        private void RefreshProfileList()
        {
            var names = Config.ListProfiles();
            ProfileList.ItemsSource     = names;
            ProfileListEmpty.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (kb.vkCode == VK_INSERT)
                    Dispatcher.BeginInvoke(ToggleMenu);
                else if (kb.vkCode == VK_END)
                    Dispatcher.BeginInvoke(Close);
            }
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private void ToggleMenu()
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                ClipCursor(IntPtr.Zero); // release CS2's in-game cursor lock
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        }

        // ---- Window chrome ----

        private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        // ---- Navigation ----

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (!_panelsReady) return;
            var tag = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString();
            PanelVisuals.Visibility  = tag == "Visuals"  ? Visibility.Visible : Visibility.Collapsed;
            PanelMisc.Visibility     = tag == "Misc"     ? Visibility.Visible : Visibility.Collapsed;
            PanelSkins.Visibility    = tag == "Skins"    ? Visibility.Visible : Visibility.Collapsed;
            PanelDebug.Visibility    = tag == "Debug"    ? Visibility.Visible : Visibility.Collapsed;
            PanelSettings.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Overlay ----

        private OverlayWindow? _overlay;

        private void OverlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay == null) OpenOverlay();
            else                  _overlay.Close();
        }

        private void OpenOverlay()
        {
            _overlay = new OverlayWindow();
            _overlay.Closed += (_, _) => { _overlay = null; OverlayBtn.Content = "Show overlay"; };
            _overlay.Show();
            OverlayBtn.Content = "Hide overlay";
        }

        private void CrosshairCustomize_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CrosshairSettingsWindow { Owner = this };
            dlg.ShowDialog();
        }

        // ---- Attach / Detach ----

        private async void AttachBtn_Click(object sender, RoutedEventArgs e)
        {
            if (GameContext.IsAttached)
            {
                // Detach = done for this session — close the app entirely so no handle lingers.
                Close();
                return;
            }

            AttachBtn.IsEnabled = false;
            ApplyStatus(GameContext.State.Connecting);

            var result = await GameContext.AttachAsync();

            // Push resolved addresses into the debug table.
            DebugView.Refresh();

            ApplyStatus(result);
            AttachBtn.IsEnabled = true;

            // Auto-open the overlay as soon as attach succeeds.
            if (result == GameContext.State.Ready && _overlay == null)
                OpenOverlay();
        }

        private void ApplyStatus(GameContext.State state)
        {
            switch (state)
            {
                case GameContext.State.Detached:
                    StatusDot.Fill    = HexBrush("#E0564B");
                    StatusText.Text   = "Not attached";
                    AttachBtn.Content = "Attach to CS2";
                    break;

                case GameContext.State.Connecting:
                    StatusDot.Fill    = HexBrush("#F0A030");
                    StatusText.Text   = "Connecting…";
                    AttachBtn.Content = "Connecting…";
                    break;

                case GameContext.State.Scanning:
                    StatusDot.Fill    = HexBrush("#F0D030");
                    StatusText.Text   = "Scanning offsets…";
                    AttachBtn.Content = "Scanning…";
                    break;

                case GameContext.State.Ready:
                    StatusDot.Fill    = HexBrush("#4FE36A");
                    StatusText.Text   = "cs2.exe — ready";
                    AttachBtn.Content = "Detach";
                    break;

                case GameContext.State.Failed:
                    StatusDot.Fill    = HexBrush("#E0564B");
                    StatusText.Text   = string.IsNullOrEmpty(GameContext.LastError)
                        ? "cs2.exe — not found"
                        : GameContext.LastError;
                    AttachBtn.Content = "Attach to CS2";
                    break;
            }
        }

        // ---- Process scanner (no attach needed) ----

        private void ScanBtn_Click(object sender, RoutedEventArgs e)
        {
            SanityResults.Children.Clear();

            // Is this process running elevated?
            bool isAdmin;
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                isAdmin = new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            AddResult(isAdmin, isAdmin
                ? "Running as Administrator"
                : "NOT Administrator — right-click the .exe and choose 'Run as administrator'");

            // Look for cs2.exe specifically
            var cs2 = System.Diagnostics.Process.GetProcessesByName("cs2");
            AddResult(cs2.Length > 0, cs2.Length > 0
                ? $"cs2.exe found — {cs2.Length} instance(s) — PID: {string.Join(", ", System.Linq.Enumerable.Select(cs2, p => p.Id))}"
                : "cs2.exe NOT found in process list");

            // Show the last attach error if there is one
            if (!string.IsNullOrEmpty(GameContext.LastError))
                AddResult(false, $"Last attach error: {GameContext.LastError}");

            AddSeparator();

            // Dump all process names containing "cs" so user can see the real name
            AddResult(true, "All processes containing 'cs':");
            var matching = System.Linq.Enumerable.OrderBy(
                System.Linq.Enumerable.Where(
                    System.Diagnostics.Process.GetProcesses(),
                    p => p.ProcessName.Contains("cs", StringComparison.OrdinalIgnoreCase)),
                p => p.ProcessName);
            foreach (var p in matching)
                AddResult(true, $"  {p.ProcessName,-30} PID {p.Id}");
        }

        // ---- Sanity check ----

        private void SanityBtn_Click(object sender, RoutedEventArgs e)
        {
            SanityResults.Children.Clear();
            var mem = GameContext.Memory;
            try
            {
                RunSanityChecks(mem);
            }
            catch (Exception ex)
            {
                AddResult(false, $"Unexpected error: {ex.GetType().Name} — {ex.Message}");
            }
        }

        private void RunSanityChecks(ProcessMemory mem)
        {

            // 0a — is this process running as admin?
            bool isAdmin;
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                isAdmin = new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            AddResult(isAdmin, isAdmin
                ? "Running as Administrator"
                : "NOT running as Administrator — re-launch the .exe as admin (right-click → Run as administrator)");

            // 0b — can we even see cs2.exe in the process list?
            var cs2Procs = System.Diagnostics.Process.GetProcessesByName("cs2");
            bool procVisible = cs2Procs.Length > 0;
            AddResult(procVisible, procVisible
                ? $"cs2.exe visible in process list — {cs2Procs.Length} instance(s), PID: {string.Join(", ", System.Linq.Enumerable.Select(cs2Procs, p => p.Id))}"
                : "cs2.exe NOT in process list — is the game running?");

            AddSeparator();

            // 1 — process attached?
            bool attached = GameContext.IsAttached && mem.Process is { HasExited: false };
            AddResult(attached,
                attached
                    ? $"Handle opened — cs2.exe (PID {mem.Process!.Id})"
                    : string.IsNullOrEmpty(GameContext.LastError)
                        ? "Not attached — click 'Attach to CS2' first"
                        : $"Attach failed: {GameContext.LastError}");

            if (!attached) return;

            // 2 — client.dll
            var clientBase = mem.GetModuleBase("client.dll");
            AddResult(clientBase != IntPtr.Zero,
                clientBase != IntPtr.Zero
                    ? $"client.dll base         @ 0x{clientBase.ToInt64():X16}"
                    : "client.dll not found in module list");

            // 3 — engine2.dll
            var engineBase = mem.GetModuleBase("engine2.dll");
            AddResult(engineBase != IntPtr.Zero,
                engineBase != IntPtr.Zero
                    ? $"engine2.dll base        @ 0x{engineBase.ToInt64():X16}"
                    : "engine2.dll not found in module list");

            // 4 — critical offsets
            bool ready = Offsets.IsReady;
            AddResult(ready,
                ready
                    ? "Critical offsets resolved  (EntityList, LocalPlayerController, ViewMatrix)"
                    : "One or more critical offsets are zero — patterns may need updating");

            if (!ready) return;

            // 5 — read entity list pointer (one deref through global var)
            var entityListPtr = mem.Read<IntPtr>(Offsets.EntityList);
            bool entityOk = entityListPtr != IntPtr.Zero;
            AddResult(entityOk,
                entityOk
                    ? $"EntityList ptr           @ 0x{entityListPtr.ToInt64():X16}"
                    : "EntityList global var reads as null — wrong offset or game not in a match");

            // 6 — local player controller pointer
            var ctrlPtr = mem.Read<IntPtr>(Offsets.LocalPlayerController);
            bool ctrlOk = ctrlPtr != IntPtr.Zero;
            AddResult(ctrlOk,
                ctrlOk
                    ? $"LocalPlayerController    @ 0x{ctrlPtr.ToInt64():X16}"
                    : "LocalPlayerController is null — spawn into a bot match first");

            // 6b — entity system pointer (one deref through global var)
            var entitySysPtr = mem.Read<IntPtr>(Offsets.EntityList);
            bool entitySysOk = entitySysPtr != IntPtr.Zero;
            AddResult(entitySysOk,
                entitySysOk
                    ? $"EntitySystem ptr         @ 0x{entitySysPtr.ToInt64():X16}"
                    : "EntitySystem is null — entity list not ready (join a match first)");

            if (ctrlOk && entitySysOk)
            {
                // Show first chunk pointer so we can verify entity traversal.
                var chunk0 = mem.Read<IntPtr>(IntPtr.Add(entitySysPtr, 0x10));
                bool chunk0Ok = chunk0 != IntPtr.Zero;
                AddResult(chunk0Ok,
                    chunk0Ok
                        ? $"Chunk[0] ptr             @ 0x{chunk0.ToInt64():X16}"
                        : "Chunk[0] is null — entity list empty or layout wrong");

                if (chunk0Ok)
                {
                    // Show first 3 entity ptrs so we know if slots are populated.
                    for (int slot = 0; slot < 3; slot++)
                    {
                        IntPtr ep = mem.Read<IntPtr>(IntPtr.Add(chunk0, slot * 0x70));
                        AddResult(ep != IntPtr.Zero,
                            $"  Entity[{slot}] ptr         = 0x{ep.ToInt64():X}");
                    }
                }

                // Scan for m_hPlayerPawn near +0x7E4 — show every uint in +0x7C0..+0x820
                // that decodes to a plausible pawn slot (1-511) via handle & 0x7FFF.
                AddResult(true, "Scanning ctrl+0x7C0..+0x820 for pawn handle:");
                for (int off = 0x7C0; off <= 0x820; off += 4)
                {
                    uint h = mem.Read<uint>(IntPtr.Add(ctrlPtr, off));
                    int s = (int)(h & 0x7FFF);
                    if (h != 0 && h != 0xFFFFFFFF && s >= 1 && s < 512)
                        AddResult(true, $"  +0x{off:X3}  h=0x{h:X8}  slot={s}  ← candidate");
                }
            }

            // 7 — view matrix (first float should be a reasonable value, not 0 or NaN)
            float m00 = mem.Read<float>(Offsets.ViewMatrix);
            bool vmOk = !float.IsNaN(m00) && m00 != 0f;
            AddResult(vmOk,
                vmOk
                    ? $"ViewMatrix[0,0]          = {m00:F6}  (looks valid)"
                    : "ViewMatrix[0,0] is 0 or NaN — may not be in-game yet");

            AddSeparator();
            int passed  = 0, total = 0;
            foreach (UIElement el in SanityResults.Children)
                if (el is StackPanel { Tag: bool b }) { total++; if (b) passed++; }
            AddResult(passed == total, $"Result: {passed}/{total} checks passed");
        }

        private void AddResult(bool pass, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(0, 3, 0, 3),
                Tag         = pass
            };

            row.Children.Add(new Ellipse
            {
                Width               = 7,
                Height              = 7,
                Fill                = HexBrush(pass ? "#4FE36A" : "#E0564B"),
                VerticalAlignment   = VerticalAlignment.Center,
                Margin              = new Thickness(0, 0, 8, 0)
            });

            row.Children.Add(new TextBlock
            {
                Text              = text,
                Foreground        = HexBrush(pass ? "#ECE9F5" : "#8A83A3"),
                FontSize          = 11,
                FontFamily        = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            });

            SanityResults.Children.Add(row);
        }

        private void AddSeparator()
        {
            SanityResults.Children.Add(new Rectangle
            {
                Height = 1,
                Fill   = HexBrush("#271F38"),
                Margin = new Thickness(0, 6, 0, 6)
            });
        }

        // ---- Color picker ----

        internal static readonly string[] SwatchColors =
        {
            "#FFFFFF", "#FFDD00", "#FF8844", "#FF4444",
            "#FF44AA", "#CC44FF", "#8844FF", "#4466FF",
            "#44AAFF", "#00CCFF", "#00FF88", "#88FF44",
            "#FF641E", "#00C8FF", "#A35BFF", "#808080"
        };

        private Popup?   _colorPopup;
        private TextBox? _hexBox;
        private string   _editingProp = string.Empty;

        private void EspColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border b && b.Tag is string prop)
                ShowColorPicker(b, prop);
        }

        private void ShowColorPicker(UIElement anchor, string prop)
        {
            _editingProp = prop;
            if (_colorPopup == null) BuildColorPopup();

            _hexBox!.Text = GetPropColor(prop);
            _colorPopup!.PlacementTarget = anchor;
            _colorPopup.Placement = PlacementMode.Bottom;
            _colorPopup.IsOpen    = true;
            _hexBox.Focus();
            _hexBox.SelectAll();
        }

        private static string GetPropColor(string prop) => prop switch
        {
            "BoxColor"      => Config.Current.BoxColor,
            "HealthColor"   => Config.Current.HealthColor,
            "NameColor"     => Config.Current.NameColor,
            "DistanceColor" => Config.Current.DistanceColor,
            "WeaponColor"   => Config.Current.WeaponColor,
            "SkeletonColor" => Config.Current.SkeletonColor,
            "SnapColor"     => Config.Current.SnapColor,
            "GrenadeColor"  => Config.Current.GrenadeColor,
            _               => "#FFFFFF"
        };

        private static void SetPropColor(string prop, string hex)
        {
            switch (prop)
            {
                case "BoxColor":      Config.Current.BoxColor      = hex; break;
                case "HealthColor":   Config.Current.HealthColor   = hex; break;
                case "NameColor":     Config.Current.NameColor     = hex; break;
                case "DistanceColor": Config.Current.DistanceColor = hex; break;
                case "WeaponColor":   Config.Current.WeaponColor   = hex; break;
                case "SkeletonColor": Config.Current.SkeletonColor = hex; break;
                case "SnapColor":     Config.Current.SnapColor     = hex; break;
                case "GrenadeColor":  Config.Current.GrenadeColor  = hex; break;
            }
        }

        private void BuildColorPopup()
        {
            var card = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0x1A, 0x15, 0x26)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x2B, 0x5C)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(10),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black }
            };

            var stack = new StackPanel { Width = 168 };

            // Swatches — 4 columns
            var swatchGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var hex in SwatchColors)
            {
                var sw = new Border
                {
                    Width        = 32, Height      = 32,
                    CornerRadius = new CornerRadius(6),
                    Background   = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                    Margin       = new Thickness(2),
                    Cursor       = Cursors.Hand,
                    BorderBrush  = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderThickness = new Thickness(1)
                };
                var capturedHex = hex;
                sw.MouseLeftButtonDown += (_, _) => ApplyColor(capturedHex);
                sw.MouseEnter += (s, _) => ((Border)s).BorderBrush = new SolidColorBrush(Colors.White);
                sw.MouseLeave += (s, _) => ((Border)s).BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                swatchGrid.Children.Add(sw);
            }

            // Hex input row
            var hexRow = new Grid();
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _hexBox = new TextBox
            {
                Background        = new SolidColorBrush(Color.FromRgb(0x0D, 0x0B, 0x14)),
                Foreground        = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xF5)),
                BorderBrush       = new SolidColorBrush(Color.FromRgb(0x27, 0x1F, 0x38)),
                BorderThickness   = new Thickness(1, 1, 0, 1),
                Padding           = new Thickness(8, 5, 8, 5),
                FontFamily        = new FontFamily("Consolas"),
                FontSize          = 12,
                CaretBrush        = new SolidColorBrush(Color.FromRgb(0xA3, 0x5B, 0xFF)),
                MaxLength         = 7
            };
            _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyColor(_hexBox.Text); };

            var confirmBtn = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(0xA3, 0x5B, 0xFF)),
                CornerRadius    = new CornerRadius(0, 5, 5, 0),
                Padding         = new Thickness(10, 5, 10, 5),
                Cursor          = Cursors.Hand
            };
            confirmBtn.Child = new TextBlock
            {
                Text       = "OK",
                Foreground = Brushes.White,
                FontSize   = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            confirmBtn.MouseLeftButtonDown += (_, _) => ApplyColor(_hexBox.Text);

            Grid.SetColumn(_hexBox,    0);
            Grid.SetColumn(confirmBtn, 1);
            hexRow.Children.Add(_hexBox);
            hexRow.Children.Add(confirmBtn);

            stack.Children.Add(swatchGrid);
            stack.Children.Add(hexRow);
            card.Child  = stack;

            _colorPopup = new Popup
            {
                Child            = card,
                StaysOpen        = false,
                AllowsTransparency = true,
                PopupAnimation   = PopupAnimation.Fade
            };
        }

        private void ApplyColor(string hex)
        {
            if (!hex.StartsWith("#")) hex = "#" + hex;
            try { ColorConverter.ConvertFromString(hex); } catch { return; }

            SetPropColor(_editingProp, hex);
            UpdateColorSwatches();
            _colorPopup!.IsOpen = false;
        }

        private void UpdateColorSwatches()
        {
            void Set(Border b, string hex)
            {
                try { b.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); } catch { }
            }
            Set(BoxColorSwatch,    Config.Current.BoxColor);
            Set(HealthColorSwatch, Config.Current.HealthColor);
            Set(NameColorSwatch,   Config.Current.NameColor);
            Set(DistColorSwatch,   Config.Current.DistanceColor);
            Set(WeaponColorSwatch, Config.Current.WeaponColor);
            Set(SkelColorSwatch,   Config.Current.SkeletonColor);
            Set(SnapColorSwatch,   Config.Current.SnapColor);
            Set(GrenColorSwatch,   Config.Current.GrenadeColor);
        }

        // ---- Sounds ----

        private static readonly string AudioFilter =
            "Audio files|*.wav;*.mp3;*.ogg;*.flac;*.aiff;*.aif;*.wma;*.aac|All files|*.*";

        private void BrowseKillSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
                { Title = "Select kill sound", Filter = AudioFilter, RestoreDirectory = true };
            if (dlg.ShowDialog() == true)
                Config.Current.KillSoundPath = dlg.FileName;
        }

        private void BrowseWeaponSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
                { Title = "Select weapon equip sound", Filter = AudioFilter, RestoreDirectory = true };
            if (dlg.ShowDialog() == true)
                Config.Current.WeaponEquipSoundPath = dlg.FileName;
        }

        private void BrowseHitSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
                { Title = "Select hit sound", Filter = AudioFilter, RestoreDirectory = true };
            if (dlg.ShowDialog() == true)
                Config.Current.HitSoundPath = dlg.FileName;
        }

        // ---- Helpers ----

        private static SolidColorBrush HexBrush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(c);
        }

        // ---- Skin Changer UI ----

        private DispatcherTimer? _skinTimer;
        private int              _skinDisplayedWeaponKey;

        // wid==0 means "no item assigned" (CS2's default/unskinned starting knife) — map it to
        // SkinChanger.DefaultKnifeSlot so it doesn't collide with "no weapon held" everywhere.
        private static int ResolveSkinKey(ushort wid) => wid == 0 ? SkinChanger.DefaultKnifeSlot : wid;

        private void StartSkinTimer()
        {
            _skinTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _skinTimer.Tick += SkinTimer_Tick;
            _skinTimer.Start();
        }

        private void SkinTimer_Tick(object? sender, EventArgs e)
        {
            KillDebugLabel.Text =
                $"ctrl:{KillTracker.LastControllerFound}  raw:{KillTracker.LastRawKillCount}  " +
                $"totalDetected:{KillTracker.TotalDetectedKills}  totalPlaySoundCalls:{KillTracker.TotalPlaySoundCalls}";

            bool   hasWeapon = EntityReader.LocalWeaponPtr != IntPtr.Zero;
            ushort wid       = EntityReader.LocalWeaponId;
            int    key       = ResolveSkinKey(wid);

            string name = !hasWeapon
                ? "—"
                : wid == 0
                    ? "Default Knife (no item assigned)"
                    : $"{SkinChanger.GetWeaponName(wid)}  (ID {wid})";
            SkinWeaponLabel.Text = name;

            // Show read-back values so we can confirm writes are persisting in memory.
            uint kit = SkinChanger.LastPaintKitReadback;
            int  idh = SkinChanger.LastItemIDHighReadback;
            SkinDebugLabel.Text = kit == 0 && idh == 0
                ? ""
                : $"mem → kit:{kit}  itemIDHigh:0x{idh:X8}";

            SkinHudDebugLabel.Text =
                $"hud → arms:0x{SkinChanger.LastArmsHandle:X}  " +
                $"armsEnt:0x{SkinChanger.LastArmsEntity.ToInt64():X}  " +
                $"armsNode:0x{SkinChanger.LastArmsNode.ToInt64():X}  " +
                $"walked:{SkinChanger.LastChildrenWalked}  " +
                $"found:0x{SkinChanger.LastHudWeaponFound.ToInt64():X}";

            if (hasWeapon && key != _skinDisplayedWeaponKey)
            {
                _skinDisplayedWeaponKey = key;
                var preset = SkinChanger.GetPreset(key);
                SkinPaintKitBox.Text        = (preset?.PaintKit ?? 0).ToString();
                SkinWearSlider.Value        = preset?.Wear ?? 0.001;
                SkinStatTrakCheck.IsChecked = preset?.StatTrak >= 0;
                SkinStatTrakBox.Text        = Math.Max(0, preset?.StatTrak ?? 0).ToString();
                SkinLegacyModelCheck.IsChecked = preset?.LegacyModel ?? false;
            }
        }

        private void SkinWearSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
            => SkinWearLabel.Text = e.NewValue.ToString("0.000");

        private void SkinApply_Click(object sender, RoutedEventArgs e)
        {
            if (EntityReader.LocalWeaponPtr == IntPtr.Zero) return;   // truly no weapon held

            ushort wid = EntityReader.LocalWeaponId;
            int    key = ResolveSkinKey(wid);

            int.TryParse(SkinPaintKitBox.Text,  out int paintKit);
            int.TryParse(SkinStatTrakBox.Text,   out int statTrakVal);
            int statTrak = SkinStatTrakCheck.IsChecked == true ? statTrakVal : -1;

            var preset = new SkinPreset
            {
                PaintKit    = paintKit,
                Wear        = (float)SkinWearSlider.Value,
                Seed        = 0,
                StatTrak    = statTrak,
                LegacyModel = SkinLegacyModelCheck.IsChecked == true,
            };
            SkinChanger.SavePreset(key, preset);
            SkinChanger.ForceReapply();
            RefreshSkinList();
        }

        private void SkinClear_Click(object sender, RoutedEventArgs e)
        {
            if (EntityReader.LocalWeaponPtr == IntPtr.Zero) return;

            ushort wid = EntityReader.LocalWeaponId;
            int    key = ResolveSkinKey(wid);
            SkinChanger.RemovePreset(key);
            SkinPaintKitBox.Text           = "0";
            SkinWearSlider.Value           = 0.001;
            SkinStatTrakCheck.IsChecked    = false;
            SkinStatTrakBox.Text           = "0";
            SkinLegacyModelCheck.IsChecked = false;
            SkinChanger.ForceReapply();
            RefreshSkinList();
        }

        private void SkinBrowse_Click(object sender, RoutedEventArgs e)
        {
            bool   hasWeapon = EntityReader.LocalWeaponPtr != IntPtr.Zero;
            ushort wid       = EntityReader.LocalWeaponId;
            string name      = !hasWeapon ? "weapon" : SkinChanger.GetWeaponName(ResolveSkinKey(wid));

            var dlg = new SkinBrowserWindow(wid, name) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.SelectedEntry != null)
            {
                SkinPaintKitBox.Text           = dlg.SelectedEntry.PaintIndex.ToString();
                SkinLegacyModelCheck.IsChecked = dlg.SelectedEntry.LegacyModel;
            }
        }

        private void SkinRemove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is int key)
            {
                SkinChanger.RemovePreset(key);
                if (key == ResolveSkinKey(EntityReader.LocalWeaponId)) SkinChanger.ForceReapply();
                RefreshSkinList();
            }
        }

        private void RefreshSkinList()
        {
            var items = new List<SkinListItem>();
            foreach (var kv in Config.Current.SkinMap)
            {
                if (kv.Value.PaintKit == 0) continue;
                string st = kv.Value.StatTrak >= 0 ? $"  •  ST {kv.Value.StatTrak}" : "";
                items.Add(new SkinListItem
                {
                    WeaponId   = kv.Key,
                    WeaponName = SkinChanger.GetWeaponName(kv.Key),
                    Summary    = $"Kit {kv.Value.PaintKit}  •  {kv.Value.Wear:0.000}{st}",
                });
            }
            SkinList.ItemsSource        = items;
            SkinListEmpty.Visibility    = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private sealed class SkinListItem
        {
            public int    WeaponId   { get; init; }
            public string WeaponName { get; init; } = string.Empty;
            public string Summary    { get; init; } = string.Empty;
        }

        // ---- Shutdown ----

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_kbHook != IntPtr.Zero)
                UnhookWindowsHookEx(_kbHook);
            if (Config.Current.SaveOnExit)
                Config.Current.Save();
            GameContext.Detach();   // closes the Windows handle to cs2.exe
            _overlay?.Close();
            base.OnClosing(e);
            Environment.Exit(0);   // hard exit — no background threads linger
        }
    }
}
