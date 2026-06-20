using System.Numerics;

namespace CounterStrike2.Entities
{
    public sealed class BombEntity
    {
        public Vector3 Position      { get; init; }
        public bool    IsTicking     { get; init; }
        public bool    IsDefused     { get; init; }
        public bool    IsBeingDefused{ get; init; }
        public float   TimeRemaining { get; init; }   // seconds until explosion
        public float   DefuseTime    { get; init; }   // seconds until defuse completes
        public int     Site          { get; init; }   // 0 = A, 1 = B
    }
}
