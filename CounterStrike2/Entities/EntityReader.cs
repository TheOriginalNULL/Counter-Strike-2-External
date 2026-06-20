using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Walks CS2's CGameEntitySystem using offsets from cs2-dumper (2026-06-17).
    ///
    /// Key optimisation: instead of one ReadProcessMemory call per entity slot (128 calls
    /// for 64 slots), the entire first chunk (512 entries × 112 bytes = 57,344 bytes) is
    /// read in a single call. Controller and pawn fields are then batched into one read
    /// each, eliminating most kernel transitions per cycle.
    /// </summary>
    public static class EntityReader
    {
        private const int ChunkCapacity  = 512;
        private const int IdentityStride = 0x70;  // 112 bytes per identity entry

        // ── Controller batch: covers SanitizedName (0x860) and PlayerPawnHandle (0x90C) ──
        private const int CtrlBase         = 0x860;
        private const int CtrlSize         = 0xB0;   // 0x860..0x910
        private const int CtrlOff_Name     = 0x860 - CtrlBase;  // 0
        private const int CtrlOff_Handle   = 0x90C - CtrlBase;  // 172

        // ── Pawn batch: covers GameSceneNode (0x330), Health (0x34C),
        //               LifeState (0x354), TeamNum (0x3EB) ──
        private const int PawnBase         = 0x330;
        private const int PawnSize         = 0xBC;   // 0x330..0x3EC
        private const int PawnOff_Node     = 0x330 - PawnBase;  // 0
        private const int PawnOff_Health   = 0x34C - PawnBase;  // 28
        private const int PawnOff_Life     = 0x354 - PawnBase;  // 36
        private const int PawnOff_Team     = 0x3EB - PawnBase;  // 187

        // Item def index for the C4 bomb.
        private const ushort C4ItemDef = 49;

        // ── Pre-allocated read buffers (single background thread — no locking needed) ──
        // Each chunk: 512 entries × 112 bytes = 57,344 bytes.
        // Chunks 0-1: players (controllers in 0, pawns in 1).
        // Chunk 2: weapon entities — C4 and late-spawned weapons often land in slots 1024-1535.
        private static readonly byte[] _chunk0Buf = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _chunk1Buf = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _chunk2Buf = new byte[ChunkCapacity * IdentityStride];
        private static readonly byte[] _ctrlBuf   = new byte[CtrlSize];
        private static readonly byte[] _pawnBuf   = new byte[PawnSize];
        private static readonly byte[] _boneBuf   = new byte[EntitySchema.BoneBatchBytes];

        // Cached C4 weapon entity pointer — avoids rescanning every frame.
        // Invalidated when the entity no longer reports item def 49 (new round / bomb planted).
        private static IntPtr _c4EntityCache = IntPtr.Zero;

        // Cached chunk availability — updated each ReadPlayers; used by skin changer via
        // LookupEntityInternal without requiring an extra ReadPlayers call.
        private static bool _chunk1OkCached;
        private static bool _chunk2OkCached;

        public static int    LastPtrCount    { get; private set; }
        public static int    LastRawCount    { get; private set; }
        public static IntPtr LocalWeaponPtr  { get; private set; } = IntPtr.Zero;
        public static ushort LocalWeaponId   { get; private set; }
        public static IntPtr LocalPawnPtr    { get; private set; } = IntPtr.Zero;

        // Clear cached local-player pointers so any stale, leftover pointer never outlives the
        // frame it was resolved in. Without this, leaving a match (entity system briefly
        // zero/unreadable during teardown) leaves LocalPawnPtr/LocalWeaponPtr pointing at
        // freed/reused memory while SkinChanger keeps writing into it every frame — a
        // use-after-free that can corrupt unrelated live objects and crash the game.
        private static void ClearLocalCaches()
        {
            LocalPawnPtr   = IntPtr.Zero;
            LocalWeaponPtr = IntPtr.Zero;
            LocalWeaponId  = 0;
        }

        public static List<PlayerEntity> ReadPlayers(ProcessMemory mem)
        {
            IntPtr entitySystem = mem.Read<IntPtr>(Offsets.EntityList);
            if (entitySystem == IntPtr.Zero)
            {
                LastPtrCount = LastRawCount = 0;
                ClearLocalCaches();
                return new List<PlayerEntity>();
            }

            // One RPM per chunk. Chunk 0 holds controllers (slots 0-511); chunk 1 holds
            // pawns after rejoins/map loads (slots 512-1023). Chunk pointers are stored
            // as a pointer array at entitySystem+0x10.
            IntPtr chunk0Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x10));
            if (chunk0Ptr == IntPtr.Zero || !mem.ReadBytes(chunk0Ptr, _chunk0Buf))
            {
                LastPtrCount = LastRawCount = 0;
                ClearLocalCaches();
                return new List<PlayerEntity>();
            }
            IntPtr chunk1Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x18));
            bool   chunk1Ok  = chunk1Ptr != IntPtr.Zero && mem.ReadBytes(chunk1Ptr, _chunk1Buf);
            IntPtr chunk2Ptr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x20));
            bool   chunk2Ok  = chunk2Ptr != IntPtr.Zero && mem.ReadBytes(chunk2Ptr, _chunk2Buf);
            _chunk1OkCached  = chunk1Ok;
            _chunk2OkCached  = chunk2Ok;

            // Identify local pawn from the LocalPlayerController global.
            IntPtr localPawn = IntPtr.Zero;
            IntPtr localCtrl = mem.Read<IntPtr>(Offsets.LocalPlayerController);
            if (localCtrl != IntPtr.Zero && mem.ReadBytes(IntPtr.Add(localCtrl, CtrlBase), _ctrlBuf))
            {
                uint lh = MemoryMarshal.Read<uint>(_ctrlBuf.AsSpan(CtrlOff_Handle));
                if (lh != 0 && lh != 0xFFFFFFFF)
                    localPawn = LookupEntity(lh & 0x7FFF, chunk1Ok, chunk2Ok);
            }

            LocalPawnPtr = localPawn;

            // Find the C4 owner pawn once per tick (cached after first scan).
            IntPtr c4OwnerPawn = FindC4OwnerPawn(mem, chunk1Ok, chunk2Ok);

            int ptrs = 0, raw = 0;
            var result = new List<PlayerEntity>(12);

            for (int i = 0; i < 64; i++)
            {
                // Extract controller pointer directly from the pre-loaded chunk 0 buffer.
                IntPtr controller = MemoryMarshal.Read<IntPtr>(_chunk0Buf.AsSpan(i * IdentityStride));
                if (controller == IntPtr.Zero) continue;
                ptrs++;

                // One RPM: controller batch covers name pointer + pawn handle.
                if (!mem.ReadBytes(IntPtr.Add(controller, CtrlBase), _ctrlBuf)) continue;

                uint handle = MemoryMarshal.Read<uint>(_ctrlBuf.AsSpan(CtrlOff_Handle));
                if (handle is 0 or 0xFFFFFFFF) continue;

                // Pawn can be in chunk 0 (slots 0-511) or chunk 1 (slots 512-1023).
                IntPtr pawn = LookupEntity(handle & 0x7FFF, chunk1Ok, chunk2Ok);
                if (pawn == IntPtr.Zero) continue;
                raw++;

                // One RPM: pawn batch covers scene node ptr + health + lifeState + team.
                if (!mem.ReadBytes(IntPtr.Add(pawn, PawnBase), _pawnBuf)) continue;

                IntPtr sceneNode = MemoryMarshal.Read<IntPtr>(_pawnBuf.AsSpan(PawnOff_Node));
                int    health    = MemoryMarshal.Read<int>    (_pawnBuf.AsSpan(PawnOff_Health));
                byte   lifeState = _pawnBuf[PawnOff_Life];
                byte   team      = _pawnBuf[PawnOff_Team];
                bool   alive     = lifeState == 0 && health is > 0 and <= 100;

                // One RPM: origin from scene node (12 bytes, zero-alloc Read<Vector3>).
                Vector3 origin = sceneNode != IntPtr.Zero
                    ? mem.Read<Vector3>(IntPtr.Add(sceneNode, EntitySchema.AbsOrigin))
                    : Vector3.Zero;

                // One RPM (stack-alloc): player name string.
                IntPtr namePtr = MemoryMarshal.Read<IntPtr>(_ctrlBuf.AsSpan(CtrlOff_Name));
                string name    = namePtr != IntPtr.Zero
                    ? mem.ReadString(namePtr, 64)
                    : string.Empty;

                // Active weapon: 2 chained RPMs (services ptr → handle) + 1 for item def.
                // Weapon entity pointer comes from the pre-loaded chunk buffer (zero extra RPM).
                ushort weaponId     = 0;
                IntPtr weaponEntity = IntPtr.Zero;
                IntPtr weaponSvc    = mem.Read<IntPtr>(IntPtr.Add(pawn, EntitySchema.WeaponServices));
                if (weaponSvc != IntPtr.Zero)
                {
                    uint wh = mem.Read<uint>(IntPtr.Add(weaponSvc, EntitySchema.ActiveWeapon));
                    if (wh != 0 && wh != 0xFFFFFFFF)
                    {
                        weaponEntity = LookupEntity(wh & 0x7FFF, chunk1Ok, chunk2Ok);
                        if (weaponEntity != IntPtr.Zero)
                            weaponId = mem.Read<ushort>(IntPtr.Add(weaponEntity, EntitySchema.ItemDefIndex));
                    }
                }

                if (pawn == localPawn)
                {
                    LocalWeaponPtr = weaponEntity;
                    LocalWeaponId  = weaponId;
                }

                // Bone data — one batch RPM covering indices 0-27, skipped when toggle is off.
                BoneData bones = default;
                if (Config.Current.EspSkeleton && sceneNode != IntPtr.Zero)
                {
                    IntPtr boneArrayPtr = mem.Read<IntPtr>(
                        IntPtr.Add(sceneNode, EntitySchema.BoneArrayPtr));
                    if (boneArrayPtr != IntPtr.Zero
                        && mem.ReadBytes(boneArrayPtr, _boneBuf))
                    {
                        bones.Head      = BonePos(EntitySchema.Bone_Head);
                        bones.Neck      = BonePos(EntitySchema.Bone_Neck);
                        bones.Chest     = BonePos(EntitySchema.Bone_Chest);
                        bones.Waist     = BonePos(EntitySchema.Bone_Waist);
                        bones.LShoulder = BonePos(EntitySchema.Bone_LShoulder);
                        bones.LElbow    = BonePos(EntitySchema.Bone_LElbow);
                        bones.LHand     = BonePos(EntitySchema.Bone_LHand);
                        bones.RShoulder = BonePos(EntitySchema.Bone_RShoulder);
                        bones.RElbow    = BonePos(EntitySchema.Bone_RElbow);
                        bones.RHand     = BonePos(EntitySchema.Bone_RHand);
                        bones.LHip      = BonePos(EntitySchema.Bone_LHip);
                        bones.LKnee     = BonePos(EntitySchema.Bone_LKnee);
                        bones.LFoot     = BonePos(EntitySchema.Bone_LFoot);
                        bones.RHip      = BonePos(EntitySchema.Bone_RHip);
                        bones.RKnee     = BonePos(EntitySchema.Bone_RKnee);
                        bones.RFoot     = BonePos(EntitySchema.Bone_RFoot);
                        bones.Valid     = true;
                    }

                    // Debug: read all 48 bones so EspLayer can draw numbered dots.
                    if (Config.Current.BoneDebug && boneArrayPtr != IntPtr.Zero)
                    {
                        var dbuf = new byte[EntitySchema.BoneDebugBytes];
                        if (mem.ReadBytes(boneArrayPtr, dbuf))
                        {
                            var db = new Vector3[EntitySchema.BoneDebugCount];
                            for (int bi = 0; bi < EntitySchema.BoneDebugCount; bi++)
                                db[bi] = MemoryMarshal.Read<Vector3>(
                                    dbuf.AsSpan(bi * EntitySchema.BoneStride));
                            bones.DebugBones = db;
                        }
                    }
                }

                result.Add(new PlayerEntity
                {
                    IsValid  = true,
                    IsAlive  = alive,
                    IsLocal  = pawn == localPawn,
                    Health   = health,
                    Team     = team,
                    Name     = name,
                    Origin   = origin,
                    WeaponId = weaponId,
                    HasC4    = pawn == c4OwnerPawn,
                    Bones    = bones,
                    PawnPtr  = pawn,
                });
            }

            LastPtrCount = ptrs;
            LastRawCount = raw;
            return result;
        }

        // Extract a bone world-space position from the pre-loaded bone buffer.
        private static System.Numerics.Vector3 BonePos(int boneId)
            => MemoryMarshal.Read<System.Numerics.Vector3>(
                _boneBuf.AsSpan(boneId * EntitySchema.BoneStride));

        // Find the player pawn that owns the C4 weapon entity.
        // Cache hit (99% of frames): 2 RPMs — validate item def + read owner handle.
        // Cache miss (once per round): scans all loaded chunk buffers for item def 49.
        private static IntPtr FindC4OwnerPawn(ProcessMemory mem, bool chunk1Ok, bool chunk2Ok)
        {
            if (_c4EntityCache != IntPtr.Zero)
            {
                if (mem.Read<ushort>(IntPtr.Add(_c4EntityCache, EntitySchema.ItemDefIndex)) == C4ItemDef)
                {
                    uint oh = mem.Read<uint>(IntPtr.Add(_c4EntityCache, EntitySchema.OwnerEntity));
                    if (oh == 0 || oh == 0xFFFFFFFF) return IntPtr.Zero;
                    return LookupEntity(oh & 0x7FFF, chunk1Ok, chunk2Ok);
                }
                _c4EntityCache = IntPtr.Zero; // entity gone or recycled — drop cache
            }

            // Scan chunk 0, 1, 2 for the entity whose item def == 49.
            // Each non-null slot costs 1 RPM; done once per round on cache miss.
            _c4EntityCache = IntPtr.Zero;
            for (int ci = 0; ci < 3; ci++)
            {
                if (ci == 1 && !chunk1Ok) continue;
                if (ci == 2 && !chunk2Ok) continue;
                byte[] buf = ci == 0 ? _chunk0Buf : ci == 1 ? _chunk1Buf : _chunk2Buf;
                for (int i = 0; i < ChunkCapacity; i++)
                {
                    IntPtr ptr = MemoryMarshal.Read<IntPtr>(buf.AsSpan(i * IdentityStride));
                    if (ptr == IntPtr.Zero) continue;
                    if (mem.Read<ushort>(IntPtr.Add(ptr, EntitySchema.ItemDefIndex)) == C4ItemDef)
                    {
                        _c4EntityCache = ptr;
                        uint oh = mem.Read<uint>(IntPtr.Add(ptr, EntitySchema.OwnerEntity));
                        if (oh == 0 || oh == 0xFFFFFFFF) return IntPtr.Zero;
                        return LookupEntity(oh & 0x7FFF, chunk1Ok, chunk2Ok);
                    }
                }
            }
            return IntPtr.Zero;
        }

        // Look up an entity pointer from the pre-loaded chunk buffers (zero extra RPM).
        // Slots 0-511 → chunk 0; 512-1023 → chunk 1; 1024-1535 → chunk 2 (weapon entities).
        private static IntPtr LookupEntity(uint slot, bool chunk1Ok, bool chunk2Ok)
        {
            int entry = (int)(slot & (ChunkCapacity - 1));   // slot % 512
            int chunk = (int)(slot >> 9);                     // slot / 512
            return chunk switch
            {
                0 => MemoryMarshal.Read<IntPtr>(_chunk0Buf.AsSpan(entry * IdentityStride)),
                1 when chunk1Ok => MemoryMarshal.Read<IntPtr>(_chunk1Buf.AsSpan(entry * IdentityStride)),
                2 when chunk2Ok => MemoryMarshal.Read<IntPtr>(_chunk2Buf.AsSpan(entry * IdentityStride)),
                _ => IntPtr.Zero,
            };
        }

        /// <summary>
        /// Look up an entity slot using the chunk buffers from the last ReadPlayers call.
        /// Safe to call from the skin changer between ReadPlayers frames. Falls back to a
        /// direct (uncached) chunk read for slots outside 0-1535 — CS2 places some entities
        /// (e.g. the HUD/viewmodel arms entity) at much higher indices than players/weapons.
        /// </summary>
        internal static IntPtr LookupEntityInternal(ProcessMemory mem, uint slot)
        {
            int chunk = (int)(slot >> 9);
            if (chunk <= 2)
                return LookupEntity(slot, _chunk1OkCached, _chunk2OkCached);

            IntPtr entitySystem = mem.Read<IntPtr>(Offsets.EntityList);
            if (entitySystem == IntPtr.Zero) return IntPtr.Zero;

            IntPtr chunkPtr = mem.Read<IntPtr>(IntPtr.Add(entitySystem, 0x10 + chunk * 8));
            if (chunkPtr == IntPtr.Zero) return IntPtr.Zero;

            int entry = (int)(slot & (ChunkCapacity - 1));
            return mem.Read<IntPtr>(IntPtr.Add(chunkPtr, entry * IdentityStride));
        }

        /// <summary>
        /// Return pointers to every weapon in the local pawn's m_hMyWeapons list,
        /// resolved through the cached chunk buffers (zero extra RPM per weapon).
        /// </summary>
        public static List<IntPtr> GetLocalWeapons(ProcessMemory mem)
        {
            var result = new List<IntPtr>(8);
            IntPtr pawn = LocalPawnPtr;
            if (pawn == IntPtr.Zero) return result;

            IntPtr svc = mem.Read<IntPtr>(IntPtr.Add(pawn, EntitySchema.WeaponServices));
            if (svc == IntPtr.Zero) return result;

            long   count   = mem.Read<long>  (IntPtr.Add(svc, EntitySchema.WeaponCount));
            IntPtr entries = mem.Read<IntPtr>(IntPtr.Add(svc, EntitySchema.WeaponEntries));

            if (entries == IntPtr.Zero || count <= 0 || count > 64) return result;

            for (int i = 0; i < (int)count; i++)
            {
                uint handle = mem.Read<uint>(IntPtr.Add(entries, i * 4));
                if (handle == 0 || handle == 0xFFFFFFFF) continue;
                IntPtr weapon = LookupEntity(handle & 0x7FFF, _chunk1OkCached, _chunk2OkCached);
                if (weapon != IntPtr.Zero)
                    result.Add(weapon);
            }

            return result;
        }
    }
}
