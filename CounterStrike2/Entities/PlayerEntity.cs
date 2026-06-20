using System;
using System.Numerics;

namespace CounterStrike2.Entities
{
    /// <summary>World-space positions of the joints we draw for skeleton ESP.</summary>
    public struct BoneData
    {
        public Vector3 Head, Neck, Chest, Waist;
        public Vector3 LShoulder, LElbow, LHand;
        public Vector3 RShoulder, RElbow, RHand;
        public Vector3 LHip, LKnee, LFoot;
        public Vector3 RHip, RKnee, RFoot;
        public bool    Valid;   // false if the bone array pointer was null

        /// <summary>All bone positions 0..BoneDebugCount-1 — only populated when Config.BoneDebug is on.</summary>
        public System.Numerics.Vector3[]? DebugBones;
    }

    /// <summary>Immutable snapshot of one player, captured from game memory each tick.</summary>
    public sealed class PlayerEntity
    {
        public bool     IsValid  { get; init; }
        public bool     IsAlive  { get; init; }
        public bool     IsLocal  { get; init; }   // true for our own pawn
        public int      Health   { get; init; }
        public int      Team     { get; init; }   // 2 = T, 3 = CT
        public string   Name     { get; init; } = string.Empty;

        /// <summary>World-space foot position (origin; Z = floor level).</summary>
        public Vector3  Origin   { get; init; }

        /// <summary>CS2 item definition index of the active weapon (0 = unknown).</summary>
        public ushort   WeaponId { get; init; }

        /// <summary>True when the player's weapon inventory contains the C4 (item def 49).</summary>
        public bool     HasC4    { get; init; }

        /// <summary>Skeleton joint positions (only valid when EspSkeleton is enabled).</summary>
        public BoneData Bones    { get; init; }

        internal IntPtr PawnPtr  { get; init; }
    }
}
