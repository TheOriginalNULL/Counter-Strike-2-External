using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Scans entity slots 64-511 (chunk 0) and 0-511 (chunk 1) for live grenade
    /// projectiles.  Grenade entities are identified by a valid m_vInitialVelocity
    /// magnitude (100-3000 units/s) and a sane m_nBounces count (0-15).
    /// Those two fields are specific to C_BaseCSGrenadeProjectile — any other entity
    /// at these slot indices will either read zero (RPM failure) or an out-of-range value.
    /// </summary>
    internal static class GrenadeReader
    {
        private const int ChunkCapacity  = 512;
        private const int IdentityStride = 0x70;

        private static readonly byte[] _chunk0Buf = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _chunk1Buf = new byte[ChunkCapacity * IdentityStride];

        internal static List<GrenadeEntity> ReadGrenades(ProcessMemory mem)
        {
            var result = new List<GrenadeEntity>(16);

            IntPtr entitySystem = mem.Read<IntPtr>(Offsets.EntityList);
            if (entitySystem == IntPtr.Zero) return result;

            IntPtr chunk0Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x10));
            if (chunk0Ptr == IntPtr.Zero || !mem.ReadBytes(chunk0Ptr, _chunk0Buf))
                return result;

            IntPtr chunk1Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x18));
            bool   chunk1Ok  = chunk1Ptr != IntPtr.Zero
                               && mem.ReadBytes(chunk1Ptr, _chunk1Buf);

            // Slots 0-63 in chunk 0 are player controllers — skip them.
            ScanChunk(mem, _chunk0Buf, startEntry: 64, endEntry: ChunkCapacity, result);

            if (chunk1Ok)
                ScanChunk(mem, _chunk1Buf, startEntry: 0, endEntry: ChunkCapacity, result);

            return result;
        }

        private static void ScanChunk(ProcessMemory mem, byte[] chunkBuf,
            int startEntry, int endEntry, List<GrenadeEntity> result)
        {
            for (int i = startEntry; i < endEntry && result.Count < 64; i++)
            {
                IntPtr entity = MemoryMarshal.Read<IntPtr>(
                    chunkBuf.AsSpan(i * IdentityStride));
                if (entity == IntPtr.Zero) continue;

                // Team must be T (2) or CT (3) — world props, triggers, and lights all
                // read 0 here; this is the cheapest early-out before touching the scene node.
                byte team = mem.Read<byte>(IntPtr.Add(entity, EntitySchema.TeamNum));
                if (team != 2 && team != 3) continue;

                // Scene node (same C_BaseEntity offset as player pawns: 0x330).
                IntPtr sceneNode = mem.Read<IntPtr>(
                    IntPtr.Add(entity, EntitySchema.GameSceneNode));
                if (sceneNode == IntPtr.Zero) continue;

                // Current world position from scene node.
                Vector3 origin = mem.Read<Vector3>(
                    IntPtr.Add(sceneNode, EntitySchema.AbsOrigin));
                if (origin == Vector3.Zero) continue;

                // m_vInitialVelocity — set at throw time only on C_BaseCSGrenadeProjectile.
                // Tightened range: real grenade throws are 300-1300 units/s.
                Vector3 initVel   = mem.Read<Vector3>(IntPtr.Add(entity, EntitySchema.GrenadeInitialVel));
                float   initSpeed = initVel.Length();
                if (initSpeed < 300f || initSpeed > 1300f) continue;

                // m_vInitialPosition — where the grenade was thrown from.
                // Must be non-zero and within 3000 units of current position
                // (grenades can't fly further before exploding).
                Vector3 initPos = mem.Read<Vector3>(IntPtr.Add(entity, EntitySchema.GrenadeInitialPos));
                if (initPos == Vector3.Zero) continue;
                if (Vector3.DistanceSquared(initPos, origin) > 3000f * 3000f) continue;

                // m_nBounces — tightened to 0-5; more than 5 bounces never happens.
                int bounces = mem.Read<int>(IntPtr.Add(entity, EntitySchema.GrenadeNBounces));
                if ((uint)bounces > 5) continue;

                // Current velocity for trajectory prediction.
                Vector3 absVel = mem.Read<Vector3>(
                    IntPtr.Add(entity, EntitySchema.GrenadeAbsVelocity));

                GrenadeType type   = IsSmoke(mem, entity) ? GrenadeType.Smoke : GrenadeType.Frag;
                Vector3     landing = GrenadePredictor.Simulate(origin, absVel);

                result.Add(new GrenadeEntity
                {
                    Type     = type,
                    Position = origin,
                    Landing  = landing,
                });
            }
        }

        private static bool IsSmoke(ProcessMemory mem, IntPtr entity)
        {
            // C_SmokeGrenadeProjectile-only field: m_nSmokeEffectTickBegin (non-zero
            // once the smoke activates) or m_bDidSmokeEffect (set on deployment).
            int  smokeTick = mem.Read<int> (IntPtr.Add(entity, EntitySchema.GrenadeSmokeTick));
            byte smokeBit  = mem.Read<byte>(IntPtr.Add(entity, EntitySchema.GrenadeSmokeBit));
            return smokeTick != 0 || smokeBit != 0;
        }
    }
}
