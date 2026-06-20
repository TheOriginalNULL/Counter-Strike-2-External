using System;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Tracks damage WE dealt this round, for the hit indicator/hit sound. Mirrors
    /// KillTracker's delta-detection pattern: CCSPlayerController_ActionTrackingServices
    /// ::m_flTotalRoundDamageDealt increases as we land hits and resets to 0 each round —
    /// a drop is treated as a round reset (0 delta), not negative damage.
    /// </summary>
    internal static class DamageTracker
    {
        private static float _last;
        private static bool  _ready;

        /// <summary>Returns the damage dealt since the last call (0 if none, or on a round reset).</summary>
        internal static float Update(ProcessMemory mem)
        {
            if (Offsets.LocalPlayerController == IntPtr.Zero) return 0f;
            IntPtr ctrl = mem.Read<IntPtr>(Offsets.LocalPlayerController);
            if (ctrl == IntPtr.Zero) return 0f;

            IntPtr svc = mem.Read<IntPtr>(IntPtr.Add(ctrl, EntitySchema.ActionTrackingServices));
            if (svc == IntPtr.Zero) return 0f;

            float cur = mem.Read<float>(IntPtr.Add(svc, EntitySchema.TotalRoundDamageDealt));

            if (!_ready) { _last = cur; _ready = true; return 0f; }

            float delta = cur > _last ? cur - _last : 0f;
            _last = cur;
            return delta;
        }

        internal static void Reset() => _ready = false;
    }
}
