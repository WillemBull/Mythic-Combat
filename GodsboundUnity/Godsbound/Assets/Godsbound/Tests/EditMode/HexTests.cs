using System.Linq;
using NUnit.Framework;
using Godsbound.Core;

namespace Godsbound.Tests
{
    /// <summary>
    /// Hex adjacency and distance, checked exhaustively against the browser reference.
    /// </summary>
    public class HexTests
    {
        /// <summary>
        /// Every cell's neighbour list, in order. Order matters: several browser-game
        /// call sites take the *first* legal neighbour, so a reordered ring changes
        /// behaviour even though the set is identical.
        /// </summary>
        [Test]
        public void Neighbors_MatchReferenceForEveryCell_InOrder()
        {
            foreach (var cell in HexReference.Data.cells)
            {
                var actual = new Hex(cell.c, cell.r).Neighbors().ToList();

                Assert.AreEqual(cell.neighbors.Length, actual.Count,
                    $"neighbour count at ({cell.c},{cell.r})");

                for (int i = 0; i < cell.neighbors.Length; i++)
                    Assert.AreEqual(new Hex(cell.neighbors[i].c, cell.neighbors[i].r), actual[i],
                        $"neighbour {i} of ({cell.c},{cell.r})");
            }
        }

        /// <summary>
        /// The odd-q trap: even and odd columns use different offsets. If a port gets
        /// this wrong it usually still passes on column 0.
        /// </summary>
        [Test]
        public void Neighbors_DifferBetweenEvenAndOddColumns()
        {
            var even = new Hex(2, 5).NeighborsUnclamped().ToList();
            var odd = new Hex(3, 5).NeighborsUnclamped().ToList();
            var evenShifted = even.Select(h => new Hex(h.C + 1, h.R)).ToList();
            Assert.AreNotEqual(evenShifted, odd,
                "odd columns must not be a plain horizontal shift of even columns");
        }

        [Test]
        public void Adjacency_IsSymmetric()
        {
            foreach (var a in Board.AllCells())
                foreach (var b in a.Neighbors())
                    Assert.IsTrue(b.Neighbors().Contains(a),
                        $"{a} lists {b} as a neighbour but not the reverse");
        }

        [Test]
        public void Distance_MatchesReferenceForEveryOrderedPair()
        {
            var data = HexReference.Data;
            int n = data.cells.Length;

            foreach (var a in Board.AllCells())
                foreach (var b in Board.AllCells())
                {
                    int expected = data.distMatrix[Board.Index(a) * n + Board.Index(b)];
                    Assert.AreEqual(expected, Hex.Distance(a, b), $"distance {a} -> {b}");
                }
        }

        [Test]
        public void Distance_IsZeroToSelfAndSymmetric()
        {
            foreach (var a in Board.AllCells())
            {
                Assert.AreEqual(0, Hex.Distance(a, a), $"distance {a} -> itself");
                foreach (var b in Board.AllCells())
                    Assert.AreEqual(Hex.Distance(a, b), Hex.Distance(b, a),
                        $"distance is asymmetric between {a} and {b}");
            }
        }

        [Test]
        public void Distance_ToEveryNeighbourIsExactlyOne()
        {
            foreach (var a in Board.AllCells())
                foreach (var b in a.Neighbors())
                {
                    Assert.AreEqual(1, Hex.Distance(a, b), $"{a} -> {b} should be one step");
                    Assert.IsTrue(a.IsAdjacentTo(b));
                }
        }

        [Test]
        public void CubeCoordinates_SumToZero()
        {
            foreach (var h in Board.AllCells())
            {
                var (x, y, z) = h.ToCube();
                Assert.AreEqual(0, x + y + z, $"cube coords for {h} must sum to zero");
            }
        }

        [Test]
        public void Equality_AndHashing_Behave()
        {
            Assert.AreEqual(new Hex(3, 4), new Hex(3, 4));
            Assert.AreEqual(new Hex(3, 4).GetHashCode(), new Hex(3, 4).GetHashCode());
            Assert.AreNotEqual(new Hex(3, 4), new Hex(4, 3));
            Assert.IsTrue(new Hex(1, 1) == new Hex(1, 1));
            Assert.IsTrue(new Hex(1, 1) != new Hex(1, 2));
        }
    }
}
