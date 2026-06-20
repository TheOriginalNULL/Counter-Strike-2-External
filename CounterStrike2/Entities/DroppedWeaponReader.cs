using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Scans entity slots 64-511 (chunk 0) and 0-511 (chunk 1) for weapons that have
    /// no current owner (m_hOwnerEntity == 0xFFFFFFFF), meaning they are on the ground.
    /// </summary>
    internal static class DroppedWeaponReader
    {
        private const int ChunkCapacity  = 512;
        private const int IdentityStride = 0x70;
        private const uint InvalidHandle = 0xFFFFFFFF;

        private static readonly byte[] _chunk0Buf = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _chunk1Buf = new byte[ChunkCapacity * IdentityStride];

        internal static List<DroppedWeaponEntity> ReadLoot(ProcessMemory mem)
        {
            var result = new List<DroppedWeaponEntity>(16);

            IntPtr entitySystem = mem.Read<IntPtr>(Offsets.EntityList);
            if (entitySystem == IntPtr.Zero) return result;

            IntPtr chunk0Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x10));
            if (chunk0Ptr == IntPtr.Zero || !mem.ReadBytes(chunk0Ptr, _chunk0Buf))
                return result;

            IntPtr chunk1Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x18));
            bool   chunk1Ok  = chunk1Ptr != IntPtr.Zero
                               && mem.ReadBytes(chunk1Ptr, _chunk1Buf);

            ScanChunk(mem, _chunk0Buf, startEntry: 64, endEntry: ChunkCapacity, result);

            if (chunk1Ok)
                ScanChunk(mem, _chunk1Buf, startEntry: 0, endEntry: ChunkCapacity, result);

            return result;
        }

        private static void ScanChunk(ProcessMemory mem, byte[] chunkBuf,
            int startEntry, int endEntry, List<DroppedWeaponEntity> result)
        {
            for (int i = startEntry; i < endEntry && result.Count < 128; i++)
            {
                IntPtr entity = MemoryMarshal.Read<IntPtr>(
                    chunkBuf.AsSpan(i * IdentityStride));
                if (entity == IntPtr.Zero) continue;

                // Only show weapons with no current owner (lying on the ground).
                uint owner = mem.Read<uint>(IntPtr.Add(entity, EntitySchema.OwnerEntity));
                if (owner != InvalidHandle) continue;

                // Dropped weapons retain the team of the player who dropped them (2=T, 3=CT).
                // World props, triggers, and lights have team 0 — this kills most false positives.
                byte team = mem.Read<byte>(IntPtr.Add(entity, EntitySchema.TeamNum));
                if (team != 2 && team != 3) continue;

                // Item definition index identifies the weapon type.
                // Range 1-500 covers all CS2 weapons; 0 means no item.
                ushort itemDef = mem.Read<ushort>(IntPtr.Add(entity, EntitySchema.ItemDefIndex));
                if (itemDef == 0 || itemDef > 500) continue;

                // Resolve human-readable name; skip unknown IDs.
                string name = WeaponNames.Get(itemDef);
                if (name.Length == 0 || name[0] == '#') continue;

                // World position via scene node.
                IntPtr sceneNode = mem.Read<IntPtr>(
                    IntPtr.Add(entity, EntitySchema.GameSceneNode));
                if (sceneNode == IntPtr.Zero) continue;

                Vector3 origin = mem.Read<Vector3>(
                    IntPtr.Add(sceneNode, EntitySchema.AbsOrigin));
                if (origin == Vector3.Zero) continue;

                result.Add(new DroppedWeaponEntity
                {
                    Position   = origin,
                    WeaponName = name,
                    WeaponId   = itemDef,
                });
            }
        }
    }
}
