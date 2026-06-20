using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using CounterStrike2.Entities;

namespace CounterStrike2.Rendering
{
    public sealed class EspLayer : FrameworkElement
    {
        private readonly DrawingVisual _dv = new();

        // ── Static frozen resources (shared, zero per-frame alloc) ──
        private static readonly SolidColorBrush _hpBg       = Freeze(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)));
        private static readonly SolidColorBrush _debugDot   = Freeze(new SolidColorBrush(Color.FromArgb(255, 255, 220, 0)));   // yellow
        // C4 carrier badge
        private static readonly SolidColorBrush _c4BgBrush  = Freeze(new SolidColorBrush(Color.FromArgb(220, 255, 160,   0)));  // orange fill
        private static readonly SolidColorBrush _c4TextBrush = Freeze(new SolidColorBrush(Color.FromArgb(255,  20,  10,   0)));  // near-black text
        // Loot colours
        private static readonly SolidColorBrush _lootBrush  = Freeze(new SolidColorBrush(Color.FromArgb(255, 255, 255, 180)));  // pale yellow
        private static readonly Pen             _lootDotPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 180)), 1.0));

        // Grenade colours — smoke is always grey; frag uses the configurable _grenFill/_grenLabel
        private static readonly SolidColorBrush _smokeFill    = Freeze(new SolidColorBrush(Color.FromArgb(200, 210, 210, 210)));
        private static readonly Pen             _smokeLandPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(180, 210, 210, 210)), 1.5));
        private static readonly SolidColorBrush _smokeLabel   = Freeze(new SolidColorBrush(Color.FromArgb(255, 210, 210, 210)));
        // Bomb colours
        private static readonly SolidColorBrush _bombRedBrush  = Freeze(new SolidColorBrush(Color.FromArgb(255, 255,  50,  50)));
        private static readonly SolidColorBrush _bombOrgBrush  = Freeze(new SolidColorBrush(Color.FromArgb(255, 255, 160,   0)));
        private static readonly SolidColorBrush _bombCyanBrush = Freeze(new SolidColorBrush(Color.FromArgb(255,   0, 220, 220)));
        private static readonly SolidColorBrush _bombGrnBrush  = Freeze(new SolidColorBrush(Color.FromArgb(255,  79, 227, 106)));
        private static readonly Pen             _bombDotPen    = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 50, 50)), 1.5));
        private static readonly Typeface _font = new("Consolas");

        // ── Per-instance cached pens/brushes (rebuilt when any color or thickness changes) ──
        private string _lastColorKey = string.Empty;

        private Pen             _boxPen      = MakePen(Color.FromArgb(255, 255, 68, 68), 1.5);
        private Pen             _bonePen     = MakePen(Color.FromArgb(180, 255, 68, 68), 1.0);
        private Pen             _snapPen     = MakePen(Color.FromArgb(100, 255, 68, 68), 1.0);
        private SolidColorBrush _healthBrush = new(Color.FromArgb(255, 79, 227, 106));
        private SolidColorBrush _nameBrushD  = new(Colors.White);
        private SolidColorBrush _distBrushD  = new(Color.FromArgb(255, 170, 96, 255));
        private SolidColorBrush _weapBrushD  = new(Color.FromArgb(255, 30, 100, 220));
        private SolidColorBrush _grenFill    = new(Color.FromArgb(220, 255, 221, 0));
        private Pen             _grenLandPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 221, 0)), 1.5));
        private SolidColorBrush _grenLabel   = new(Color.FromArgb(255, 255, 221, 0));

        // ── Cached DPI — computed once, reset if DPI changes ──
        private double _dpi;

        // ── FormattedText caches — keyed by the display string ──
        // C4 badge label — rebuilt only when DPI changes.
        private FormattedText? _ftC4;
        private double         _ftC4Dpi;

        private readonly Dictionary<string, FormattedText> _ftCache      = new(16,  StringComparer.Ordinal);
        private readonly Dictionary<string, FormattedText> _ftWeaponCache = new(32,  StringComparer.Ordinal);
        private readonly Dictionary<string, FormattedText> _ftDistCache   = new(256, StringComparer.Ordinal);

        public EspLayer()
        {
            AddVisualChild(_dv);
            AddLogicalChild(_dv);
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _dv;

        public void Clear()
        {
            using var dc = _dv.RenderOpen();
            // Empty open+close clears all previous drawing commands.
        }

        public void Redraw(IReadOnlyList<PlayerEntity> players, IReadOnlyList<GrenadeEntity> grenades,
                           IReadOnlyList<DroppedWeaponEntity> loot, BombEntity? bomb,
                           Matrix4x4 vm, Config cfg, Rect gameRect)
        {
            using var dc = _dv.RenderOpen();

            if (!cfg.EspEnabled) return;

            double gx = gameRect.X;
            double gy = gameRect.Y;
            double gw = gameRect.Width;
            double gh = gameRect.Height;
            if (gw <= 0 || gh <= 0) return;

            // Rebuild pens/brushes when any color or thickness changes.
            double thick = cfg.BoxThickness;
            string colorKey = $"{thick}|{cfg.BoxColor}|{cfg.HealthColor}|{cfg.NameColor}|{cfg.DistanceColor}|{cfg.WeaponColor}|{cfg.SkeletonColor}|{cfg.SnapColor}|{cfg.GrenadeColor}";
            if (colorKey != _lastColorKey)
            {
                _lastColorKey = colorKey;
                double boneThick = Math.Max(1.0, thick - 0.5);

                var boxC  = ParseHex(cfg.BoxColor,      Color.FromArgb(255, 255,  68,  68));
                var hlthC = ParseHex(cfg.HealthColor,   Color.FromArgb(255,  79, 227, 106));
                var nameC = ParseHex(cfg.NameColor,     Colors.White);
                var distC = ParseHex(cfg.DistanceColor, Color.FromArgb(255, 170,  96, 255));
                var weapC = ParseHex(cfg.WeaponColor,   Color.FromArgb(255,  30, 100, 220));
                var skelC = ParseHex(cfg.SkeletonColor, Color.FromArgb(255, 255,  68,  68));
                var snapC = ParseHex(cfg.SnapColor,     Color.FromArgb(255, 255,  68,  68));
                var grenC = ParseHex(cfg.GrenadeColor,  Color.FromArgb(255, 255, 221,   0));

                _boxPen      = MakePen(boxC, thick);
                _bonePen     = MakePen(Color.FromArgb(180, skelC.R, skelC.G, skelC.B), boneThick);
                _snapPen     = MakePen(Color.FromArgb(100, snapC.R, snapC.G, snapC.B), 1.0);
                _healthBrush = new SolidColorBrush(hlthC);
                _nameBrushD  = new SolidColorBrush(nameC);
                _distBrushD  = new SolidColorBrush(distC);
                _weapBrushD  = new SolidColorBrush(weapC);
                _grenFill    = new SolidColorBrush(Color.FromArgb(220, grenC.R, grenC.G, grenC.B));
                _grenLandPen = new Pen(new SolidColorBrush(Color.FromArgb(180, grenC.R, grenC.G, grenC.B)), 1.5);
                _grenLabel   = new SolidColorBrush(grenC);

                _ftCache.Clear();
                _ftWeaponCache.Clear();
                _ftDistCache.Clear();
            }

            // DPI — lazily resolved once; reset _dpi to 0 to force refresh.
            if (_dpi <= 0)
            {
                var src = PresentationSource.FromVisual(this);
                _dpi = src?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            }

            // Find local player's world position and team for distance + team filter.
            var localOrigin = Vector3.Zero;
            int localTeam   = 0;
            foreach (var p in players)
                if (p.IsLocal) { localOrigin = p.Origin; localTeam = p.Team; break; }

            // Evict distance cache if it grows large (bounded by seen distances in metres).
            if (_ftDistCache.Count > 400) _ftDistCache.Clear();

            foreach (var p in players)
            {
                if (!p.IsValid || p.IsLocal || !p.IsAlive) continue;
                if (!cfg.EspTeam && localTeam != 0 && p.Team == localTeam) continue;

                bool footOk = WorldToScreen.Project(
                    p.Origin, vm, (float)gw, (float)gh, out var foot);
                bool headOk = WorldToScreen.Project(
                    new Vector3(p.Origin.X, p.Origin.Y, p.Origin.Z + 72f),
                    vm, (float)gw, (float)gh, out var head);

                if (!footOk || !headOk) continue;

                foot = new Point(foot.X + gx, foot.Y + gy);
                head = new Point(head.X + gx, head.Y + gy);

                double boxH = foot.Y - head.Y;
                if (boxH < 4) continue;

                double boxW    = boxH * 0.40;
                double centerX = (head.X + foot.X) * 0.5;
                double boxX    = centerX - boxW * 0.5;
                double boxY    = head.Y;
                var    rect = new Rect(boxX, boxY, boxW, boxH);

                if (cfg.EspBox)
                    dc.DrawRectangle(null, _boxPen, rect);

                // ── Skeleton ──────────────────────────────────────────────────
                if (cfg.EspSkeleton && p.Bones.Valid)
                {
                    // Debug mode: draw all 48 bones as yellow numbered dots.
                    if (cfg.BoneDebug && p.Bones.DebugBones != null)
                    {
                        for (int bi = 0; bi < p.Bones.DebugBones.Length; bi++)
                        {
                            if (!WorldToScreen.Project(p.Bones.DebugBones[bi],
                                    vm, (float)gw, (float)gh, out var dbPos)) continue;
                            double dx = dbPos.X + gx;
                            double dy = dbPos.Y + gy;
                            dc.DrawEllipse(_debugDot, null, new Point(dx, dy), 3, 3);
                            var lbl = new FormattedText(bi.ToString(),
                                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                _font, 9, _debugDot, _dpi);
                            dc.DrawText(lbl, new Point(dx + 4, dy - lbl.Height * 0.5));
                        }
                        // Skip normal skeleton in debug mode — just dots.
                        goto SkeletonDone;
                    }

                    var bp = _bonePen;
                    DrawBone(dc, p.Bones.Head,      p.Bones.Neck,      vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.Neck,      p.Bones.Chest,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.Chest,     p.Bones.Waist,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.Neck,      p.Bones.LShoulder, vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.LShoulder, p.Bones.LElbow,    vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.LElbow,    p.Bones.LHand,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.Neck,      p.Bones.RShoulder, vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.RShoulder, p.Bones.RElbow,    vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.RElbow,    p.Bones.RHand,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.Waist,     p.Bones.LHip,      vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.LHip,      p.Bones.LKnee,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.LKnee,     p.Bones.LFoot,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.Waist,     p.Bones.RHip,      vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.RHip,      p.Bones.RKnee,     vm, gw, gh, gx, gy, bp);
                    DrawBone(dc, p.Bones.RKnee,     p.Bones.RFoot,     vm, gw, gh, gx, gy, bp);

                    // Head circle — radius scales with box height so it looks correct at any distance
                    if (WorldToScreen.Project(p.Bones.Head, vm, (float)gw, (float)gh, out var hbPos))
                    {
                        double headR = Math.Max(3.5, boxH * 0.095);
                        dc.DrawEllipse(null, bp, new Point(hbPos.X + gx, hbPos.Y + gy), headR, headR);
                    }

                    SkeletonDone:;
                }

                if (cfg.EspHealth)
                {
                    double frac   = Math.Clamp(p.Health / 100.0, 0, 1);
                    double hpBarH = boxH * frac;
                    double hpX    = boxX - 6;
                    dc.DrawRectangle(_hpBg, null, new Rect(hpX, boxY, 4, boxH));
                    dc.DrawRectangle(_healthBrush, null,
                        new Rect(hpX, boxY + boxH - hpBarH, 4, hpBarH));
                }

                if (cfg.EspName && !string.IsNullOrEmpty(p.Name))
                {
                    if (!_ftCache.TryGetValue(p.Name, out var ft))
                    {
                        ft = new FormattedText(p.Name,
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            _font, 11, _nameBrushD, _dpi);
                        _ftCache[p.Name] = ft;
                    }
                    dc.DrawText(ft, new Point(head.X - ft.Width * 0.5, boxY - ft.Height - 2));
                }

                // ── C4 carrier badge ──────────────────────────────────────────
                if (cfg.EspBombCarrier && p.HasC4)
                {
                    if (_ftC4 == null || Math.Abs(_ftC4Dpi - _dpi) > 0.001)
                    {
                        _ftC4    = new FormattedText("C4", CultureInfo.InvariantCulture,
                                       FlowDirection.LeftToRight, _font, 10, _c4TextBrush, _dpi);
                        _ftC4Dpi = _dpi;
                    }
                    double badgeW = _ftC4.Width + 10;
                    double badgeH = _ftC4.Height + 4;
                    double badgeX = head.X - badgeW * 0.5;
                    double badgeY = boxY - badgeH - 2;
                    if (cfg.EspName && !string.IsNullOrEmpty(p.Name))
                        badgeY -= _ftC4.Height + 4; // stack above name
                    dc.DrawRoundedRectangle(_c4BgBrush, null,
                        new Rect(badgeX, badgeY, badgeW, badgeH), 3, 3);
                    dc.DrawText(_ftC4, new Point(badgeX + 5, badgeY + 2));
                }

                // ── Info line: weapon (dark blue) + distance (purple), centred below box ──
                bool showWeapon = cfg.EspWeapon;
                bool showDist   = cfg.EspDistance;
                if (showWeapon || showDist)
                {
                    float  dist      = Vector3.Distance(p.Origin, localOrigin) / 39.37f;
                    string weaponStr = showWeapon ? WeaponNames.Get(p.WeaponId) : string.Empty;
                    double infoY     = boxY + boxH + 2;

                    FormattedText? fweap = null;
                    if (showWeapon && weaponStr.Length > 0)
                    {
                        if (!_ftWeaponCache.TryGetValue(weaponStr, out fweap))
                        {
                            fweap = new FormattedText(weaponStr,
                                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                _font, 11, _weapBrushD, _dpi);
                            _ftWeaponCache[weaponStr] = fweap;
                        }
                    }

                    FormattedText? fdist = null;
                    if (showDist)
                    {
                        string distStr = $"{dist:F0}m";
                        if (!_ftDistCache.TryGetValue(distStr, out fdist))
                        {
                            fdist = new FormattedText(distStr,
                                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                _font, 11, _distBrushD, _dpi);
                            _ftDistCache[distStr] = fdist;
                        }
                    }

                    const double gap = 5;
                    double totalW = (fweap?.Width ?? 0)
                                  + (fweap != null && fdist != null ? gap : 0)
                                  + (fdist?.Width ?? 0);
                    double startX = head.X - totalW * 0.5;

                    if (fweap != null)
                    {
                        dc.DrawText(fweap, new Point(startX, infoY));
                        startX += fweap.Width + gap;
                    }
                    if (fdist != null)
                        dc.DrawText(fdist, new Point(startX, infoY));
                }

                if (cfg.EspSnapLines)
                {
                    bool snapTop = cfg.SnapLinePos == "Top";
                    var  origin  = snapTop ? new Point(gx + gw * 0.5, gy)
                                           : new Point(gx + gw * 0.5, gy + gh);
                    var  target  = snapTop ? head : foot;
                    dc.DrawLine(_snapPen, origin, target);
                }
            }

            // ── Loot ESP ──────────────────────────────────────────────────
            if (cfg.EspLoot)
            {
                foreach (var w in loot)
                {
                    if (!WorldToScreen.Project(w.Position, vm, (float)gw, (float)gh, out var wPos))
                        continue;

                    double sx = wPos.X + gx;
                    double sy = wPos.Y + gy;

                    // Diamond dot
                    dc.DrawEllipse(_lootBrush, null, new Point(sx, sy), 4, 4);

                    // Weapon name
                    if (!_ftWeaponCache.TryGetValue(w.WeaponName, out var ftName))
                    {
                        ftName = new FormattedText(w.WeaponName,
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, _font, 13, _lootBrush, _dpi);
                        _ftWeaponCache[w.WeaponName] = ftName;
                    }
                    dc.DrawText(ftName, new Point(sx - ftName.Width * 0.5, sy - 4 - ftName.Height));

                    // Distance below the dot (reuse local player origin computed above)
                    if (cfg.EspDistance && localOrigin != Vector3.Zero)
                    {
                        float  dist    = Vector3.Distance(w.Position, localOrigin) / 39.37f;
                        string distStr = $"{dist:F0}m";
                        if (!_ftDistCache.TryGetValue(distStr, out var ftDist))
                        {
                            ftDist = new FormattedText(distStr,
                                System.Globalization.CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, _font, 12, _distBrushD, _dpi);
                            _ftDistCache[distStr] = ftDist;
                        }
                        dc.DrawText(ftDist, new Point(sx - ftDist.Width * 0.5, sy + 6));
                    }
                }
            }

            // ── Grenade ESP ───────────────────────────────────────────────
            if (cfg.EspGrenades)
            {
                foreach (var g in grenades)
                {
                    if (!WorldToScreen.Project(g.Position, vm, (float)gw, (float)gh, out var gPos))
                        continue;

                    bool hasLand = WorldToScreen.Project(g.Landing, vm, (float)gw, (float)gh, out var gLand);

                    double sx = gPos.X + gx;
                    double sy = gPos.Y + gy;

                    bool isSmoke = g.Type == GrenadeType.Smoke;
                    var  fill    = isSmoke ? _smokeFill  : _grenFill;
                    var  landPen = isSmoke ? _smokeLandPen : _grenLandPen;
                    var  lblBrush= isSmoke ? _smokeLabel : _grenLabel;

                    // Draw arc line from current position to predicted landing.
                    if (hasLand)
                    {
                        double lx = gLand.X + gx;
                        double ly = gLand.Y + gy;
                        dc.DrawLine(landPen, new Point(sx, sy), new Point(lx, ly));

                        // Landing circle (outline only).
                        dc.DrawEllipse(null, landPen, new Point(lx, ly), 7, 7);
                    }

                    // Grenade dot at current position.
                    dc.DrawEllipse(fill, null, new Point(sx, sy), 5, 5);

                    // Label above dot.
                    string label = isSmoke ? "SMOKE" : "NADE";
                    if (!_ftWeaponCache.TryGetValue(label, out var ft))
                    {
                        ft = new FormattedText(label,
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, _font, 10, lblBrush, _dpi);
                        _ftWeaponCache[label] = ft;
                    }
                    dc.DrawText(ft, new Point(sx - ft.Width * 0.5, sy - 5 - ft.Height));
                }
            }

            // ── Bomb ESP ──────────────────────────────────────────────────
            if (cfg.EspBomb && bomb != null)
            {
                if (WorldToScreen.Project(bomb.Position, vm, (float)gw, (float)gh, out var bPos))
                {
                    double bx = bPos.X + gx;
                    double by = bPos.Y + gy;

                    // Pulsing dot: red when ticking, grey when not yet armed.
                    var dotFill = bomb.IsTicking ? _bombRedBrush : _lootBrush;
                    dc.DrawEllipse(dotFill, _bombDotPen, new Point(bx, by), 6, 6);

                    // Site label: "BOMB A" / "BOMB B"
                    string siteLabel = bomb.Site == 0 ? "BOMB A" : "BOMB B";
                    if (!_ftWeaponCache.TryGetValue(siteLabel, out var ftSite))
                    {
                        ftSite = new FormattedText(siteLabel,
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            _font, 16, _bombRedBrush, _dpi);
                        _ftWeaponCache[siteLabel] = ftSite;
                    }
                    double labelY = by - 6 - ftSite.Height;
                    dc.DrawText(ftSite, new Point(bx - ftSite.Width * 0.5, labelY));

                    double lineY = by + 8;

                    if (bomb.IsDefused)
                    {
                        // Green "DEFUSED" — round is over.
                        const string defusedStr = "DEFUSED";
                        if (!_ftDistCache.TryGetValue(defusedStr, out var ftDef))
                        {
                            ftDef = new FormattedText(defusedStr,
                                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                _font, 14, _bombGrnBrush, _dpi);
                            _ftDistCache[defusedStr] = ftDef;
                        }
                        dc.DrawText(ftDef, new Point(bx - ftDef.Width * 0.5, lineY));
                    }
                    else if (bomb.IsTicking)
                    {
                        // Countdown "32.5s" — orange when >10s, red when ≤10s.
                        string timerStr = $"{bomb.TimeRemaining:F1}s";
                        var    timerClr = bomb.TimeRemaining > 10f ? _bombOrgBrush : _bombRedBrush;
                        if (!_ftDistCache.TryGetValue(timerStr, out var ftTimer))
                        {
                            ftTimer = new FormattedText(timerStr,
                                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                _font, 16, timerClr, _dpi);
                            _ftDistCache[timerStr] = ftTimer;
                        }
                        dc.DrawText(ftTimer, new Point(bx - ftTimer.Width * 0.5, lineY));
                        lineY += ftTimer.Height + 2;

                        if (bomb.IsBeingDefused)
                        {
                            string defStr = $"DEFUSING {bomb.DefuseTime:F1}s";
                            if (!_ftDistCache.TryGetValue(defStr, out var ftDfz))
                            {
                                ftDfz = new FormattedText(defStr,
                                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                    _font, 14, _bombCyanBrush, _dpi);
                                _ftDistCache[defStr] = ftDfz;
                            }
                            dc.DrawText(ftDfz, new Point(bx - ftDfz.Width * 0.5, lineY));
                        }
                    }
                }
            }
        }

        // Project both endpoints and draw only when they're plausibly adjacent joints.
        // Adjacent joints in CS2 are never more than ~80 units apart; zero-initialized
        // bones at world origin would be thousands of units from any real bone.
        private static void DrawBone(DrawingContext dc,
            Vector3 a, Vector3 b, Matrix4x4 vm,
            double gw, double gh, double gx, double gy, Pen pen)
        {
            if (Vector3.DistanceSquared(a, b) > 80f * 80f) return;
            if (!WorldToScreen.Project(a, vm, (float)gw, (float)gh, out var sa)) return;
            if (!WorldToScreen.Project(b, vm, (float)gw, (float)gh, out var sb)) return;
            dc.DrawLine(pen,
                new Point(sa.X + gx, sa.Y + gy),
                new Point(sb.X + gx, sb.Y + gy));
        }

        private static Color ParseHex(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return fallback; }
        }

        private static Pen MakePen(Color c, double thickness)
        {
            var p = new Pen(new SolidColorBrush(c), thickness);
            p.Freeze();
            return p;
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
        private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
    }
}
