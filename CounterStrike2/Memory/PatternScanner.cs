using System;
using System.Collections.Generic;

namespace CounterStrike2.Memory
{
    /// <summary>
    /// Scans a local copy of a module's bytes for IDA-style byte patterns
    /// ("48 8B 05 ?? ?? ?? ??") and resolves RIP-relative instructions
    /// without touching game memory after the initial module read.
    ///
    /// How RIP-relative resolution works:
    ///   "mov rax, [rip + X]"  →  48 8B 05 [XX XX XX XX]
    ///                                       ^^^^^^^^^^^^ 4-byte signed displacement (little-endian)
    ///   Target address = (address of next instruction) + displacement
    ///                  = matchAddr + instructionLength + displacement
    ///   That target holds the global variable (entity list ptr, view matrix, etc.)
    /// </summary>
    public sealed class PatternScanner
    {
        private readonly byte[] _buffer;
        private readonly IntPtr _base;

        public IntPtr Base => _base;
        public bool IsValid => _base != IntPtr.Zero && _buffer.Length > 0;

        public PatternScanner(byte[] buffer, IntPtr moduleBase)
        {
            _buffer = buffer;
            _base = moduleBase;
        }

        /// <summary>
        /// Read a full module into a local buffer and return a scanner for it.
        /// Returns an invalid scanner (IsValid == false) if the module isn't loaded.
        /// </summary>
        public static PatternScanner FromModule(ProcessMemory mem, string moduleName)
        {
            var (moduleBase, moduleSize) = mem.GetModule(moduleName);
            if (moduleBase == IntPtr.Zero || moduleSize == 0)
                return new PatternScanner(Array.Empty<byte>(), IntPtr.Zero);

            byte[] buffer = mem.ReadBytes(moduleBase, moduleSize);
            return new PatternScanner(buffer, moduleBase);
        }

        // ---- Search ----

        /// <summary>
        /// Find the first match for a pattern.
        /// Returns the absolute address (in game process) of the first matched byte, or Zero.
        /// </summary>
        public IntPtr Find(string pattern)
        {
            Parse(pattern, out byte[] bytes, out bool[] wild);
            int limit = _buffer.Length - bytes.Length;

            for (int i = 0; i <= limit; i++)
            {
                if (Matches(i, bytes, wild))
                    return IntPtr.Add(_base, i);
            }
            return IntPtr.Zero;
        }

        /// <summary>Find all matches. Useful for validating a pattern is unique.</summary>
        public List<IntPtr> FindAll(string pattern)
        {
            Parse(pattern, out byte[] bytes, out bool[] wild);
            var results = new List<IntPtr>();
            int limit = _buffer.Length - bytes.Length;

            for (int i = 0; i <= limit; i++)
            {
                if (Matches(i, bytes, wild))
                    results.Add(IntPtr.Add(_base, i));
            }
            return results;
        }

        // ---- RIP resolution (uses local buffer — no extra game memory read) ----

        /// <summary>
        /// Resolve the RIP-relative displacement encoded in an instruction found at
        /// <paramref name="matchAddr"/>.
        ///
        ///   result = matchAddr + instrLen + Read&lt;int&gt;(matchAddr + dispOffset)
        ///
        /// Default values cover the most common case:
        ///   48 8B 05 [XX XX XX XX]  →  dispOffset=3, instrLen=7
        ///   48 8D 0D [XX XX XX XX]  →  same
        /// </summary>
        public IntPtr ResolveRip(IntPtr matchAddr, int dispOffset = 3, int instrLen = 7)
        {
            if (matchAddr == IntPtr.Zero) return IntPtr.Zero;

            long localOffset = matchAddr.ToInt64() - _base.ToInt64();
            if (localOffset < 0 || localOffset + dispOffset + 4 > _buffer.Length)
                return IntPtr.Zero;

            int disp = BitConverter.ToInt32(_buffer, (int)(localOffset + dispOffset));
            return IntPtr.Add(matchAddr, instrLen + disp);
        }

        // ---- Helpers ----

        private bool Matches(int bufferOffset, byte[] bytes, bool[] wild)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!wild[i] && _buffer[bufferOffset + i] != bytes[i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Parse "48 8B 05 ?? ?? ?? ??" into parallel byte + wildcard-mask arrays.
        /// Single "?" is treated the same as "??".
        /// </summary>
        private static void Parse(string pattern, out byte[] bytes, out bool[] wild)
        {
            var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bytes = new byte[tokens.Length];
            wild  = new bool[tokens.Length];

            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] is "??" or "?")
                    wild[i] = true;
                else
                    bytes[i] = Convert.ToByte(tokens[i], 16);
            }
        }
    }
}
