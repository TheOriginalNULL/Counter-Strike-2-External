using System.Numerics;

namespace CounterStrike2.Entities
{
    public enum GrenadeType { Frag, Smoke }

    public sealed class GrenadeEntity
    {
        public GrenadeType Type     { get; init; }
        public Vector3 Position     { get; init; }  // current world-space origin
        public Vector3 Landing      { get; init; }  // gravity-simulated landing point
    }
}
