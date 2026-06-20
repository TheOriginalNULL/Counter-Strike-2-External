namespace CounterStrike2.Entities
{
    /// <summary>
    /// Struct-field offsets sourced from cs2-dumper (a2x), dumped 2026-06-17.
    /// When CS2 updates, re-run the dumper and update these constants.
    /// https://github.com/a2x/cs2-dumper
    /// </summary>
    internal static class EntitySchema
    {
        // ---- C_BaseEntity ----
        public const int GameSceneNode  = 0x330;  // CGameSceneNode*

        // ---- C_BasePlayerPawn ----
        public const int Health         = 0x34C;  // int32
        public const int LifeState      = 0x354;  // uint8  — 0=alive
        public const int TeamNum        = 0x3EB;  // uint8  — T=2, CT=3

        // ---- CGameSceneNode ----
        public const int AbsOrigin      = 0xC8;   // Vector3 (3 × float32)

        // ---- CCSPlayerController ----
        public const int PlayerPawnHandle = 0x90C;  // CHandle<C_CSPlayerPawn>
        public const int SanitizedName    = 0x860;  // CUtlString → first 8 bytes = char*

        // ---- CCSPlayerController (damage/kill tracking, for hit/kill indicators+sounds) ----
        // controller + ActionTrackingServices → CCSPlayerController_ActionTrackingServices*
        public const int ActionTrackingServices = 0x818;
        // svc + TotalRoundDamageDealt → float32, cumulative damage dealt this round, resets each round.
        public const int TotalRoundDamageDealt  = 0x130;
        // svc + NumRoundKills → int32, kills this round. Used instead of the flat
        // CCSPlayerController::m_nKillCount offset (0x941) — that one read a plausible-looking
        // but non-incrementing value on a live build, while this rides the SAME services
        // pointer chain already confirmed live and correct via working hit detection.
        public const int NumRoundKills          = 0x128;

        // ---- C_BasePlayerPawn (weapon chain) ----
        // pawn + WeaponServices → CPlayer_WeaponServices*
        public const int WeaponServices   = 0x11E0;

        // ---- CPlayer_WeaponServices ----
        // svc + ActiveWeapon → CHandle<C_BasePlayerWeapon> (uint32)
        public const int ActiveWeapon     = 0x60;

        // m_hMyWeapons is a CNetworkUtlVectorBase at svc+0x48:
        //   +0x00 = uint64 count of weapon handles
        //   +0x08 = uintptr_t pointer to uint32[] handle array
        public const int WeaponCount      = 0x48;  // CPlayer_WeaponServices::m_hMyWeapons size
        public const int WeaponEntries    = 0x50;  // CPlayer_WeaponServices::m_hMyWeapons ptr

        // ---- C_EconEntity → C_AttributeContainer → C_EconItemView ----
        // weapon + 0x1180 (m_AttributeManager) + 0x50 (m_Item) + 0x1BA (m_iItemDefinitionIndex)
        public const int ItemDefIndex     = 0x138A;  // uint16

        // ---- Skeleton / bone cache ----
        // CSkeletonInstance::m_modelState (embedded CModelState) = 0x150
        // Within CModelState, at +0x80 (not in networked schema) = bone array ptr
        // Combined: sceneNode + 0x1D0 → IntPtr → bone array
        // Each bone entry = 32 bytes; position (Vector3) at byte 0.
        public const int BoneArrayPtr     = 0x1D0;   // offset from sceneNode → IntPtr
        public const int BoneStride       = 32;       // bytes per bone

        // CS2 player model bone indices — verified from bone debug screenshot (2026-06-18).
        // Bone 0 = entity root at ground level (NOT waist). Bone 1 = actual pelvis/waist.
        public const int Bone_Head        = 7;
        public const int Bone_Neck        = 6;
        public const int Bone_Chest       = 4;
        public const int Bone_Waist       = 1;   // pelvis — bone 0 is entity root at ground
        public const int Bone_LShoulder   = 8;
        public const int Bone_LElbow      = 9;
        public const int Bone_LHand       = 10;
        public const int Bone_RShoulder   = 13;
        public const int Bone_RElbow      = 14;
        public const int Bone_RHand       = 15;
        public const int Bone_LHip        = 20;  // confirmed from debug dots
        public const int Bone_LKnee       = 21;
        public const int Bone_LFoot       = 22;
        public const int Bone_RHip        = 17;  // confirmed from debug dots
        public const int Bone_RKnee       = 18;
        public const int Bone_RFoot       = 19;

        // Number of bones to read in one batch (covers all indices above).
        public const int BoneBatchCount   = 28;      // indices 0-27
        public const int BoneBatchBytes   = BoneBatchCount * BoneStride;  // 896

        // Debug mode: read more bones to identify unknown indices visually.
        public const int BoneDebugCount   = 48;
        public const int BoneDebugBytes   = BoneDebugCount * BoneStride;  // 1536

        // ---- C_BaseCSGrenadeProjectile (all offsets absolute from entity ptr) ----
        // ---- C_PlantedC4 (all offsets absolute from entity ptr) ----
        public const int BombTicking      = 0x1160;  // m_bBombTicking     byte
        public const int BombSite         = 0x1164;  // m_nBombSite        int32 (0=A,1=B)
        public const int BombBlowTime     = 0x1190;  // m_flC4Blow         float (abs game time)
        public const int BombTimerLength  = 0x1198;  // m_flTimerLength    float (total seconds)
        public const int BombBeingDefused = 0x119C;  // m_bBeingDefused    byte
        public const int BombDefuseLength = 0x11AC;  // m_flDefuseLength   float
        public const int BombDefuseEnd    = 0x11B0;  // m_flDefuseCountDown float (abs game time)
        public const int BombDefused      = 0x11B4;  // m_bBombDefused     byte

        // ---- CGlobalVarsBase (via Offsets.GlobalVars → deref → struct) ----
        public const int GlobalVarsCurTime = 0x34;   // m_flCurTime float

        // ---- Spectator chain ----
        // controller + ObserverPawnHandle → C_CSObserverPawn handle
        // observer pawn + ObserverServices → CPlayer_ObserverServices*
        // svc + ObserverTarget → watched entity CHandle (pawn or controller)
        public const int ObserverPawnHandle = 0x910;  // CCSPlayerController::m_hObserverPawn
        public const int ObserverServices   = 0x11F8; // C_BasePlayerPawn::m_pObserverServices ptr
        public const int ObserverTarget     = 0x4C;   // CPlayer_ObserverServices::m_hObserverTarget

        // ---- Skin changer — C_EconEntity (weapon base), offsets from dumper 2026-06-17 ----
        // Fallback values (used when m_iItemIDHigh == -1 to bypass server-assigned item):
        public const int FallbackPaintKit      = 0x1658;  // m_nFallbackPaintKit uint32
        public const int FallbackSeed          = 0x165C;  // m_nFallbackSeed     uint32
        public const int FallbackWear          = 0x1660;  // m_flFallbackWear    float
        public const int FallbackStatTrak      = 0x1664;  // m_nFallbackStatTrak int32 (-1 = no stattrak)
        // C_EconItemView starts at weapon + m_AttributeManager(0x1180) + m_Item(0x50) = 0x11D0
        public const int ItemID                = 0x1398;  // m_iItemID      uint64 (set MaxValue → use fallback)
        public const int ItemIDHigh            = 0x13A0;  // m_iItemIDHigh  int32  (set -1 → use fallback)
        public const int ItemIDLow             = 0x13A4;  // m_iItemIDLow   int32  (set -1 → use fallback)
        public const int ItemAccountID         = 0x13A8;  // m_iAccountID   uint32 (0x11D0 + 0x1D8)
        public const int EntityQuality         = 0x138C;  // m_iEntityQuality int32 (4=unique, 9=stattrak)
        // Flags cleared to force CS2 to re-read paint kit after we write it:
        public const int AttributesInitialized = 0x1178;  // m_bAttributesInitialized bool on C_EconEntity
        public const int VisualsDataSet        = 0x18B9;  // m_bVisualsDataSet        bool on C_CSWeaponBase

        // ---- Skin changer – attribute list and HUD weapon ----
        // CPtrGameVector { uint64 size; uintptr_t ptr; } at weapon+0x13E0
        //   = m_AttributeManager(0x1180) + m_Item(0x50) + m_AttributeList(0x208) + m_Attributes(0x08)
        // Also the value patched into RegenerateWeaponSkins at +0x52 so the function
        // reads our allocated CEconItemAttribute list.
        public const int ItemAttrData     = 0x13E0;

        // C_CSPlayerPawn::m_hHudModelArms — handle to the HUD/viewmodel arms entity.
        // Traverse its scene-node children to find the per-weapon viewmodel.
        public const int HudModelArms     = 0x1B58;

        // ---- Glove changer — C_CSPlayerPawn fields ----
        // m_EconGloves is an EMBEDDED C_EconItemView (not a pointer) directly on the pawn —
        // gloves are not a weapon entity, so there's no C_AttributeContainer/m_Item wrapper.
        public const int EconGloves           = 0x1890;  // C_EconItemView
        public const int NeedToReApplyGloves   = 0x188D;  // bool

        // ---- C_EconItemView field offsets, relative to the START of the view ----
        // (weapon's C_EconItemView begins at weapon+0x11D0; these match ItemDefIndex/ItemIDHigh/
        // etc. above once you subtract 0x11D0, confirmed against the friend's raw dumper offsets.)
        public const int EconItemView_ItemDefIndex  = 0x1BA;  // uint16
        public const int EconItemView_EntityQuality = 0x1BC;  // int32
        public const int EconItemView_ItemIDHigh    = 0x1D0;  // uint32 (set -1 → use fallback attrs)
        public const int EconItemView_ItemIDLow     = 0x1D4;  // uint32
        public const int EconItemView_AccountID     = 0x1D8;  // uint32
        public const int EconItemView_Initialized   = 0x1E8;  // bool
        public const int EconItemView_AttrList      = 0x210;  // CPtrGameVector (m_AttributeList+m_Attributes)

        // ---- C_BaseEntity (shared by all entity types) ----
        // m_hOwnerEntity: 0xFFFFFFFF = no owner (weapon is on the ground).
        public const int OwnerEntity        = 0x520;   // m_hOwnerEntity CHandle uint32

        // m_vecAbsVelocity lives on C_BaseEntity (1020 = 0x3FC).
        public const int GrenadeAbsVelocity = 0x3FC;   // current velocity Vector3
        // Fields specific to C_BaseCSGrenadeProjectile:
        public const int GrenadeInitialPos  = 0x11A0;  // m_vInitialPosition  Vector3
        public const int GrenadeInitialVel  = 0x11AC;  // m_vInitialVelocity  Vector3
        public const int GrenadeNBounces    = 0x11B8;  // m_nBounces          int32
        // Fields on C_SmokeGrenadeProjectile (inherit chain only, absent on other types):
        public const int GrenadeSmokeTick   = 0x1250;  // m_nSmokeEffectTickBegin int32
        public const int GrenadeSmokeBit    = 0x1254;  // m_bDidSmokeEffect   byte
    }
}
