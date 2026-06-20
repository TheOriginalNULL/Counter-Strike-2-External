using System;
using System.Collections.Generic;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Detects when the local player acquires a new weapon (buy menu or ground pickup).
    /// Chain: LocalPlayerPawn → m_pWeaponServices (0x11E0)
    ///        → m_hMyWeapons array (0x48) + m_hActiveWeapon (0x60).
    /// Tracks per-life known handles; new handle = new weapon equipped.
    /// A 2-second grace window after spawn seeds starting gear silently.
    /// </summary>
    internal static class WeaponEquipTracker
    {
        // m_hMyWeapons: CNetworkUtlVectorBase at 0x48
        //   +0x00 = m_pMemory (IntPtr, pointer to uint32[] handles)
        //   +0x10 = m_Size    (int32, weapon count)
        private const int WeaponsVecOffset    = 0x48;
        private const int WeaponsVecCountOff  = 0x10; // relative to vec base
        private const int MaxWeapons          = 16;

        private static readonly HashSet<uint> _known = new();
        private static bool     _wasAlive;
        private static DateTime _aliveAt = DateTime.MinValue;

        internal static bool Check(ProcessMemory mem)
        {
            if (Offsets.LocalPlayerPawn == IntPtr.Zero) return false;

            IntPtr pawn = mem.Read<IntPtr>(Offsets.LocalPlayerPawn);
            if (pawn == IntPtr.Zero) return false;

            byte lifeState = mem.Read<byte>(IntPtr.Add(pawn, EntitySchema.LifeState));
            bool alive = lifeState == 0;

            if (alive && !_wasAlive)
            {
                _known.Clear();
                _aliveAt = DateTime.UtcNow;
            }
            _wasAlive = alive;
            if (!alive) return false;

            IntPtr weaponSvc = mem.Read<IntPtr>(IntPtr.Add(pawn, EntitySchema.WeaponServices));
            if (weaponSvc == IntPtr.Zero) return false;

            bool inGrace = (DateTime.UtcNow - _aliveAt).TotalSeconds < 2.0;
            if (inGrace)
            {
                // Seed ALL held weapons during grace window so switching to them later is silent
                SeedInventory(mem, weaponSvc);
                return false;
            }

            // After grace: new active-weapon handle = weapon just acquired
            uint active = mem.Read<uint>(IntPtr.Add(weaponSvc, EntitySchema.ActiveWeapon));
            if (active is 0 or 0xFFFFFFFF) return false;

            return _known.Add(active); // true only first time we see this handle
        }

        internal static void Reset()
        {
            _known.Clear();
            _wasAlive = false;
            _aliveAt  = DateTime.MinValue;
        }

        private static void SeedInventory(ProcessMemory mem, IntPtr weaponSvc)
        {
            IntPtr vecBase  = IntPtr.Add(weaponSvc, WeaponsVecOffset);
            IntPtr dataPtr  = mem.Read<IntPtr>(vecBase);
            int    count    = mem.Read<int>(IntPtr.Add(vecBase, WeaponsVecCountOff));
            if (dataPtr == IntPtr.Zero || count <= 0 || count > MaxWeapons) return;

            for (int i = 0; i < count; i++)
            {
                uint h = mem.Read<uint>(IntPtr.Add(dataPtr, i * 4));
                if (h is not (0 or 0xFFFFFFFF))
                    _known.Add(h);
            }
        }
    }
}
