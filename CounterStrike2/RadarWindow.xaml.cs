using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CounterStrike2.Entities;

namespace CounterStrike2
{
    public partial class RadarWindow : Window
    {
        private const double Radius     = 88;   // canvas is 176x176, centered
        private const double DotRadius  = 4;
        private const double WorldRange = 1500;  // world units shown at the outer ring

        private const ushort C4ItemDef = 49;

        private static readonly SolidColorBrush LocalBrush    = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush AllyBrush     = new(Color.FromRgb(0x4F, 0xC3, 0xF7));
        private static readonly SolidColorBrush EnemyBrush    = new(Color.FromRgb(0xFF, 0x4D, 0x4D));
        private static readonly SolidColorBrush BombPlantedBrush = new(Color.FromRgb(0xFF, 0x8C, 0x1A));
        private static readonly SolidColorBrush BombLooseBrush   = new(Color.FromRgb(0xE8, 0xD4, 0x4A));
        private static readonly SolidColorBrush FragBrush        = new(Color.FromRgb(0xFF, 0xDD, 0x00));
        private static readonly SolidColorBrush SmokeBrush       = new(Color.FromRgb(0xD2, 0xD2, 0xD2));

        public RadarWindow()
        {
            InitializeComponent();
            Left = Config.Current.RadarLeft ?? 20;
            Top  = Config.Current.RadarTop  ?? 60;

            LocationChanged += (_, _) =>
            {
                Config.Current.RadarLeft = Left;
                Config.Current.RadarTop  = Top;
            };
        }

        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        /// <summary>
        /// Redraw dots for every player, projected top-down and centered on the local player,
        /// rotated so the local player's facing direction always points "up" — matching CS2's
        /// own minimap rather than a fixed north-up layout.
        /// </summary>
        public void UpdateRadar(
            IReadOnlyList<PlayerEntity> players,
            float localYawDegrees,
            BombEntity? bomb,
            IReadOnlyList<DroppedWeaponEntity> loot,
            IReadOnlyList<GrenadeEntity> grenades)
        {
            // Remove only the dots from the previous frame — keep the static range-ring ellipses
            // (the first 3 children, added in XAML) untouched.
            while (RadarCanvas.Children.Count > 3)
                RadarCanvas.Children.RemoveAt(RadarCanvas.Children.Count - 1);

            PlayerEntity? local = null;
            int alive = 0;
            foreach (var p in players)
            {
                if (p.IsValid && p.IsLocal) local = p;
                if (p.IsValid && p.IsAlive) alive++;
            }

            if (local == null)
            {
                EmptyText.Visibility = Visibility.Visible;
                CountBadge.Text      = string.Empty;
                return;
            }

            EmptyText.Visibility = Visibility.Collapsed;
            CountBadge.Text       = $"({alive})";

            double scale  = Radius / WorldRange;
            double yawRad = localYawDegrees * Math.PI / 180.0;
            double sin    = Math.Sin(yawRad);
            double cos    = Math.Cos(yawRad);

            // Forward-direction wedge at the local player's position — always points "up" since
            // the whole radar is rotated to match facing direction.
            var wedge = new Polygon
            {
                Points = new PointCollection { new(Radius, Radius - 9), new(Radius - 5, Radius + 2), new(Radius + 5, Radius + 2) },
                Fill   = LocalBrush,
            };
            RadarCanvas.Children.Add(wedge);

            foreach (var p in players)
            {
                if (!p.IsValid) continue;
                if (p.IsLocal) continue;          // drawn separately as the forward wedge above
                if (!p.IsAlive) continue;          // declutter — skip dead teammates/enemies

                var (cx, cy) = Project(p.Origin, local.Origin, sin, cos, scale, DotRadius);
                Brush brush = p.Team == local.Team ? AllyBrush : EnemyBrush;

                var dot = new Ellipse
                {
                    Width  = DotRadius * 2,
                    Height = DotRadius * 2,
                    Fill   = brush,
                };
                Canvas.SetLeft(dot, cx - DotRadius);
                Canvas.SetTop(dot, cy - DotRadius);
                RadarCanvas.Children.Add(dot);
            }

            // C4 — planted (ticking or just defused) takes priority over a loose one on the
            // ground, since once planted the dropped-weapon scan no longer sees it as loot.
            const double bombSize = 7;
            Vector3? bombPos = null;
            Brush    bombBrush = BombLooseBrush;

            if (bomb != null)
            {
                bombPos   = bomb.Position;
                bombBrush = BombPlantedBrush;
            }
            else
            {
                foreach (var item in loot)
                {
                    if (item.WeaponId == C4ItemDef) { bombPos = item.Position; break; }
                }
            }

            if (bombPos != null)
            {
                var (cx, cy) = Project(bombPos.Value, local.Origin, sin, cos, scale, bombSize);
                var diamond = new Polygon
                {
                    Points = new PointCollection
                    {
                        new(cx, cy - bombSize), new(cx + bombSize, cy),
                        new(cx, cy + bombSize), new(cx - bombSize, cy),
                    },
                    Fill = bombBrush,
                };
                RadarCanvas.Children.Add(diamond);
            }

            // Grenades — small squares so they're visually distinct from the round player dots
            // and the diamond C4 marker. Smoke is always grey, frag matches the ESP's color.
            const double grenSize = 5;
            foreach (var g in grenades)
            {
                var (cx, cy) = Project(g.Position, local.Origin, sin, cos, scale, grenSize);
                var square = new Rectangle
                {
                    Width  = grenSize * 2,
                    Height = grenSize * 2,
                    Fill   = g.Type == GrenadeType.Smoke ? SmokeBrush : FragBrush,
                };
                Canvas.SetLeft(square, cx - grenSize);
                Canvas.SetTop(square, cy - grenSize);
                RadarCanvas.Children.Add(square);
            }
        }

        /// <summary>
        /// Rotate a world position relative to the local player into the local player's facing
        /// frame (forward = screen-up, right = screen-right) and clamp it inside the radar
        /// circle, leaving a margin equal to the marker's own radius.
        /// </summary>
        private static (double X, double Y) Project(
            Vector3 world, Vector3 localOrigin, double sin, double cos, double scale, double margin)
        {
            double relX = world.X - localOrigin.X;
            double relY = world.Y - localOrigin.Y;

            double fwd   = relX * cos + relY * sin;
            double right = relX * -sin + relY * cos;

            double dx = right * scale;
            double dy = fwd   * scale;

            double len = Math.Sqrt(dx * dx + dy * dy);
            double max = Radius - margin - 2;
            if (len > max && len > 0)
            {
                double clampScale = max / len;
                dx *= clampScale;
                dy *= clampScale;
            }

            return (Radius - dx, Radius - dy);   // both axes mirrored to match the confirmed-correct player projection
        }
    }
}
