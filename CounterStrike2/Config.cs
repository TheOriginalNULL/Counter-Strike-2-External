using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrike2.Skins;

namespace CounterStrike2
{
    /// <summary>
    /// Holds all menu state and persists it to disk as JSON.
    /// The UI binds directly to these properties, so flipping a toggle
    /// updates the value that a render loop would later read.
    /// </summary>
    public class Config : INotifyPropertyChanged
    {
        // ---- Visuals / ESP ----
        private bool _espEnabled = true;
        private bool _espBox = true;
        private bool _espHealth = true;
        private bool _espName;
        private bool _espDistance;
        private bool _espWeapon;
        private bool _espSkeleton;
        private bool _boneDebug;
        private bool _espBombCarrier = true;
        private bool _espGrenades;
        private bool   _espSnapLines;
        private string _snapLinePos = "Bottom";
        private bool   _espTeam;
        private double _boxThickness = 1.5;

        public bool EspEnabled { get => _espEnabled; set => Set(ref _espEnabled, value); }
        public bool EspBox { get => _espBox; set => Set(ref _espBox, value); }
        public bool EspHealth { get => _espHealth; set => Set(ref _espHealth, value); }
        public bool EspName { get => _espName; set => Set(ref _espName, value); }
        public bool EspDistance  { get => _espDistance;  set => Set(ref _espDistance,  value); }
        public bool EspWeapon    { get => _espWeapon;    set => Set(ref _espWeapon,    value); }
        public bool EspSkeleton  { get => _espSkeleton;  set => Set(ref _espSkeleton,  value); }
        /// <summary>Draw all bone indices 0-47 as yellow numbered dots to identify unknown indices.</summary>
        public bool BoneDebug    { get => _boneDebug;    set => Set(ref _boneDebug,    value); }
        public bool EspBombCarrier { get => _espBombCarrier; set => Set(ref _espBombCarrier, value); }
        public bool EspGrenades   { get => _espGrenades;   set => Set(ref _espGrenades,   value); }
        public bool   EspSnapLines { get => _espSnapLines; set => Set(ref _espSnapLines, value); }
        public string SnapLinePos  { get => _snapLinePos;  set => Set(ref _snapLinePos,  value); }
        public bool   EspTeam      { get => _espTeam;      set => Set(ref _espTeam,      value); }
        public double BoxThickness { get => _boxThickness; set => Set(ref _boxThickness, value); }

        // ---- World ----
        private bool _droppedWeapons;
        private bool _espBomb;
        private bool _bombMarker;
        private bool _spectatorList;
        private bool   _radarEnabled;
        private double _radarOpacity = 0.85;

        public bool EspLoot { get => _droppedWeapons; set => Set(ref _droppedWeapons, value); }
        public bool EspBomb { get => _espBomb; set => Set(ref _espBomb, value); }
        public bool BombMarker { get => _bombMarker; set => Set(ref _bombMarker, value); }
        public bool SpectatorList { get => _spectatorList; set => Set(ref _spectatorList, value); }
        public bool   RadarEnabled { get => _radarEnabled; set => Set(ref _radarEnabled, value); }
        public double RadarOpacity { get => _radarOpacity; set => Set(ref _radarOpacity, value); }

        // Null = never moved yet, use the window's built-in default position.
        private double? _radarLeft;
        private double? _radarTop;
        public double? RadarLeft { get => _radarLeft; set => Set(ref _radarLeft, value); }
        public double? RadarTop  { get => _radarTop;  set => Set(ref _radarTop,  value); }

        // ---- Kill sound ----
        private bool   _killSound;
        private string _killSoundPath = string.Empty;

        public bool   KillSound     { get => _killSound;     set => Set(ref _killSound, value); }
        public string KillSoundPath
        {
            get => _killSoundPath;
            set { Set(ref _killSoundPath, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KillSoundFileName))); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string KillSoundFileName => string.IsNullOrEmpty(_killSoundPath)
            ? "No file selected"
            : Path.GetFileName(_killSoundPath);

        // ---- Weapon equip sound ----
        private bool   _weaponEquipSound;
        private string _weaponEquipSoundPath = string.Empty;

        public bool   WeaponEquipSound     { get => _weaponEquipSound;     set => Set(ref _weaponEquipSound, value); }
        public string WeaponEquipSoundPath
        {
            get => _weaponEquipSoundPath;
            set { Set(ref _weaponEquipSoundPath, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WeaponEquipSoundFileName))); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string WeaponEquipSoundFileName => string.IsNullOrEmpty(_weaponEquipSoundPath)
            ? "No file selected"
            : Path.GetFileName(_weaponEquipSoundPath);

        // ---- Hit indicator / hit sound ----
        private bool   _hitIndicator;
        private bool   _hitSound;
        private string _hitSoundPath = string.Empty;

        public bool HitIndicator { get => _hitIndicator; set => Set(ref _hitIndicator, value); }
        public bool HitSound     { get => _hitSound;      set => Set(ref _hitSound,     value); }
        public string HitSoundPath
        {
            get => _hitSoundPath;
            set { Set(ref _hitSoundPath, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HitSoundFileName))); }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public string HitSoundFileName => string.IsNullOrEmpty(_hitSoundPath)
            ? "No file selected"
            : Path.GetFileName(_hitSoundPath);

        // ---- ESP Colors (per-feature) ----
        private string _boxColor      = "#FF4444";
        private string _healthColor   = "#4FE36A";
        private string _nameColor     = "#FFFFFF";
        private string _distanceColor = "#AA60FF";
        private string _weaponColor   = "#1E64DC";
        private string _skeletonColor = "#FF4444";
        private string _snapColor     = "#FF4444";
        private string _grenadeColor  = "#FFDD00";

        public string BoxColor      { get => _boxColor;      set => Set(ref _boxColor,      value); }
        public string HealthColor   { get => _healthColor;   set => Set(ref _healthColor,   value); }
        public string NameColor     { get => _nameColor;     set => Set(ref _nameColor,     value); }
        public string DistanceColor { get => _distanceColor; set => Set(ref _distanceColor, value); }
        public string WeaponColor   { get => _weaponColor;   set => Set(ref _weaponColor,   value); }
        public string SkeletonColor { get => _skeletonColor; set => Set(ref _skeletonColor, value); }
        public string SnapColor     { get => _snapColor;     set => Set(ref _snapColor,     value); }
        public string GrenadeColor  { get => _grenadeColor;  set => Set(ref _grenadeColor,  value); }

        // ---- Overlay ----
        private bool _showFps = true;
        private bool _watermark = true;
        private bool _crosshairOverlay;

        public bool ShowFps { get => _showFps; set => Set(ref _showFps, value); }
        public bool Watermark { get => _watermark; set => Set(ref _watermark, value); }
        public bool CrosshairOverlay { get => _crosshairOverlay; set => Set(ref _crosshairOverlay, value); }

        // ---- Crosshair customization ----
        private string _crosshairColor     = "#A35BFF";
        private double _crosshairSize      = 6;
        private double _crosshairThickness = 1.5;
        private double _crosshairGap       = 3;
        private bool   _crosshairDot;

        public string CrosshairColor     { get => _crosshairColor;     set => Set(ref _crosshairColor,     value); }
        public double CrosshairSize      { get => _crosshairSize;      set => Set(ref _crosshairSize,      value); }
        public double CrosshairThickness { get => _crosshairThickness; set => Set(ref _crosshairThickness, value); }
        public double CrosshairGap       { get => _crosshairGap;       set => Set(ref _crosshairGap,       value); }
        public bool   CrosshairDot       { get => _crosshairDot;       set => Set(ref _crosshairDot,       value); }

        // ---- Skin changer ----
        private bool _skinChangerEnabled;
        private Dictionary<int, SkinPreset> _skinMap = new();

        public bool SkinChangerEnabled { get => _skinChangerEnabled; set => Set(ref _skinChangerEnabled, value); }
        public Dictionary<int, SkinPreset> SkinMap { get => _skinMap; set { _skinMap = value ?? new(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SkinMap))); } }

        private GlovePreset? _glovePreset;
        public GlovePreset? GlovePreset { get => _glovePreset; set => Set(ref _glovePreset, value); }

        // ---- Settings ----
        private bool _saveOnExit = true;
        private bool _startMinimized;

        public bool SaveOnExit { get => _saveOnExit; set => Set(ref _saveOnExit, value); }
        public bool StartMinimized { get => _startMinimized; set => Set(ref _startMinimized, value); }

        // ================= Persistence =================

        private static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stratus");
        private static readonly string FilePath = Path.Combine(Dir, "config.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        /// <summary>The single shared config instance the whole app binds to.</summary>
        public static Config Current { get; private set; } = new();

        /// <summary>Load config from disk, or keep defaults if none / unreadable.</summary>
        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<Config>(json, JsonOpts);
                    if (loaded != null)
                        Current = loaded;
                }
            }
            catch
            {
                // Corrupt or partial file — fall back to defaults rather than crash.
                Current = new Config();
            }
        }

        /// <summary>Write current config to disk.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
            }
            catch
            {
                // Non-fatal: a failed save shouldn't take the app down.
            }
        }

        // ================= Named profiles =================
        // Full config snapshots (ESP, skins, radar, everything) saved separately from the
        // single auto-loaded config.json, so the user can flip between setups by name.

        private static readonly string ProfilesDir = Path.Combine(Dir, "Profiles");

        private static string ProfilePath(string name) => Path.Combine(ProfilesDir, SanitizeFileName(name) + ".json");

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>Names of all saved profiles, alphabetical.</summary>
        public static List<string> ListProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfilesDir)) return new List<string>();
                return Directory.GetFiles(ProfilesDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList()!;
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Save the current live config as a named profile.</summary>
        public void SaveAsProfile(string name)
        {
            try
            {
                Directory.CreateDirectory(ProfilesDir);
                File.WriteAllText(ProfilePath(name), JsonSerializer.Serialize(this, JsonOpts));
            }
            catch
            {
                // Non-fatal.
            }
        }

        /// <summary>
        /// Load a named profile's values into the SAME <see cref="Current"/> instance (rather
        /// than swapping the reference) so every window's existing DataContext binding picks up
        /// the change automatically via the normal property-changed notifications.
        /// </summary>
        public static bool LoadProfile(string name)
        {
            try
            {
                string path = ProfilePath(name);
                if (!File.Exists(path)) return false;

                var json   = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Config>(json, JsonOpts);
                if (loaded == null) return false;

                foreach (var prop in typeof(Config).GetProperties())
                {
                    if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0) continue;
                    prop.SetValue(Current, prop.GetValue(loaded));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void DeleteProfile(string name)
        {
            try
            {
                string path = ProfilePath(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Non-fatal.
            }
        }

        /// <summary>Copy a saved profile's JSON file to an arbitrary path, for backup/sharing.</summary>
        public static bool ExportProfile(string name, string destPath)
        {
            try
            {
                string src = ProfilePath(name);
                if (!File.Exists(src)) return false;
                File.Copy(src, destPath, overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Copy an external profile JSON file into the Profiles folder under the given name,
        /// validating it actually deserializes as a Config first so a garbage file doesn't end
        /// up silently sitting in the list only to fail later when loaded.
        /// </summary>
        public static bool ImportProfile(string sourcePath, string name)
        {
            try
            {
                if (!File.Exists(sourcePath)) return false;

                var json   = File.ReadAllText(sourcePath);
                var parsed = JsonSerializer.Deserialize<Config>(json, JsonOpts);
                if (parsed == null) return false;

                Directory.CreateDirectory(ProfilesDir);
                File.Copy(sourcePath, ProfilePath(name), overwrite: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================= INotifyPropertyChanged =================

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
