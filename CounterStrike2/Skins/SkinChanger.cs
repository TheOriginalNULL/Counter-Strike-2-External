using System;
using System.Collections.Generic;
using CounterStrike2.Entities;
using CounterStrike2.Memory;

namespace CounterStrike2.Skins
{
    public static class SkinChanger
    {
        private static readonly object _lock = new();

        // RegenerateWeaponSkins — found once via sig scan, then patched and cached.
        private static IntPtr _regenFunc;
        private static bool   _regenReady;

        // Set by ForceReapply() to clear per-weapon processed markers on the next tick.
        private static bool _forceUpdate;

        // SkinMap key used when a weapon entity's own m_iItemDefinitionIndex reads 0 —
        // CS2's default/unskinned starting knife reports 0 (no item assigned yet), which
        // would otherwise collide with "no weapon held" everywhere wid==0 is checked.
        public const int DefaultKnifeSlot = -1;

        public static uint LastPaintKitReadback   { get; private set; }
        public static int  LastItemIDHighReadback { get; private set; }

        // ── GetHudWeapon diagnostics — exposed for the Debug tab ────────────────
        public static uint   LastArmsHandle   { get; private set; }
        public static IntPtr LastArmsEntity   { get; private set; }
        public static IntPtr LastArmsNode     { get; private set; }
        public static int    LastChildrenWalked { get; private set; }
        public static IntPtr LastHudWeaponFound { get; private set; }

        // ── Public API ────────────────────────────────────────────────────────

        public static void Tick(ProcessMemory mem, IntPtr localPawn)
        {
            if (!Config.Current.SkinChangerEnabled) return;
            if (localPawn == IntPtr.Zero) return;

            EnsureInit(mem);
            if (!_regenReady) return;

            List<IntPtr> weapons = EntityReader.GetLocalWeapons(mem);
            if (weapons.Count == 0) return;

            // Clear processed markers so every weapon is re-evaluated this tick.
            if (_forceUpdate)
            {
                foreach (var w in weapons)
                    mem.Write<int>(IntPtr.Add(w, EntitySchema.ItemIDHigh), 0);
                _forceUpdate = false;
            }

            bool shouldUpdate = false;
            var applied = new List<(IntPtr weapon, IntPtr block)>(weapons.Count);

            foreach (var weapon in weapons)
            {
                // m_iItemIDHigh == -1 is our sentinel: weapon was already processed.
                int idh = mem.Read<int>(IntPtr.Add(weapon, EntitySchema.ItemIDHigh));
                if (idh == -1) continue;

                // Mark as processed immediately so next tick skips it.
                mem.Write<int>(IntPtr.Add(weapon, EntitySchema.ItemIDHigh), -1);

                // wid==0 happens for CS2's default/unassigned starting knife (also seen as the
                // classic CS:GO CT/T placeholder ids 41/42) — map to a sentinel key distinct
                // from "no weapon held" rather than colliding with it.
                ushort wid = mem.Read<ushort>(IntPtr.Add(weapon, EntitySchema.ItemDefIndex));
                int key = wid == 0 ? DefaultKnifeSlot : wid;
                SkinPreset? preset;
                lock (_lock) Config.Current.SkinMap.TryGetValue(key, out preset);
                if (preset == null || preset.PaintKit == 0) continue;

                // Knives are painted exactly like guns — only the paint/wear/seed on whatever
                // model is already equipped changes. CS2 resolves which knife/glove MODEL to
                // bind once at equip time, not continuously, so switching knife types requires
                // actually re-equipping the real item (e.g. console `give weapon_knife_karambit`
                // in an offline/-insecure match) rather than a memory patch while already held.
                float wear = preset.Wear > 0 ? preset.Wear : 0.01f;

                // Write fallback values — RegenerateWeaponSkins reads these directly.
                mem.Write<uint> (IntPtr.Add(weapon, EntitySchema.FallbackPaintKit), (uint)preset.PaintKit);
                mem.Write<float>(IntPtr.Add(weapon, EntitySchema.FallbackWear),     wear);
                mem.Write<uint> (IntPtr.Add(weapon, EntitySchema.FallbackSeed),     (uint)preset.Seed);

                // mask = LegacyModel ? 2 : 1  (friend's: const uint64_t mask = skin.bUsesOldModel + 1)
                ulong mask = preset.LegacyModel ? 2UL : 1UL;

                // Apply mesh mask to the world entity and to the viewmodel (HUD weapon).
                SetMeshMask(mem, weapon, mask);
                IntPtr hud = GetHudWeapon(mem, weapon, localPawn);
                if (hud != IntPtr.Zero)
                    SetMeshMask(mem, hud, mask);

                // Write the 3-attribute CEconItemAttribute list that RegenerateWeaponSkins reads.
                IntPtr block = WriteAttrList(mem, weapon, preset.PaintKit, preset.Seed, wear);
                applied.Add((weapon, block));

                LastPaintKitReadback   = mem.Read<uint>(IntPtr.Add(weapon, EntitySchema.FallbackPaintKit));
                LastItemIDHighReadback = mem.Read<int> (IntPtr.Add(weapon, EntitySchema.ItemIDHigh));

                shouldUpdate = true;
            }

            if (!shouldUpdate) return;

            mem.CallThread(_regenFunc);

            // Cleanup — matches friend's UpdateWeapons: reset sentinel, remove attr list, free block.
            foreach (var (weapon, block) in applied)
            {
                mem.Write<uint>(IntPtr.Add(weapon, EntitySchema.FallbackPaintKit), 0xFFFFFFFF);
                RemoveAttrList(mem, weapon);
                if (block != IntPtr.Zero)
                    mem.Free(block);
            }
        }

        /// <summary>
        /// Clear per-weapon processed markers so every weapon is re-skinned on the next tick.
        /// Call this after the user presses Apply or changes a preset.
        /// </summary>
        public static void ForceReapply()
        {
            _forceUpdate = true;
            // Also reset the init state so the sig scan re-runs if it failed previously
            // (e.g. CS2 was not fully loaded yet on the first attempt).
            if (!_regenReady) { _regenFunc = IntPtr.Zero; }
        }

        // ── Glove changer ────────────────────────────────────────────────────
        // Gloves are NOT a weapon entity — m_EconGloves is an embedded C_EconItemView
        // directly on the pawn, with no C_AttributeContainer wrapper and no
        // RegenerateWeaponSkins call. CS2 re-applies the glove model via the
        // m_bNeedToReApplyGloves flag instead.

        // Sentinel (-1) so the very first tick after a preset is (re)saved always reallocates
        // the attribute block, even if its def/paint happen to coincidentally equal 0.
        private static int    _lastGloveDef   = -1;
        private static int    _lastGlovePaint = -1;
        private static IntPtr _gloveAttrBlock = IntPtr.Zero;

        // ── Glove diagnostics — exposed for the Skins tab debug label ───────────
        public static ushort LastGloveDefReadback         { get; private set; }
        public static bool   LastGloveInitReadback        { get; private set; }
        public static bool   LastGloveNeedReapplyReadback { get; private set; }

        // How many ticks (~8ms each ≈ 480ms) to keep actively writing glove fields after a
        // change before going completely silent. This is the real fix for the match-transition
        // crash: holding m_bNeedToReApplyGloves (and friends) permanently true — as the previous
        // version did — almost certainly made CS2's own glove-reapply logic re-trigger every
        // single tick for the rest of the match, colliding with the game's own pawn
        // creation/teardown at match transitions far more often than a brief, bounded burst
        // would. Manually clearing the preset already avoided the crash because it stopped ALL
        // writes outright; this makes the steady-state (after the burst) behave the same way —
        // fully quiet — without requiring the user to manually clear it first.
        private const int GloveActiveWindowTicks = 60;
        private static int  _gloveTicksSinceChange;
        private static bool _gloveSettled;

        public static void TickGloves(ProcessMemory mem, IntPtr localPawn)
        {
            if (localPawn == IntPtr.Zero)
            {
                // Pawn is gone (disconnected/between matches) — whatever entity referenced our
                // block no longer exists, so there's nothing left to zero out on its side; just
                // reclaim our own memory so it doesn't sit orphaned for the rest of the session.
                if (_gloveAttrBlock != IntPtr.Zero)
                {
                    mem.Free(_gloveAttrBlock);
                    _gloveAttrBlock = IntPtr.Zero;
                }
                return;
            }

            GlovePreset? glove = null;
            if (Config.Current.SkinChangerEnabled)
                lock (_lock) glove = Config.Current.GlovePreset;

            IntPtr econGloves = IntPtr.Add(localPawn, EntitySchema.EconGloves);
            IntPtr attrAddr   = IntPtr.Add(econGloves, EntitySchema.EconItemView_AttrList);

            // No active preset (cleared, disabled, or never set) — release any outstanding
            // block before it can ever become a dangling pointer the game tries to free itself.
            if (glove == null || glove.DefIndex == 0)
            {
                FreeGloveAttrList(mem, attrAddr);
                _gloveSettled = true;
                return;
            }

            bool changed = glove.DefIndex != _lastGloveDef || glove.PaintKit != _lastGlovePaint;
            if (changed)
            {
                FreeGloveAttrList(mem, attrAddr);
                if (glove.PaintKit != 0)
                {
                    float wear = glove.Wear > 0 ? glove.Wear : 0.01f;
                    _gloveAttrBlock = WriteAttrListAt(mem, attrAddr, glove.PaintKit, glove.Seed, wear);
                }
                _lastGloveDef          = glove.DefIndex;
                _lastGlovePaint        = glove.PaintKit;
                _gloveTicksSinceChange = 0;
                _gloveSettled          = false;
            }

            // Already finished the active burst for this preset — touch nothing. This is the
            // steady state for the vast majority of the match, identical to "no preset configured."
            if (_gloveSettled) return;

            mem.Write<bool>(IntPtr.Add(econGloves, EntitySchema.EconItemView_Initialized), false);
            mem.Write<ushort>(IntPtr.Add(econGloves, EntitySchema.EconItemView_ItemDefIndex), (ushort)glove.DefIndex);
            mem.Write<int>(IntPtr.Add(econGloves, EntitySchema.EconItemView_ItemIDHigh), -1);
            mem.Write<int>(IntPtr.Add(econGloves, EntitySchema.EconItemView_ItemIDLow), -1);
            mem.Write<int>(IntPtr.Add(econGloves, EntitySchema.EconItemView_EntityQuality), 3);  // unique

            // Give the gloves a consistent account/ownership ID, mirroring a real inventory
            // item. Sourced from an already-owned weapon's own (in-bounds) m_iAccountID field
            // rather than the friend's out-of-bounds C_EconEntity::m_OriginalOwnerXuidLow write
            // (which targets the wrong struct entirely and corrupts adjacent pawn memory).
            uint accountId = 12345;
            List<IntPtr> weapons = EntityReader.GetLocalWeapons(mem);
            if (weapons.Count > 0)
            {
                uint wAccount = mem.Read<uint>(IntPtr.Add(weapons[0], EntitySchema.ItemAccountID));
                if (wAccount != 0) accountId = wAccount;
            }
            mem.Write<uint>(IntPtr.Add(econGloves, EntitySchema.EconItemView_AccountID), accountId);

            mem.Write<bool>(IntPtr.Add(econGloves, EntitySchema.EconItemView_Initialized), true);
            mem.Write<bool>(IntPtr.Add(localPawn, EntitySchema.NeedToReApplyGloves), true);

            LastGloveDefReadback         = mem.Read<ushort>(IntPtr.Add(econGloves, EntitySchema.EconItemView_ItemDefIndex));
            LastGloveInitReadback        = mem.Read<bool>(IntPtr.Add(econGloves, EntitySchema.EconItemView_Initialized));
            LastGloveNeedReapplyReadback = mem.Read<bool>(IntPtr.Add(localPawn, EntitySchema.NeedToReApplyGloves));

            // Burst window elapsed — free the attribute block and go fully silent until the
            // user changes the preset again.
            if (++_gloveTicksSinceChange >= GloveActiveWindowTicks)
            {
                FreeGloveAttrList(mem, attrAddr);
                _gloveSettled = true;
            }
        }

        // Zero the live CPtrGameVector field (if it still points at our block) and free our
        // VirtualAllocEx'd memory ourselves — never leave CS2 to discover and free it on its own.
        private static void FreeGloveAttrList(ProcessMemory mem, IntPtr attrAddr)
        {
            if (_gloveAttrBlock == IntPtr.Zero) return;
            mem.Write<long>(attrAddr, 0L);
            mem.Write<long>(IntPtr.Add(attrAddr, 8), 0L);
            mem.Free(_gloveAttrBlock);
            _gloveAttrBlock = IntPtr.Zero;
        }

        public static void SaveGlovePreset(GlovePreset preset)
        {
            lock (_lock) Config.Current.GlovePreset = preset;
        }

        public static void RemoveGlovePreset()
        {
            lock (_lock) Config.Current.GlovePreset = null;
            _lastGloveDef = _lastGlovePaint = -1;
        }

        public static void ForceReapplyGloves() => _lastGloveDef = _lastGlovePaint = -1;

        public static void SavePreset(int weaponId, SkinPreset preset)
        {
            lock (_lock) Config.Current.SkinMap[weaponId] = preset;
        }

        public static void RemovePreset(int weaponId)
        {
            lock (_lock) Config.Current.SkinMap.Remove(weaponId);
        }

        public static SkinPreset? GetPreset(int weaponId)
        {
            lock (_lock)
                return Config.Current.SkinMap.TryGetValue(weaponId, out var p) ? p : null;
        }

        // ── Initialisation ────────────────────────────────────────────────────

        private static void EnsureInit(ProcessMemory mem)
        {
            if (_regenReady) return;

            _regenFunc = mem.SigScan("client.dll",
                "48 83 EC ? E8 ? ? ? ? 48 85 C0 0F 84 ? ? ? ? 48 8B 10");
            if (_regenFunc == IntPtr.Zero) return;

            // Patch the displacement at +0x52 inside RegenerateWeaponSkins so the function
            // reads from our CPtrGameVector at weapon+ItemAttrData (0x13E0) instead of its
            // compiled-in default, which may differ between CS2 builds.
            mem.Write<ushort>(IntPtr.Add(_regenFunc, 0x52), (ushort)EntitySchema.ItemAttrData);

            _regenReady = true;
        }

        // ── Mesh mask ─────────────────────────────────────────────────────────

        // Exact match of friend's SetMeshMask: write dirty-model-data mask first so the
        // renderer uses our value, then hammer m_MeshGroupMask 700× and retry until the
        // read-back confirms the value held. mask = LegacyModel ? 2 : 1.
        private static void SetMeshMask(ProcessMemory mem, IntPtr entity, ulong mask)
        {
            IntPtr sceneNode = mem.Read<IntPtr>(IntPtr.Add(entity, EntitySchema.GameSceneNode));
            if (sceneNode == IntPtr.Zero) return;

            // CSkeletonInstance::m_modelState is an embedded struct (not a pointer) at +0x150.
            IntPtr modelState = IntPtr.Add(sceneNode, 0x150);
            IntPtr maskAddr   = IntPtr.Add(modelState, 0x1C8);  // m_MeshGroupMask

            // Write dirty model data mask — prevents CS2 from resetting our value each tick.
            IntPtr dirtyData = mem.Read<IntPtr>(IntPtr.Add(modelState, 0xD8));
            if (dirtyData != IntPtr.Zero)
                mem.Write<ulong>(IntPtr.Add(dirtyData, 0x10), mask);

            // Retry until the write sticks (friend's OverideMeshMaskNetvar goto loop).
            for (int attempt = 0; attempt < 10; attempt++)
            {
                for (int i = 0; i < 700; i++)
                    mem.Write<ulong>(maskAddr, mask);

                System.Threading.Thread.Sleep(5);

                if (mem.Read<ulong>(maskAddr) == mask)
                    break;
            }
        }

        // ── HUD / viewmodel weapon lookup ─────────────────────────────────────

        // Find the viewmodel entity that corresponds to `weapon` by walking the
        // scene-node children of the C_CS2HudModelArms entity.
        private static IntPtr GetHudWeapon(ProcessMemory mem, IntPtr weapon, IntPtr pawn)
        {
            LastArmsHandle = 0; LastArmsEntity = IntPtr.Zero; LastArmsNode = IntPtr.Zero;
            LastChildrenWalked = 0; LastHudWeaponFound = IntPtr.Zero;

            if (pawn == IntPtr.Zero) return IntPtr.Zero;

            uint armsHandle = mem.Read<uint>(IntPtr.Add(pawn, EntitySchema.HudModelArms));
            LastArmsHandle = armsHandle;
            if (armsHandle == 0 || armsHandle == 0xFFFFFFFF) return IntPtr.Zero;

            IntPtr armsEntity = EntityReader.LookupEntityInternal(mem, armsHandle & 0x7FFF);
            LastArmsEntity = armsEntity;
            if (armsEntity == IntPtr.Zero) return IntPtr.Zero;

            IntPtr armsNode = mem.Read<IntPtr>(IntPtr.Add(armsEntity, EntitySchema.GameSceneNode));
            LastArmsNode = armsNode;
            if (armsNode == IntPtr.Zero) return IntPtr.Zero;

            // Recursively walk the scene-node tree (m_pChild / m_pNextSibling), not just direct
            // children — the weapon's viewmodel node may be nested under an intermediate node.
            IntPtr found = WalkSceneNodeForWeapon(mem, armsNode, weapon, depth: 0);
            LastHudWeaponFound = found;
            return found;
        }

        private static IntPtr WalkSceneNodeForWeapon(ProcessMemory mem, IntPtr node, IntPtr weapon, int depth)
        {
            if (depth > 6) return IntPtr.Zero;   // avoid runaway recursion on bad pointers

            IntPtr child = mem.Read<IntPtr>(IntPtr.Add(node, 0x40));  // m_pChild

            for (int guard = 0; child != IntPtr.Zero && guard < 64; guard++)
            {
                LastChildrenWalked++;

                // m_pOwner gives the entity that owns this scene node.
                IntPtr owner = mem.Read<IntPtr>(IntPtr.Add(child, 0x30));
                if (owner != IntPtr.Zero)
                {
                    // Check if that entity's m_hOwnerEntity points to `weapon`.
                    uint ownerWeaponHandle = mem.Read<uint>(IntPtr.Add(owner, EntitySchema.OwnerEntity));
                    if (ownerWeaponHandle != 0 && ownerWeaponHandle != 0xFFFFFFFF)
                    {
                        IntPtr ownerWeapon = EntityReader.LookupEntityInternal(mem, ownerWeaponHandle & 0x7FFF);
                        if (ownerWeapon == weapon)
                            return owner;
                    }
                }

                // Recurse into this child's own children before moving to the next sibling.
                IntPtr nested = WalkSceneNodeForWeapon(mem, child, weapon, depth + 1);
                if (nested != IntPtr.Zero)
                    return nested;

                child = mem.Read<IntPtr>(IntPtr.Add(child, 0x48));  // m_pNextSibling
            }

            return IntPtr.Zero;
        }

        // ── CEconItemAttribute list ───────────────────────────────────────────

        // Layout of a single CEconItemAttribute (0x48 = 72 bytes):
        //   +0x30  uint16  defIndex   (6 = paint kit, 7 = pattern, 8 = wear)
        //   +0x34  float   value
        //   +0x38  float   initValue
        private const int AttrStride = 0x48;
        private const int AttrCount  = 3;

        // Allocate a block in CS2's address space, write 3 attribute entries (paint,
        // pattern, wear), and point the CPtrGameVector at weapon+ItemAttrData at it.
        // Returns the allocated block so the caller can free it after RegenerateWeaponSkins.
        private static IntPtr WriteAttrList(ProcessMemory mem, IntPtr weapon, int paintKit, int seed, float wear)
        {
            IntPtr attrAddr = IntPtr.Add(weapon, EntitySchema.ItemAttrData);

            // Don't double-allocate if a list is somehow already present.
            if (mem.Read<long>(attrAddr) != 0) return IntPtr.Zero;

            return WriteAttrListAt(mem, attrAddr, paintKit, seed, wear);
        }

        // Shared by weapons (attrAddr = weapon+ItemAttrData) and gloves
        // (attrAddr = pawn+EconGloves+EconItemView_AttrList).
        private static IntPtr WriteAttrListAt(ProcessMemory mem, IntPtr attrAddr, int paintKit, int seed, float wear)
        {
            byte[] buf = new byte[AttrStride * AttrCount];

            void WriteAttr(int idx, ushort def, float val)
            {
                int off = idx * AttrStride;
                // Fields at their struct offsets; rest stays zero (vtable, owner, pad, refundable, setBonus).
                BitConverter.GetBytes(def).CopyTo(buf, off + 0x30);  // defIndex
                BitConverter.GetBytes(val).CopyTo(buf, off + 0x34);  // value
                BitConverter.GetBytes(val).CopyTo(buf, off + 0x38);  // initValue
            }

            WriteAttr(0, 6, paintKit);       // paint kit definition
            WriteAttr(1, 7, (float)seed);    // pattern seed
            WriteAttr(2, 8, wear);           // float wear

            IntPtr block = mem.Allocate(AttrStride * AttrCount);
            if (block == IntPtr.Zero) return IntPtr.Zero;

            mem.WriteBytes(block, buf);

            // Write CPtrGameVector as one 16-byte block — matches friend's single Write<CPtrGameVector>.
            byte[] vec = new byte[16];
            BitConverter.GetBytes((long)AttrCount).CopyTo(vec, 0);   // size
            BitConverter.GetBytes(block.ToInt64()).CopyTo(vec, 8);    // ptr
            mem.WriteBytes(attrAddr, vec);

            return block;
        }

        // Zero the CPtrGameVector so CS2 no longer references the (soon-to-be-freed) block.
        private static void RemoveAttrList(ProcessMemory mem, IntPtr weapon)
        {
            IntPtr attrAddr = IntPtr.Add(weapon, EntitySchema.ItemAttrData);
            if (mem.Read<long>(attrAddr) == 0) return;
            mem.Write<long>(attrAddr,                              0L);
            mem.Write<long>(IntPtr.Add(attrAddr, 8), 0L);
        }

        // ── Weapon name lookup ───────────────────────────────────────────────

        public static string GetWeaponName(int id)
            => id == DefaultKnifeSlot
                ? "Default Knife"
                : Names.TryGetValue(id, out var n) ? n : $"Weapon #{id}";

        public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
        {
            [1]   = "Desert Eagle",
            [2]   = "Dual Berettas",
            [3]   = "Five-SeveN",
            [4]   = "Glock-18",
            [7]   = "AK-47",
            [8]   = "AUG",
            [9]   = "AWP",
            [10]  = "FAMAS",
            [13]  = "Galil AR",
            [14]  = "M249",
            [16]  = "M4A4",
            [17]  = "MAC-10",
            [19]  = "P90",
            [23]  = "MP5-SD",
            [24]  = "UMP-45",
            [25]  = "XM1014",
            [26]  = "PP-Bizon",
            [27]  = "MAG-7",
            [28]  = "Negev",
            [29]  = "Sawed-Off",
            [30]  = "Tec-9",
            [32]  = "P2000",
            [33]  = "MP7",
            [34]  = "MP9",
            [35]  = "Nova",
            [36]  = "P250",
            [38]  = "SCAR-20",
            [39]  = "SG 553",
            [40]  = "SSG 08",
            [41]  = "Knife (CT)",
            [42]  = "Knife (T)",
            [60]  = "M4A1-S",
            [61]  = "USP-S",
            [63]  = "CZ75-Auto",
            [64]  = "R8 Revolver",
            [500] = "Bayonet",
            [505] = "Flip Knife",
            [506] = "Gut Knife",
            [507] = "Karambit",
            [508] = "M9 Bayonet",
            [509] = "Huntsman Knife",
            [512] = "Falchion Knife",
            [514] = "Bowie Knife",
            [515] = "Butterfly Knife",
            [516] = "Shadow Daggers",
            [519] = "Ursus Knife",
            [520] = "Navaja Knife",
            [521] = "Stiletto Knife",
            [522] = "Talon Knife",
            [523] = "Classic Knife",
            [525] = "Paracord Knife",
            [526] = "Survival Knife",
            [527] = "Nomad Knife",
            [528] = "Skeleton Knife",
            [529] = "Kukri Knife",
        };
    }
}
