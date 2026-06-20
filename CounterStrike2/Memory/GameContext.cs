using System;
using System.Threading.Tasks;

namespace CounterStrike2.Memory
{
    /// <summary>
    /// Owns the single ProcessMemory handle for the lifetime of the app.
    /// Call AttachAsync() / Detach() from the UI — both are safe to call from any thread.
    /// </summary>
    public static class GameContext
    {
        public static readonly ProcessMemory Memory = new();

        public enum State { Detached, Connecting, Scanning, Ready, Failed }

        public static State  Current   { get; private set; } = State.Detached;
        public static string LastError { get; private set; } = string.Empty;

        public static bool IsAttached =>
            Current is State.Scanning or State.Ready;

        /// <summary>
        /// Attach to cs2.exe and scan all offsets on a background thread.
        /// Returns the final state so the caller can update the UI.
        /// </summary>
        public static async Task<State> AttachAsync()
        {
            Current = State.Connecting;

            bool attached = await Task.Run(() => Memory.Attach("cs2"));
            if (!attached)
            {
                LastError = Memory.LastError;
                Current   = State.Failed;
                return Current;
            }

            Current = State.Scanning;
            await Task.Run(() => Offsets.Refresh(Memory));

            Current = Offsets.IsReady ? State.Ready : State.Failed;
            return Current;
        }

        public static void Detach()
        {
            Memory.Detach();
            Offsets.Reset();
            Current = State.Detached;
        }
    }
}
