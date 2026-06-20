using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Finds all players whose observer target is the local player.
    /// Chain: controller → m_hObserverPawn (0x910) → C_CSObserverPawn
    ///        → m_pObserverServices (0x11F8) → m_hObserverTarget (0x4C)
    ///        → compare entity ptr with local pawn OR local controller.
    /// Reads chunks 0-2 to cover observer pawns allocated at any slot.
    /// </summary>
    internal static class SpectatorReader
    {
        private const int ChunkCapacity  = 512;
        private const int IdentityStride = 0x70;

        private static readonly byte[] _chunk0 = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _chunk1 = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _chunk2 = new byte[ChunkCapacity * IdentityStride];

        internal static List<string> Read(ProcessMemory mem, IntPtr localPawnPtr)
        {
            var result = new List<string>(4);
            if (Offsets.EntityList == IntPtr.Zero) return result;

            IntPtr localCtrl = mem.Read<IntPtr>(Offsets.LocalPlayerController);
            if (localCtrl == IntPtr.Zero) return result;

            IntPtr entitySystem = mem.Read<IntPtr>(Offsets.EntityList);
            if (entitySystem == IntPtr.Zero) return result;

            IntPtr chunk0Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x10));
            if (chunk0Ptr == IntPtr.Zero || !mem.ReadBytes(chunk0Ptr, _chunk0)) return result;

            IntPtr chunk1Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x18));
            bool   chunk1Ok  = chunk1Ptr != IntPtr.Zero && mem.ReadBytes(chunk1Ptr, _chunk1);

            IntPtr chunk2Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x20));
            bool   chunk2Ok  = chunk2Ptr != IntPtr.Zero && mem.ReadBytes(chunk2Ptr, _chunk2);

            for (int i = 0; i < 64; i++)
            {
                IntPtr controller = MemoryMarshal.Read<IntPtr>(_chunk0.AsSpan(i * IdentityStride));
                if (controller == IntPtr.Zero || controller == localCtrl) continue;

                // Skip alive players — CS2 keeps observer pawns allocated even during live play
                byte pawnAlive = mem.Read<byte>(IntPtr.Add(controller, 0x914)); // m_bPawnIsAlive
                if (pawnAlive != 0) continue;

                // Get the observer pawn handle
                uint obsPawnHandle = mem.Read<uint>(IntPtr.Add(controller, EntitySchema.ObserverPawnHandle));
                if (obsPawnHandle is 0 or 0xFFFFFFFF) continue;

                IntPtr obsPawn = Lookup(obsPawnHandle & 0x7FFF, chunk1Ok, chunk2Ok);
                if (obsPawn == IntPtr.Zero) continue;

                // C_CSObserverPawn inherits C_BasePlayerPawn → m_pObserverServices at 0x11F8
                IntPtr obsSvc = mem.Read<IntPtr>(IntPtr.Add(obsPawn, EntitySchema.ObserverServices));
                if (obsSvc == IntPtr.Zero) continue;

                // m_iObserverMode must be non-zero — 0 = OBS_MODE_NONE (not actually spectating)
                int obsMode = mem.Read<int>(IntPtr.Add(obsSvc, 0x48));
                if (obsMode == 0) continue;

                uint targetHandle = mem.Read<uint>(IntPtr.Add(obsSvc, EntitySchema.ObserverTarget));
                if (targetHandle is 0 or 0xFFFFFFFF) continue;

                // Target can be the watched player's pawn OR controller depending on CS2 version
                IntPtr targetEntity = Lookup(targetHandle & 0x7FFF, chunk1Ok, chunk2Ok);
                if (targetEntity == IntPtr.Zero) continue;
                if (targetEntity != localPawnPtr && targetEntity != localCtrl) continue;

                IntPtr namePtr = mem.Read<IntPtr>(IntPtr.Add(controller, EntitySchema.SanitizedName));
                string name    = namePtr != IntPtr.Zero ? mem.ReadString(namePtr, 64) : string.Empty;
                if (name.Length > 0 && result.Count < 12)
                    result.Add(name);
            }

            return result;
        }

        private static IntPtr Lookup(uint slot, bool chunk1Ok, bool chunk2Ok)
        {
            int entry = (int)(slot & (ChunkCapacity - 1));
            int chunk  = (int)(slot >> 9);
            return chunk switch
            {
                0 => MemoryMarshal.Read<IntPtr>(_chunk0.AsSpan(entry * IdentityStride)),
                1 when chunk1Ok => MemoryMarshal.Read<IntPtr>(_chunk1.AsSpan(entry * IdentityStride)),
                2 when chunk2Ok => MemoryMarshal.Read<IntPtr>(_chunk2.AsSpan(entry * IdentityStride)),
                _ => IntPtr.Zero,
            };
        }
    }
}
