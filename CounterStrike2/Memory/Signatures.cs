namespace CounterStrike2.Memory
{
    /// <summary>
    /// IDA-style byte patterns converted from a2x/cs2-dumper's pelite format.
    /// Source: https://github.com/a2x/cs2-dumper/blob/main/src/analysis/offsets.rs
    ///
    /// Pelite → IDA conversion rule:
    ///   ${''} = 4 wildcard bytes (the RIP displacement) → ?? ?? ?? ??
    ///   ?     = 1 wildcard byte
    ///   u4    = 4-byte inline value (not a displacement) → ?? ?? ?? ??
    ///   [N]   = skip N bytes → N × ??
    ///
    /// When a pattern needs non-default RIP resolution (dispOffset ≠ 3 or instrLen ≠ 7),
    /// see the comments in Offsets.Refresh where it is called.
    ///
    /// Last synced with a2x: June 2026. Re-sync after every CS2 update.
    /// </summary>
    public static class Signatures
    {
        // =====================================================================
        //  client.dll
        // =====================================================================
        public static class Client
        {
            // mov [rip+?], rcx  →  48 89 0D [disp]
            // a2x: "48890d${''} e9${} cc"
            public const string EntityList =
                "48 89 0D ?? ?? ?? ?? E9 ?? ?? ?? ?? CC";

            // mov rax, [rip+?]  →  48 8B 05 [disp]
            // a2x: "488b05${''} 4189be"
            public const string LocalPlayerController =
                "48 8B 05 ?? ?? ?? ?? 41 89 BE";

            // lea rcx, [rip+?]  →  48 8D 0D [disp]   (LEA — no deref, result IS the matrix)
            // a2x: "488d0d${''} 48c1e006"
            public const string ViewMatrix =
                "48 8D 0D ?? ?? ?? ?? 48 C1 E0 06";

            // mov [rip+?], rdx  →  48 89 15 [disp]
            // a2x: "488915${''} 488942"
            public const string GlobalVars =
                "48 89 15 ?? ?? ?? ?? 48 89 42";

            // 4C 8B 05 [disp] is at byte 9 of the matched pattern
            // dispOffset=12, instrLen=16 — see Offsets.Refresh
            // a2x: "f6c1010f85${} 4c8b05${''} 4d85"
            public const string GameRules =
                "F6 C1 01 0F 85 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ?? 4D 85";

            // mov rdx, [rip+?]  →  48 8B 15 [disp]
            // a2x: "488b15${''} 41ffc0 488d4c24?"
            public const string PlantedC4 =
                "48 8B 15 ?? ?? ?? ?? 41 FF C0 48 8D 4C 24 ??";

            // mov [rip+?], rax  →  48 89 05 [disp]
            // a2x: "488905${''} 0f57c0 0f1105"
            public const string CSGOInput =
                "48 89 05 ?? ?? ?? ?? 0F 57 C0 0F 11 05";

            // LocalPlayerPawn uses a callback pattern in a2x with no direct RIP displacement.
            // Resolved via module RVA fallback in Offsets.Refresh instead.
            // a2x offset (June 2026): 0x234C698 — update this RVA after CS2 patches.
        }

        // =====================================================================
        //  engine2.dll
        // =====================================================================
        public static class Engine2
        {
            // mov [rip+?], rdi  →  48 89 3D [disp]
            // a2x: "48893d${''} ff87"
            public const string NetworkGameClient =
                "48 89 3D ?? ?? ?? ?? FF 87";

            // mov [rip+?], eax  →  89 05 [disp]  (32-bit, no REX prefix)
            // dispOffset=2, instrLen=6 — see Offsets.Refresh
            // a2x: "8905${''} 488d0d"
            public const string BuildNumber =
                "89 05 ?? ?? ?? ?? 48 8D 0D";
        }

        // =====================================================================
        //  inputsystem.dll
        // =====================================================================
        public static class InputSystem
        {
            // mov [rip+?], rax  →  48 89 05 [disp]
            // a2x: "488905${''} 33c0"
            public const string InputSystemPtr =
                "48 89 05 ?? ?? ?? ?? 33 C0";
        }
    }
}
