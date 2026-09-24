using NUnit.Framework;
using UnityEngine;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>The shared hexagon mesh every board cell renders.</summary>
    public class HexMeshTests
    {
        [Test]
        public void MeshIsAFanOfSixTriangles()
        {
            var m = HexMesh.Create();
            try
            {
                Assert.AreEqual(7, m.vertexCount, "centre plus six corners");
                Assert.AreEqual(18, m.triangles.Length, "six triangles");
            }
            finally { Object.DestroyImmediate(m); }
        }

        /// <summary>
        /// Flat-top: a vertex sits at 0 degrees, so the shape is widest along X and the
        /// extreme points are left and right, not top and bottom. A pointy-top mesh would
        /// leave gaps against the browser game's column spacing.
        /// </summary>
        [Test]
        public void MeshIsFlatTopped()
        {
            var m = HexMesh.Create(1f);
            try
            {
                var b = m.bounds;
                Assert.AreEqual(2f, b.size.x, 1e-4f, "width should be 2 radii");
                Assert.AreEqual(Mathf.Sqrt(3f), b.size.y, 1e-4f, "height should be sqrt(3) radii");
                Assert.Greater(b.size.x, b.size.y, "flat-top hexes are wider than they are tall");
            }
            finally { Object.DestroyImmediate(m); }
        }

        /// <summary>
        /// The front face must point toward −Z, the side the board camera sits on. If this
        /// inverts, every cell is backface-culled: the Game view renders empty while the
        /// Scene view still looks fine, which is a genuinely confusing failure. Pin it.
        /// </summary>
        [Test]
        public void FrontFacePointsTowardNegativeZ()
        {
            var m = HexMesh.Create();
            try
            {
                foreach (var n in m.normals)
                    Assert.Less(n.z, -0.5f, "every normal should point toward -Z");
            }
            finally { Object.DestroyImmediate(m); }
        }

        /// <summary>Winding is clockwise seen from the front, per Unity's convention.</summary>
        [Test]
        public void TrianglesWindClockwiseFromTheFront()
        {
            var m = HexMesh.Create();
            try
            {
                var v = m.vertices;
                var tris = m.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                    // Signed area in XY; negative means clockwise viewed from +Z, which is
                    // clockwise-from-the-front when the front is -Z.
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    Assert.Less(cross, 0f, $"triangle {i / 3} winds the wrong way");
                }
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void RadiusScalesTheMesh()
        {
            var m = HexMesh.Create(2.5f);
            try { Assert.AreEqual(5f, m.bounds.size.x, 1e-4f); }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void EveryCornerSitsOnTheCircumcircle()
        {
            var m = HexMesh.Create(1f);
            try
            {
                var v = m.vertices;
                for (int i = 1; i < v.Length; i++)
                    Assert.AreEqual(1f, new Vector2(v[i].x, v[i].y).magnitude, 1e-4f,
                        $"corner {i} should be one radius from the centre");
            }
            finally { Object.DestroyImmediate(m); }
        }
    }

    /// <summary>
    /// The UI preview quad, held to the same winding invariant as the hex. This exists
    /// because the quad was first written with the winding inverted — the same mistake the
    /// hex mesh had already made — and the bands rendered invisibly as a result.
    /// </summary>
    public class QuadMeshTests
    {
        [Test]
        public void QuadIsAUnitSquare()
        {
            var m = QuadMesh.Create();
            try
            {
                Assert.AreEqual(4, m.vertexCount);
                Assert.AreEqual(6, m.triangles.Length, "two triangles");
                Assert.AreEqual(1f, m.bounds.size.x, 1e-4f);
                Assert.AreEqual(1f, m.bounds.size.y, 1e-4f);
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void QuadFrontFacePointsTowardNegativeZ()
        {
            var m = QuadMesh.Create();
            try
            {
                foreach (var n in m.normals)
                    Assert.Less(n.z, -0.5f, "every normal should point toward -Z");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [Test]
        public void QuadTrianglesWindClockwiseFromTheFront()
        {
            var m = QuadMesh.Create();
            try
            {
                var v = m.vertices;
                var tris = m.triangles;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = v[tris[i]], b = v[tris[i + 1]], c = v[tris[i + 2]];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    Assert.Less(cross, 0f, $"triangle {i / 3} winds the wrong way");
                }
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
