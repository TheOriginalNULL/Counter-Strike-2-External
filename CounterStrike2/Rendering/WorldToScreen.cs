using System;
using System.Numerics;
using System.Windows;

namespace CounterStrike2.Rendering
{
    /// <summary>
    /// Projects a world-space Vector3 onto the 2D screen using CS2's view-projection matrix.
    ///
    /// CS2 stores the matrix as 16 contiguous floats (row-major):
    ///   row 0 = M11..M14, row 1 = M21..M24, row 2 = M31..M34, row 3 = M41..M44
    ///
    ///   clipX = row0 · (x,y,z,1)  →  M11*x + M12*y + M13*z + M14
    ///   clipY = row1 · (x,y,z,1)  →  M21*x + M22*y + M23*z + M24
    ///   clipW = row3 · (x,y,z,1)  →  M41*x + M42*y + M43*z + M44
    ///
    ///   screenX = (clipX/clipW + 1) / 2 * width
    ///   screenY = (1 - clipY/clipW) / 2 * height   ← Y axis flipped (NDC up, screen down)
    /// </summary>
    public static class WorldToScreen
    {
        public static bool Project(
            Vector3 world,
            Matrix4x4 m,
            float screenW, float screenH,
            out Point screen)
        {
            // dwViewMatrix is a 4×4 float array stored row-major in memory.
            // .NET Matrix4x4 reads it identically (M{row}{col}, row-major).
            // Standard CS2 projection:
            //   clipW = row3 · world  →  M41*x + M42*y + M43*z + M44
            //   clipX = row0 · world  →  M11*x + M12*y + M13*z + M14
            //   clipY = row1 · world  →  M21*x + M22*y + M23*z + M24
            float clipW = m.M41 * world.X + m.M42 * world.Y + m.M43 * world.Z + m.M44;

            if (clipW <= 0.001f)          // behind the camera
            {
                screen = default;
                return false;
            }

            float clipX = m.M11 * world.X + m.M12 * world.Y + m.M13 * world.Z + m.M14;
            float clipY = m.M21 * world.X + m.M22 * world.Y + m.M23 * world.Z + m.M24;

            float ndcX = clipX / clipW;
            float ndcY = clipY / clipW;

            screen = new Point(
                (ndcX + 1f) * 0.5f * screenW,
                (1f - ndcY) * 0.5f * screenH);

            return true;
        }

        /// <summary>
        /// Extract the camera's world-space facing yaw (degrees) from the view-projection
        /// matrix. The screen-center viewing ray is exactly the set of world points where
        /// clipX=0 AND clipY=0 simultaneously (true at every depth along that ray) — so its
        /// direction is the null vector of those two linear constraints, found directly via
        /// the cross product of the matrix's clipX and clipY row vectors. No inversion, no
        /// assumption about near/far NDC depth convention — just the same two rows
        /// WorldToScreen.Project already uses successfully every frame.
        /// </summary>
        public static float ExtractYaw(Matrix4x4 vm)
        {
            var rowX = new Vector3(vm.M11, vm.M12, vm.M13);
            var rowY = new Vector3(vm.M21, vm.M22, vm.M23);

            // Cross order picks one of two opposite perpendiculars — this order was confirmed
            // (2026) to point backward (front/back swapped on the radar), so it's reversed here.
            Vector3 forward = Vector3.Cross(rowY, rowX);
            if (forward.LengthSquared() < 1e-9f)
                return 0f;

            return MathF.Atan2(forward.Y, forward.X) * (180f / MathF.PI);
        }
    }
}
