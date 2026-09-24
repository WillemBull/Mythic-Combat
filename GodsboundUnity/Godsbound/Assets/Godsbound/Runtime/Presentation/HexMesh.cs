using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>Builds the flat-top hexagon mesh every board cell shares.</summary>
    /// <remarks>
    /// Flat-top means a vertex at 0°, i.e. points at left and right and flat edges top and
    /// bottom — the same orientation as the browser game's grid, where columns step by
    /// <c>1.5 * HEX</c> horizontally and odd columns drop half a row. A pointy-top mesh
    /// would tile with visible gaps against these centres.
    /// </remarks>
    public static class HexMesh
    {
        /// <summary>
        /// A flat-top hexagon of circumradius <paramref name="radius"/>, centred on the
        /// origin, in the XY plane, wound so its front face points toward −Z.
        /// </summary>
        /// <remarks>
        /// The winding matters and is easy to get backwards. Unity treats a triangle as
        /// front-facing when its vertices run CLOCKWISE as seen from the front. The board
        /// camera sits at negative Z looking toward +Z (the standard Unity 2D setup), so
        /// the front is the −Z side and the fan must run centre → corner(i+1) → corner(i).
        /// Getting this inverted culls every cell and the Game view renders empty while the
        /// Scene view — whose camera can be on the other side — still looks correct. That
        /// is exactly what happened during U2.
        /// </remarks>
        public static Mesh Create(float radius = 1f)
        {
            var mesh = new Mesh { name = "GodsboundHex" };

            // Centre plus six corners at 0, 60, ..., 300 degrees.
            var verts = new Vector3[7];
            var uvs = new Vector2[7];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i);
                float x = Mathf.Cos(a), y = Mathf.Sin(a);
                verts[i + 1] = new Vector3(x * radius, y * radius, 0f);
                uvs[i + 1] = new Vector2(0.5f + x * 0.5f, 0.5f + y * 0.5f);
            }

            var tris = new int[18];
            for (int i = 0; i < 6; i++)
            {
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = (i + 1) % 6 + 1;
                tris[i * 3 + 2] = i + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
