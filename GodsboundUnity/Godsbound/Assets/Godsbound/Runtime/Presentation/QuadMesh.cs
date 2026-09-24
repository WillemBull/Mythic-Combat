using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>A unit quad in the XY plane, front face toward −Z.</summary>
    /// <remarks>
    /// Its own type purely so the winding is covered by tests. The hex mesh's winding was
    /// inverted once (backface-culling the whole board), and then the UI preview's quad was
    /// written with the same mistake despite the rule being documented — a comment was not
    /// enough. Both meshes now assert the same invariant: front faces wind CLOCKWISE as seen
    /// from the front, and the front is the −Z side the board camera sits on.
    /// </remarks>
    public static class QuadMesh
    {
        /// <summary>A 1x1 quad centred on the origin, facing −Z.</summary>
        public static Mesh Create()
        {
            var m = new Mesh { name = "GodsboundQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f)
            };
            m.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
