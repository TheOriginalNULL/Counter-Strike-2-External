using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CounterStrike2.Memory
{
    /// <summary>
    /// External memory reader. Opens a target process by name and exposes typed
    /// reads through ReadProcessMemory. Read-only — it never writes to the target.
    ///
    /// This is the foundation every higher layer (schema dumper, entity walk,
    /// world-to-screen) reads through. Intended for offline -insecure bot matches
    /// and reverse-engineering study.
    /// </summary>
    public sealed class ProcessMemory : IDisposable
    {
        // ---- Win32 ----
        private const uint PROCESS_VM_READ        = 0x0010;
        private const uint PROCESS_VM_WRITE       = 0x0020;
        private const uint PROCESS_VM_OPERATION   = 0x0008;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool ReadProcessMemory(
            IntPtr handle, IntPtr address, void* buffer, int size, out int bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool WriteProcessMemory(
            IntPtr handle, IntPtr address, void* buffer, int size, out int bytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr hProcess, IntPtr lpAddress, int dwSize,
            uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFreeEx(
            IntPtr hProcess, IntPtr lpAddress, int dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr hProcess, IntPtr lpThreadAttributes, int dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter,
            uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        private const uint MEM_COMMIT             = 0x1000;
        private const uint MEM_RESERVE            = 0x2000;
        private const uint MEM_RELEASE            = 0x8000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        // ---- State ----
        private IntPtr _handle = IntPtr.Zero;

        public Process? Process  { get; private set; }
        public string LastError  { get; private set; } = string.Empty;

        public bool IsAttached =>
            _handle != IntPtr.Zero && Process is { HasExited: false };

        /// <summary>Open the named process (no ".exe") for reading. Returns success.</summary>
        public bool Attach(string processName)
        {
            if (IsAttached && Process!.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                return true;

            Detach();
            LastError = string.Empty;

            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0)
            {
                LastError = $"'{processName}.exe' is not running";
                return false;
            }

            var proc = procs[0];
            IntPtr handle = OpenProcess(PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION | PROCESS_QUERY_INFORMATION, false, proc.Id);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LastError = err == 5
                    ? "Access denied — run the app as Administrator"
                    : $"OpenProcess failed (Win32 error {err})";
                proc.Dispose();
                return false;
            }

            _handle = handle;
            Process  = proc;
            return true;
        }

        public void Detach()
        {
            if (_handle != IntPtr.Zero)
            {
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
            Process?.Dispose();
            Process = null;
        }

        /// <summary>Base address of a loaded module (e.g. "client.dll"), or Zero if not found.</summary>
        public IntPtr GetModuleBase(string moduleName)
            => GetModule(moduleName).Base;

        /// <summary>Base address and image size of a loaded module, or (Zero, 0).</summary>
        public (IntPtr Base, int Size) GetModule(string moduleName)
        {
            if (Process == null) return (IntPtr.Zero, 0);
            try
            {
                foreach (ProcessModule m in Process.Modules)
                {
                    if (string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                        return (m.BaseAddress, m.ModuleMemorySize);
                }
            }
            catch
            {
                // Modules can momentarily be unreadable while the process loads.
            }
            return (IntPtr.Zero, 0);
        }

        /// <summary>
        /// Resolve a 32-bit RIP-relative operand into an absolute address.
        /// disp is read at (address + displacementOffset); target = next instruction + disp.
        /// e.g. for "48 8B 05 ?? ?? ?? ??" (mov rax,[rip+x]) use displacementOffset=3, instructionLength=7.
        /// </summary>
        public IntPtr ResolveRelative(IntPtr address, int displacementOffset, int instructionLength)
        {
            int disp = Read<int>(IntPtr.Add(address, displacementOffset));
            return IntPtr.Add(address, instructionLength + disp);
        }

        /// <summary>
        /// Look up an exported function by name by parsing the module's PE export table
        /// directly out of target memory (works externally — no GetProcAddress).
        /// </summary>
        public IntPtr GetExport(IntPtr moduleBase, string exportName)
        {
            if (moduleBase == IntPtr.Zero) return IntPtr.Zero;

            // DOS header -> NT headers
            int ntOffset = Read<int>(IntPtr.Add(moduleBase, 0x3C));   // e_lfanew
            IntPtr nt = IntPtr.Add(moduleBase, ntOffset);

            // PE32+ optional header begins at nt+0x18; DataDirectory[0] (export) at +0x70.
            int exportRva = Read<int>(IntPtr.Add(nt, 0x18 + 0x70));
            if (exportRva == 0) return IntPtr.Zero;
            IntPtr exportDir = IntPtr.Add(moduleBase, exportRva);

            int numberOfNames   = Read<int>(IntPtr.Add(exportDir, 0x18));
            int functionsRva    = Read<int>(IntPtr.Add(exportDir, 0x1C));
            int namesRva        = Read<int>(IntPtr.Add(exportDir, 0x20));
            int ordinalsRva     = Read<int>(IntPtr.Add(exportDir, 0x24));

            for (int i = 0; i < numberOfNames; i++)
            {
                int nameRva = Read<int>(IntPtr.Add(moduleBase, namesRva + i * 4));
                string name = ReadString(IntPtr.Add(moduleBase, nameRva), 96);
                if (name == exportName)
                {
                    ushort ordinal = Read<ushort>(IntPtr.Add(moduleBase, ordinalsRva + i * 2));
                    int funcRva = Read<int>(IntPtr.Add(moduleBase, functionsRva + ordinal * 4));
                    return IntPtr.Add(moduleBase, funcRva);
                }
            }
            return IntPtr.Zero;
        }

        // ================= Reads =================

        /// <summary>
        /// Read an unmanaged value directly into a stack-allocated slot — zero heap allocation.
        /// </summary>
        public unsafe T Read<T>(IntPtr address) where T : unmanaged
        {
            T result = default;
            if (IsAttached)
                ReadProcessMemory(_handle, address, &result, sizeof(T), out _);
            return result;
        }

        /// <summary>Fill a pre-allocated byte[] from target memory. Returns true on a complete read.</summary>
        public unsafe bool ReadBytes(IntPtr address, byte[] buffer)
        {
            if (!IsAttached || buffer.Length == 0) return false;
            fixed (byte* ptr = buffer)
                return ReadProcessMemory(_handle, address, ptr, buffer.Length, out int read)
                       && read == buffer.Length;
        }

        /// <summary>Allocate and fill a byte[] of the given length (used for large module scans).</summary>
        public byte[] ReadBytes(IntPtr address, int count)
        {
            var buf = new byte[count];
            ReadBytes(address, buf);
            return buf;
        }

        /// <summary>Fill a Span from target memory — works with stackalloc and static arrays.</summary>
        public unsafe bool ReadBytes(IntPtr address, Span<byte> buffer)
        {
            if (!IsAttached || buffer.IsEmpty) return false;
            fixed (byte* ptr = buffer)
                return ReadProcessMemory(_handle, address, ptr, buffer.Length, out int read)
                       && read == buffer.Length;
        }

        /// <summary>Read a null-terminated UTF-8 string (stack-allocated, no heap).</summary>
        public unsafe string ReadString(IntPtr address, int maxLength = 64)
        {
            byte* buf = stackalloc byte[maxLength];
            int read = 0;
            if (!IsAttached || !ReadProcessMemory(_handle, address, buf, maxLength, out read))
                return string.Empty;
            int len = 0;
            while (len < read && buf[len] != 0) len++;
            return len == 0 ? string.Empty : Encoding.UTF8.GetString(buf, len);
        }

        /// <summary>
        /// Resolve a multi-level pointer chain. Each offset except the last dereferences;
        /// the final offset is added to the last pointer. Returns the resolved address.
        /// </summary>
        public IntPtr ReadChain(IntPtr baseAddress, params int[] offsets)
        {
            IntPtr addr = baseAddress;
            for (int i = 0; i < offsets.Length - 1; i++)
            {
                addr = Read<IntPtr>(IntPtr.Add(addr, offsets[i]));
                if (addr == IntPtr.Zero)
                    return IntPtr.Zero;
            }
            return IntPtr.Add(addr, offsets[^1]);
        }

        // ================= Writes =================

        /// <summary>Write an unmanaged value to the target process. No-op when not attached.</summary>
        public unsafe void Write<T>(IntPtr address, T value) where T : unmanaged
        {
            if (!IsAttached) return;
            WriteProcessMemory(_handle, address, &value, sizeof(T), out _);
        }

        // ================= Remote memory / thread =================

        /// <summary>Allocate a committed RWX block inside the target process.</summary>
        public IntPtr Allocate(int size = 0x1000)
        {
            if (!IsAttached) return IntPtr.Zero;
            return VirtualAllocEx(_handle, IntPtr.Zero, size,
                                  MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
        }

        /// <summary>Release a block previously allocated with Allocate().</summary>
        public void Free(IntPtr address)
        {
            if (!IsAttached || address == IntPtr.Zero) return;
            VirtualFreeEx(_handle, address, 0, MEM_RELEASE);
        }

        /// <summary>
        /// Spin a remote thread at funcAddress (no parameter), wait for it to finish,
        /// then close its handle. Times out after 5 seconds to avoid a permanent hang.
        /// </summary>
        public void CallThread(IntPtr funcAddress)
        {
            if (!IsAttached || funcAddress == IntPtr.Zero) return;
            IntPtr thread = CreateRemoteThread(_handle, IntPtr.Zero, 0,
                                               funcAddress, IntPtr.Zero, 0, out _);
            if (thread == IntPtr.Zero) return;
            WaitForSingleObject(thread, 5000);
            CloseHandle(thread);
        }

        /// <summary>Write a raw byte array to target memory.</summary>
        public unsafe void WriteBytes(IntPtr address, byte[] data)
        {
            if (!IsAttached || data == null || data.Length == 0) return;
            fixed (byte* p = data)
                WriteProcessMemory(_handle, address, p, data.Length, out _);
        }

        /// <summary>
        /// Scan a loaded module for a byte-pattern signature and return the address of
        /// the first match inside target-process memory. '?' or '??' are wildcards.
        /// Returns IntPtr.Zero if the module is not found or the pattern does not match.
        /// </summary>
        public IntPtr SigScan(string moduleName, string pattern)
        {
            var (modBase, modSize) = GetModule(moduleName);
            if (modBase == IntPtr.Zero || modSize == 0) return IntPtr.Zero;

            // Parse "48 83 EC ? E8 ..." into (byte value, bool isWildcard) pairs.
            var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sig   = new (byte val, bool wild)[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                bool w = parts[i] == "?" || parts[i] == "??";
                sig[i] = (w ? (byte)0 : Convert.ToByte(parts[i], 16), w);
            }

            int sigLen = sig.Length;
            const int ChunkSize = 0x10000;
            // Over-allocate so a pattern spanning a chunk boundary is never split.
            var buf = new byte[ChunkSize + sigLen];

            for (int offset = 0; offset < modSize; offset += ChunkSize)
            {
                int toRead = Math.Min(ChunkSize + sigLen, modSize - offset);
                if (toRead < sigLen) break;

                if (!ReadBytes(IntPtr.Add(modBase, offset), new Span<byte>(buf, 0, toRead)))
                    continue;

                int limit = toRead - sigLen;
                for (int i = 0; i <= limit; i++)
                {
                    bool found = true;
                    for (int j = 0; j < sigLen; j++)
                    {
                        if (!sig[j].wild && buf[i + j] != sig[j].val) { found = false; break; }
                    }
                    if (found) return IntPtr.Add(modBase, offset + i);
                }
            }

            return IntPtr.Zero;
        }

        public void Dispose() => Detach();
    }
}
