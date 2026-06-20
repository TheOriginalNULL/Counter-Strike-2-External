using System;

namespace CounterStrike2.Memory
{
    /// <summary>
    /// Resolved absolute addresses in the game process.
    /// Call Refresh() once after attaching — it scans every pattern in Signatures.cs
    /// and caches the results here. The rest of the codebase reads these properties.
    ///
    /// If a property is IntPtr.Zero, the pattern didn't match — either the game updated
    /// (fix the pattern in Signatures.cs) or the process isn't fully loaded yet.
    /// </summary>
    public static class Offsets
    {
        // ---- client.dll ----

        /// <summary>Address of the global CGameEntitySystem* variable.</summary>
        public static IntPtr EntityList           { get; private set; }

        /// <summary>Address of the global C_CSPlayerController* variable.</summary>
        public static IntPtr LocalPlayerController { get; private set; }

        /// <summary>Address of the global C_CSPlayerPawn* variable.</summary>
        public static IntPtr LocalPlayerPawn      { get; private set; }

        /// <summary>
        /// Address of the 4x4 view-matrix float array (NOT a pointer — the matrix lives here).
        /// Read 64 bytes here: 16 floats laid out row-major.
        /// </summary>
        public static IntPtr ViewMatrix           { get; private set; }

        /// <summary>Address of the global CGlobalVars* variable.</summary>
        public static IntPtr GlobalVars           { get; private set; }

        /// <summary>Address of the global CCSGameRules* variable.</summary>
        public static IntPtr GameRules            { get; private set; }

        /// <summary>Address of the global CPlantedC4* variable (null when no bomb).</summary>
        public static IntPtr PlantedC4            { get; private set; }

        /// <summary>Address of the global CCSGOInput* variable.</summary>
        public static IntPtr CSGOInput            { get; private set; }

        // ---- engine2.dll ----

        /// <summary>Address of the global INetworkGameClient* variable.</summary>
        public static IntPtr NetworkGameClient    { get; private set; }

        /// <summary>Address of the 4-byte build number value.</summary>
        public static IntPtr BuildNumber          { get; private set; }

        // ---- inputsystem.dll ----

        /// <summary>Address of the global IInputSystem* variable.</summary>
        public static IntPtr InputSystem          { get; private set; }

        // ---- Status ----

        /// <summary>
        /// True when the three addresses that must be non-zero for ESP to work are resolved.
        /// Use this to gate your render loop.
        /// </summary>
        /// <summary>
        /// True when the addresses required for ESP are resolved.
        /// LocalPlayerPawn is now derived at read-time; we only require the entity list,
        /// the pattern-scanned controller global, and the view matrix.
        /// </summary>
        public static bool IsReady =>
            EntityList            != IntPtr.Zero &&
            LocalPlayerController != IntPtr.Zero &&
            ViewMatrix            != IntPtr.Zero;

        // ---- Reset ----

        /// <summary>Zero every offset — call this on detach so stale addresses aren't used.</summary>
        public static void Reset()
        {
            EntityList            = IntPtr.Zero;
            LocalPlayerController = IntPtr.Zero;
            LocalPlayerPawn       = IntPtr.Zero;
            ViewMatrix            = IntPtr.Zero;
            GlobalVars            = IntPtr.Zero;
            GameRules             = IntPtr.Zero;
            PlantedC4             = IntPtr.Zero;
            CSGOInput             = IntPtr.Zero;
            NetworkGameClient     = IntPtr.Zero;
            BuildNumber           = IntPtr.Zero;
            InputSystem           = IntPtr.Zero;
        }

        // ---- Refresh ----

        /// <summary>
        /// Populate every offset by adding the dumped RVA to each module's base address.
        /// RVAs come from cs2-dumper (a2x), dumped 2026-06-17.
        /// No pattern scanning — instant, and survives obfuscation.
        /// When CS2 updates: re-run the dumper, copy the new RVAs here.
        /// </summary>
        public static void Refresh(ProcessMemory mem)
        {
            IntPtr client  = mem.GetModuleBase("client.dll");
            IntPtr engine2 = mem.GetModuleBase("engine2.dll");
            IntPtr input   = mem.GetModuleBase("inputsystem.dll");

            // ---- client.dll — from Dumper/output/offsets.cs ----
            if (client != IntPtr.Zero)
            {
                EntityList            = client + 0x24E76A0;  // dwEntityList / dwGameEntitySystem
                LocalPlayerController = client + 0x2320720;  // dwLocalPlayerController
                LocalPlayerPawn       = client + 0x2341698;  // dwLocalPlayerPawn
                ViewMatrix            = client + 0x2346B30;  // dwViewMatrix  (matrix lives here, no deref)
                GlobalVars            = client + 0x20616D0;  // dwGlobalVars
                GameRules             = client + 0x2341158;  // dwGameRules
                PlantedC4             = client + 0x234FF98;  // dwPlantedC4
                CSGOInput             = client + 0x2356240;  // dwCSGOInput
            }

            // ---- engine2.dll ----
            if (engine2 != IntPtr.Zero)
            {
                NetworkGameClient = engine2 + 0x90A1A0;  // dwNetworkGameClient
                BuildNumber       = engine2 + 0x60CC74;  // dwBuildNumber
            }

            // ---- inputsystem.dll ----
            if (input != IntPtr.Zero)
                InputSystem = input + 0x42B50;  // dwInputSystem
        }
    }
}
