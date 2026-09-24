using NUnit.Framework;
using Godsbound.Core;

namespace Godsbound.Tests
{
    /// <summary>Hex geometry and the screen-space tilt, against the browser reference.</summary>
    public class HexLayoutTests
    {
        private static HexLayout ReferenceLayout()
        {
            var r = HexReference.Data;
            return new HexLayout(r.geomHex, r.geomOx, r.geomOy);
        }

        [Test]
        public void TiltSquash_MatchesReference()
        {
            Assert.AreEqual(HexReference.Data.tiltSquash, HexLayout.TiltSquash, 1e-6f);
        }

        [Test]
        public void HexCenters_MatchReferenceForEveryCell()
        {
            var layout = ReferenceLayout();
            var centers = HexReference.Data.centers;

            Assert.AreEqual(Board.CellCount * 4, centers.Length,
                "fixture centres should hold four floats per cell");

            for (int i = 0; i < centers.Length; i += 4)
            {
                int c = (int)centers[i];
                int r = (int)centers[i + 1];
                var actual = layout.Center(c, r);
                Assert.AreEqual(centers[i + 2], actual.X, 1e-3f, $"centre x at ({c},{r})");
                Assert.AreEqual(centers[i + 3], actual.Y, 1e-3f, $"centre y at ({c},{r})");
            }
        }

        /// <summary>
        /// The tilt must be presentation-only. If the simulation ever picks up the
        /// squash, units move at different speeds depending on heading.
        /// </summary>
        [Test]
        public void ToScreen_ThenFromScreen_RoundTrips()
        {
            var layout = ReferenceLayout();
            foreach (var h in Board.AllCells())
            {
                var logical = layout.Center(h);
                var round = layout.FromScreen(layout.ToScreen(logical));
                Assert.AreEqual(logical.X, round.X, 1e-3f, $"x round trip at {h}");
                Assert.AreEqual(logical.Y, round.Y, 1e-3f, $"y round trip at {h}");
            }
        }

        [Test]
        public void ToScreen_CompressesVerticallyAwayFromThePivot()
        {
            var layout = ReferenceLayout();
            float pivot = layout.BoardPivotY();

            var atPivot = layout.ToScreen(new BoardPoint(100f, pivot));
            Assert.AreEqual(pivot, atPivot.Y, 1e-3f, "the pivot row must not move");

            var below = layout.ToScreen(new BoardPoint(100f, pivot + 100f));
            Assert.AreEqual(pivot + 80f, below.Y, 1e-3f, "0.8 squash below the pivot");

            var above = layout.ToScreen(new BoardPoint(100f, pivot - 100f));
            Assert.AreEqual(pivot - 80f, above.Y, 1e-3f, "0.8 squash above the pivot");

            Assert.AreEqual(100f, below.X, 1e-6f, "the tilt must never touch x");
        }

        /// <summary>
        /// Tapping a hex's own centre must select that hex. Callers pass raw pointer
        /// coordinates, so the test feeds screen space, not logical space.
        /// </summary>
        [Test]
        public void PixelToHex_ResolvesEveryHexCentre()
        {
            var layout = ReferenceLayout();
            foreach (var h in Board.AllCells())
            {
                var screen = layout.ToScreen(layout.Center(h));
                var hit = layout.PixelToHex(screen.X, screen.Y);
                Assert.IsTrue(hit.HasValue, $"tap on the centre of {h} found nothing");
                Assert.AreEqual(h, hit.Value, $"tap on the centre of {h} resolved elsewhere");
            }
        }

        [Test]
        public void PixelToHex_ReturnsNullWellOffTheBoard()
        {
            var layout = ReferenceLayout();
            Assert.IsNull(layout.PixelToHex(-10000f, -10000f));
            Assert.IsNull(layout.PixelToHex(10000f, 10000f));
        }
    }
}
