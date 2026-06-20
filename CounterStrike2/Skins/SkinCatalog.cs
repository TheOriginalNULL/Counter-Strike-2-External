using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CounterStrike2.Skins
{
    /// <summary>One distinct skin: a (weapon, paint kit) pair with display metadata.</summary>
    public sealed class SkinCatalogEntry
    {
        public int    WeaponId    { get; set; }
        public string WeaponName  { get; set; } = string.Empty;
        public int    PaintIndex  { get; set; }
        public string Name        { get; set; } = string.Empty;
        public string ImageUrl    { get; set; } = string.Empty;
        public string RarityColor { get; set; } = "#7C7C7C";
        public bool   LegacyModel { get; set; }
    }

    /// <summary>A weapon entry for the picker dropdown — a plain class so WPF's reflection-based
    /// binding (DisplayMemberPath/SelectedValuePath) can see named properties (a ValueTuple's
    /// element names are compile-time aliases only and aren't visible to runtime binding).</summary>
    public sealed class WeaponListEntry
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Skin name/image/paint-kit database sourced from the public ByMykel/CSGO-API project
    /// (https://github.com/ByMykel/CSGO-API), cached to disk for a week at a time so the
    /// browser doesn't need network access on every launch.
    /// </summary>
    public static class SkinCatalog
    {
        private const string SourceUrl =
            "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/skins.json";

        private static readonly string CacheDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stratus");
        private static readonly string CacheFile = Path.Combine(CacheDir, "skin_catalog.json");
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

        private static readonly object _lock = new();
        private static List<SkinCatalogEntry>? _entries;

        public static bool   IsLoaded  => _entries != null;
        public static string? LastError { get; private set; }

        /// <summary>Load from disk cache if fresh, otherwise download. Safe to call repeatedly — only does work once.</summary>
        public static async Task EnsureLoadedAsync()
        {
            lock (_lock) { if (_entries != null) return; }

            var loaded = TryLoadCache();
            if (loaded == null)
            {
                loaded = await DownloadAsync();
                if (loaded != null) SaveCache(loaded);
            }

            lock (_lock) { _entries ??= loaded ?? new List<SkinCatalogEntry>(); }
        }

        public static List<SkinCatalogEntry> GetForWeapon(int weaponId)
        {
            lock (_lock)
            {
                if (_entries == null) return new List<SkinCatalogEntry>();
                return _entries.Where(e => e.WeaponId == weaponId)
                                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
        }

        /// <summary>Distinct weapons present in the catalog, for a weapon picker dropdown.</summary>
        public static List<WeaponListEntry> GetWeaponList()
        {
            lock (_lock)
            {
                if (_entries == null) return new List<WeaponListEntry>();
                return _entries
                    .GroupBy(e => e.WeaponId)
                    .Select(g => new WeaponListEntry { Id = g.Key, Name = g.First().WeaponName })
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static List<SkinCatalogEntry>? TryLoadCache()
        {
            try
            {
                if (!File.Exists(CacheFile)) return null;
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) > CacheLifetime) return null;
                var json = File.ReadAllText(CacheFile);
                return JsonSerializer.Deserialize<List<SkinCatalogEntry>>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveCache(List<SkinCatalogEntry> entries)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(entries));
            }
            catch
            {
                // Non-fatal — just means we re-download next launch.
            }
        }

        private static async Task<List<SkinCatalogEntry>?> DownloadAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var json = await http.GetStringAsync(SourceUrl);
                using var doc = JsonDocument.Parse(json);

                var result = new List<SkinCatalogEntry>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("weapon", out var weaponEl)) continue;
                    if (!weaponEl.TryGetProperty("weapon_id", out var widEl) || widEl.ValueKind != JsonValueKind.Number) continue;
                    if (!item.TryGetProperty("paint_index", out var paintEl)) continue;
                    if (!int.TryParse(paintEl.GetString(), out int paintIndex) || paintIndex == 0) continue;

                    string name       = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    string weaponName = weaponEl.TryGetProperty("name", out var wNameEl) ? wNameEl.GetString() ?? "" : "";
                    string image      = item.TryGetProperty("image", out var imgEl) ? imgEl.GetString() ?? "" : "";
                    bool   legacy     = item.TryGetProperty("legacy_model", out var legEl) && legEl.ValueKind == JsonValueKind.True;

                    string color = "#7C7C7C";
                    if (item.TryGetProperty("rarity", out var rarEl) && rarEl.TryGetProperty("color", out var colorEl))
                        color = colorEl.GetString() ?? color;

                    result.Add(new SkinCatalogEntry
                    {
                        WeaponId    = widEl.GetInt32(),
                        WeaponName  = weaponName,
                        PaintIndex  = paintIndex,
                        Name        = name,
                        ImageUrl    = image,
                        RarityColor = color,
                        LegacyModel = legacy,
                    });
                }

                LastError = null;
                return result;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }
}
