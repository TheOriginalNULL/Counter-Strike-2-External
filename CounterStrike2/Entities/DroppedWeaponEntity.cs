using System.Numerics;

namespace CounterStrike2.Entities
{
    public sealed class DroppedWeaponEntity
    {
        public Vector3 Position   { get; init; }
        public string  WeaponName { get; init; } = string.Empty;
        public ushort  WeaponId   { get; init; }
    }
}
