using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>
    /// A flat-top hexagon OUTLINE — the ring between two concentric hexagons — so the board can be
    /// a grid drawn over the arena instead of a mosaic of coloured cells (U43).
    /// </summary>
    /// <remarks>
    /// The browser's board is a photograph with hex outlines on it; its own comment at the light
    /// stroke says the hexes are "otherwise fully transparent". Unity filled every cell with a
    /// terrain colour, which is why a backdrop behind them would have been invisible.
    /// Winding follows <see cref="HexMesh"/>: front is −Z, triangles run clockwise seen from there.
    /// </remarks>
    public static class HexRingMesh
    {
        /// <summary>
        /// A ring of the given <paramref name="thickness"/> drawn INSIDE <paramref name="radius"/>,
        /// so a thicker line grows inward and the hexes stay tiled edge to edge.
        /// </summary>
        public static Mesh Create(float radius = 1f, float thickness = 0.06f)
        {
            var mesh = new Mesh { name = "GodsboundHexRing" };
            float inner = Mathf.Max(0f, radius - thickness);

            var verts = new Vector3[12];
            var uvs = new Vector2[12];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Deg2Rad * (60f * i);
                float x = Mathf.Cos(a), y = Mathf.Sin(a);
                verts[i] = new Vector3(x * radius, y * radius, 0f);          // outer 0..5
                verts[i + 6] = new Vector3(x * inner, y * inner, 0f);        // inner 6..11
                uvs[i] = new Vector2(0.5f + x * 0.5f, 0.5f + y * 0.5f);
                uvs[i + 6] = uvs[i];
            }

            // Two triangles per side, wound clockwise from the −Z side like HexMesh's fan.
            var tris = new int[36];
            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                int o0 = i, o1 = next, i0 = i + 6, i1 = next + 6;
                int t = i * 6;
                tris[t] = o0; tris[t + 1] = i1; tris[t + 2] = o1;
                tris[t + 3] = o0; tris[t + 4] = i0; tris[t + 5] = i1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
