using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CounterStrike2.Skins;

namespace CounterStrike2
{
    public partial class SkinBrowserWindow : Window
    {
        public SkinCatalogEntry? SelectedEntry { get; private set; }

        private List<SkinCatalogEntry> _currentWeaponSkins = new();

        public SkinBrowserWindow(int initialWeaponId, string initialWeaponName)
        {
            InitializeComponent();
            SubtitleText.Text = $"Browsing {initialWeaponName} skins — click one to apply";
            Loaded += async (_, _) => await LoadAsync(initialWeaponId);
        }

        private void OnDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async System.Threading.Tasks.Task LoadAsync(int initialWeaponId)
        {
            await SkinCatalog.EnsureLoadedAsync();

            if (!SkinCatalog.IsLoaded || SkinCatalog.GetWeaponList().Count == 0)
            {
                StatusText.Text = string.IsNullOrEmpty(SkinCatalog.LastError)
                    ? "No skin data available."
                    : $"Couldn't load skin database: {SkinCatalog.LastError}";
                return;
            }

            var weapons = SkinCatalog.GetWeaponList();
            WeaponCombo.ItemsSource = weapons;

            var match = weapons.FirstOrDefault(w => w.Id == initialWeaponId);
            WeaponCombo.SelectedItem = match ?? weapons[0];
            // SelectionChanged fires RefreshGrid(); if it was already selected (no-op), force it once.
            if (match == null || WeaponCombo.SelectedItem == match)
                RefreshGrid();
        }

        private void WeaponCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
            => RefreshGrid();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => ApplyFilter();

        private void RefreshGrid()
        {
            if (WeaponCombo.SelectedValue is not int weaponId) return;
            _currentWeaponSkins = SkinCatalog.GetForWeapon(weaponId);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchBox.Text?.Trim() ?? string.Empty;
            var filtered = query.Length == 0
                ? _currentWeaponSkins
                : _currentWeaponSkins
                    .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            SkinGrid.ItemsSource = filtered;

            bool hasItems = filtered.Count > 0;
            GridScroller.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Visibility   = hasItems ? Visibility.Collapsed : Visibility.Visible;
            if (!hasItems)
                StatusText.Text = _currentWeaponSkins.Count == 0
                    ? "No skins found for this weapon."
                    : "No skins match your search.";
        }

        private void Tile_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not SkinCatalogEntry entry) return;
            SelectedEntry = entry;
            DialogResult = true;
            Close();
        }
    }
}
