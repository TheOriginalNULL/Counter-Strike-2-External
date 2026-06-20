using System.Numerics;

namespace CounterStrike2.Entities
{
    /// <summary>
    /// Pure gravity simulation — no BSP collision, so wall/ceiling bounces are ignored.
    /// Accurate for open-air throws; the landing circle drifts for throws near geometry.
    /// </summary>
    internal static class GrenadePredictor
    {
        private const float Gravity  = 800f;       // CS2 Source engine gravity (units/s²)
        private const float Dt       = 1f / 64f;   // 64-tick simulation step
        private const int   MaxSteps = 384;        // 6-second cap

        internal static Vector3 Simulate(Vector3 pos, Vector3 vel)
        {
            float startZ = pos.Z;

            for (int i = 0; i < MaxSteps; i++)
            {
                vel.Z -= Gravity * Dt;
                pos   += vel * Dt;

                // Stop once the grenade is clearly descending and has dropped below
                // its snapshot height — best approximation of "it has landed."
                if (vel.Z < -200f && pos.Z < startZ - 5f)
                    break;
            }

            return pos;
        }
    }
}
