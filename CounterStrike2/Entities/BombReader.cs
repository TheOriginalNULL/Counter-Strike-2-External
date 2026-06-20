using System;
using System.Diagnostics;
using System.Numerics;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    internal static class BombReader
    {
        // Wall-clock fallback — used when GlobalVars curTime is unavailable or implausible.
        private static readonly Stopwatch _sw = Stopwatch.StartNew();

        private static double _bombWallRef   = -1.0;  // wall time when ticking first detected
        private static float  _bombTimerRef  = 0f;    // timerLen at that moment

        private static double _defuseWallRef  = -1.0; // wall time when defuse started
        private static float  _defuseLenRef   = 0f;   // defuseLen at that moment

        // Used to detect a new bomb being planted (blowTime is constant during countdown).
        private static float  _lastBlowTime  = -1f;

        internal static BombEntity? Read(ProcessMemory mem)
        {
            if (Offsets.PlantedC4 == IntPtr.Zero) return null;

            // dwPlantedC4 = CUtlVector<C_PlantedC4*> embedded in .data.
            // First 8 bytes = m_pMemory (pointer to element array); element[0] = C_PlantedC4*.
            IntPtr listBase = mem.Read<IntPtr>(Offsets.PlantedC4);
            if (listBase == IntPtr.Zero) { Reset(); return null; }

            IntPtr bomb = mem.Read<IntPtr>(listBase);
            if (bomb == IntPtr.Zero) { Reset(); return null; }

            IntPtr sceneNode = mem.Read<IntPtr>(IntPtr.Add(bomb, EntitySchema.GameSceneNode));
            if (sceneNode == IntPtr.Zero) { Reset(); return null; }

            Vector3 origin = mem.Read<Vector3>(IntPtr.Add(sceneNode, EntitySchema.AbsOrigin));
            if (Math.Abs(origin.X) > 20000f || Math.Abs(origin.Y) > 20000f) { Reset(); return null; }

            bool  ticking      = mem.Read<byte> (IntPtr.Add(bomb, EntitySchema.BombTicking))      != 0;
            bool  defused      = mem.Read<byte> (IntPtr.Add(bomb, EntitySchema.BombDefused))      != 0;
            bool  beingDefused = mem.Read<byte> (IntPtr.Add(bomb, EntitySchema.BombBeingDefused)) != 0;
            int   site         = mem.Read<int>  (IntPtr.Add(bomb, EntitySchema.BombSite));
            float blowTime     = mem.Read<float>(IntPtr.Add(bomb, EntitySchema.BombBlowTime));
            float timerLen     = mem.Read<float>(IntPtr.Add(bomb, EntitySchema.BombTimerLength));
            float defuseEnd    = mem.Read<float>(IntPtr.Add(bomb, EntitySchema.BombDefuseEnd));
            float defuseLen    = mem.Read<float>(IntPtr.Add(bomb, EntitySchema.BombDefuseLength));

            // Sanity-check: real C4 timer is 5–90 s. Math.Clamp throws if timerLen < 0.
            if (timerLen < 1f || timerLen > 120f) { Reset(); return null; }

            // Reject contradictory or stale states.
            if (defused && ticking)   { Reset(); return null; } // impossible in real gameplay
            if (!ticking && !defused) { Reset(); return null; } // inactive — nothing to show
            // blowTime must be greater than timerLen (bomb planted at positive game time).
            if (blowTime <= timerLen || blowTime > timerLen + 7200f) { Reset(); return null; }

            // Detect a new bomb being planted: blowTime is a fixed absolute timestamp that
            // never changes during a countdown but jumps when a new bomb is planted.
            // This resets the wall-clock reference so each round starts fresh.
            if (Math.Abs(blowTime - _lastBlowTime) > 0.5f)
            {
                Reset();
                _lastBlowTime = blowTime;
            }

            float curTime = ReadCurTime(mem);

            // ── Bomb countdown ──────────────────────────────────────────────
            float remaining;
            if (!ticking || defused)
            {
                remaining = 0f;
                _bombWallRef = -1.0;
            }
            else
            {
                // Use GlobalVars curTime when it's plausible (game time > 10 s, hasn't
                // overshot the blow time).
                float gvLeft = blowTime - curTime;
                if (curTime > 10f && gvLeft >= 0f && gvLeft <= timerLen)
                {
                    remaining    = gvLeft;
                    _bombWallRef = -1.0;   // don't accumulate wall-clock drift
                }
                else
                {
                    // Fall back to wall-clock countdown from first detection.
                    double now = _sw.Elapsed.TotalSeconds;
                    if (_bombWallRef < 0.0) { _bombWallRef = now; _bombTimerRef = timerLen; }
                    remaining = Math.Clamp(_bombTimerRef - (float)(now - _bombWallRef), 0f, timerLen);
                }
            }

            // ── Defuse countdown ────────────────────────────────────────────
            bool  reallyDefusing = beingDefused && !defused;
            float defuseLeft;
            float safeDefLen = Math.Max(0f, defuseLen);

            if (!reallyDefusing)
            {
                defuseLeft    = 0f;
                _defuseWallRef = -1.0;
            }
            else
            {
                float gvDefLeft = defuseEnd - curTime;
                if (curTime > 10f && gvDefLeft >= 0f && safeDefLen > 0f && gvDefLeft <= safeDefLen + 1f)
                {
                    defuseLeft    = gvDefLeft;
                    _defuseWallRef = -1.0;
                }
                else
                {
                    // Wall-clock fallback for defuse timer.
                    double now = _sw.Elapsed.TotalSeconds;
                    if (_defuseWallRef < 0.0)
                    {
                        _defuseWallRef = now;
                        _defuseLenRef  = safeDefLen > 0f ? safeDefLen : 5f; // default 5 s
                    }
                    defuseLeft = Math.Clamp(_defuseLenRef - (float)(now - _defuseWallRef), 0f, _defuseLenRef);
                }
            }

            return new BombEntity
            {
                Position       = origin,
                IsTicking      = ticking,
                IsDefused      = defused,
                IsBeingDefused = reallyDefusing,
                TimeRemaining  = remaining,
                DefuseTime     = defuseLeft,
                Site           = site >= 0 && site <= 1 ? site : 0,
            };
        }

        private static void Reset()
        {
            _bombWallRef   = -1.0;
            _defuseWallRef = -1.0;
            // Do NOT reset _lastBlowTime here — it's used to detect new bombs across rounds.
        }

        private static float ReadCurTime(ProcessMemory mem)
        {
            if (Offsets.GlobalVars == IntPtr.Zero) return 0f;
            IntPtr cgvars = mem.Read<IntPtr>(Offsets.GlobalVars);
            if (cgvars == IntPtr.Zero) return 0f;

            // m_flCurTime — try 0x10 (most common in CS2 schema) then 0x34 (older cite).
            float t = mem.Read<float>(IntPtr.Add(cgvars, 0x10));
            if (t > 10f && t < 1_000_000f) return t;

            t = mem.Read<float>(IntPtr.Add(cgvars, 0x34));
            if (t > 10f && t < 1_000_000f) return t;

            return 0f;
        }
    }
}
