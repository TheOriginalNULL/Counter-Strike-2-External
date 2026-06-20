using System.Collections.ObjectModel;
using System.Linq;
using CounterStrike2.Memory;

namespace CounterStrike2.Debug
{
    /// <summary>
    /// Static store for the debug address table.
    /// Call Refresh() after Offsets.Refresh() to push new values into the UI.
    /// Bind ItemsControl.ItemsSource to DebugView.Entries in XAML via x:Static.
    /// </summary>
    public static class DebugView
    {
        public static ObservableCollection<DebugEntry> Entries { get; } = new(new[]
        {
            // client.dll
            new DebugEntry("EntityList",             "client.dll"),
            new DebugEntry("LocalPlayerController",  "client.dll"),
            new DebugEntry("LocalPlayerPawn",        "client.dll"),
            new DebugEntry("ViewMatrix",             "client.dll"),
            new DebugEntry("GlobalVars",             "client.dll"),
            new DebugEntry("GameRules",              "client.dll"),
            new DebugEntry("PlantedC4",              "client.dll"),
            new DebugEntry("CSGOInput",              "client.dll"),

            // engine2.dll
            new DebugEntry("NetworkGameClient",      "engine2.dll"),
            new DebugEntry("BuildNumber",            "engine2.dll"),

            // inputsystem.dll
            new DebugEntry("InputSystem",            "inputsystem.dll"),
        });

        /// <summary>Push the latest resolved addresses into every row.</summary>
        public static void Refresh()
        {
            Set("EntityList",            Offsets.EntityList);
            Set("LocalPlayerController", Offsets.LocalPlayerController);
            Set("LocalPlayerPawn",       Offsets.LocalPlayerPawn);
            Set("ViewMatrix",            Offsets.ViewMatrix);
            Set("GlobalVars",            Offsets.GlobalVars);
            Set("GameRules",             Offsets.GameRules);
            Set("PlantedC4",             Offsets.PlantedC4);
            Set("CSGOInput",             Offsets.CSGOInput);
            Set("NetworkGameClient",     Offsets.NetworkGameClient);
            Set("BuildNumber",           Offsets.BuildNumber);
            Set("InputSystem",           Offsets.InputSystem);
        }

        /// <summary>Zero every row — call after detach.</summary>
        public static void Reset()
        {
            foreach (var e in Entries)
                e.Address = System.IntPtr.Zero;
        }

        private static void Set(string name, System.IntPtr addr)
        {
            var entry = Entries.FirstOrDefault(e => e.Name == name);
            if (entry != null) entry.Address = addr;
        }
    }
}
