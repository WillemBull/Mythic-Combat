using NUnit.Framework;
using Godsbound.Core;

namespace Godsbound.Tests
{
    /// <summary>Board shape and row ownership, checked against the browser reference.</summary>
    public class BoardTests
    {
        [Test]
        public void Dimensions_MatchReference()
        {
            var r = HexReference.Data;
            Assert.AreEqual(r.cols, Board.Cols, "column count");
            Assert.AreEqual(r.rows, Board.Rows, "row count");
            Assert.AreEqual(r.aiRows, Board.AiRows, "AI half depth");
            Assert.AreEqual(r.neutralRows, Board.NeutralRows, "no-man's land depth");
            Assert.AreEqual(r.playerRows, Board.PlayerRows, "player half depth");
            Assert.AreEqual(r.playerRow0, Board.PlayerRow0, "player half start row");
            Assert.AreEqual(r.cells.Length, Board.CellCount, "total cells");
        }

        /// <summary>
        /// Guards the halving bug the browser game's HANDOFF calls out: with a neutral
        /// band, Rows/2 is neither the half depth nor the player's start row.
        /// </summary>
        [Test]
        public void HalfDepth_IsNotHalfTheBoard()
        {
            Assert.AreNotEqual(Board.Rows / 2, Board.PlayerRow0,
                "PlayerRow0 must account for the neutral band, not be Rows/2");
            Assert.AreEqual(Board.AiRows + Board.NeutralRows + Board.PlayerRows, Board.Rows);
        }

        [Test]
        public void HexCapacity_MatchesReference()
        {
            Assert.AreEqual(HexReference.Data.hexCapacity, Board.HexCapacity);
        }

        [Test]
        public void InBounds_RejectsEveryOffBoardCell()
        {
            Assert.IsFalse(Board.InBounds(-1, 0));
            Assert.IsFalse(Board.InBounds(0, -1));
            Assert.IsFalse(Board.InBounds(Board.Cols, 0));
            Assert.IsFalse(Board.InBounds(0, Board.Rows));
            Assert.IsTrue(Board.InBounds(0, 0));
            Assert.IsTrue(Board.InBounds(Board.Cols - 1, Board.Rows - 1));
        }

        [Test]
        public void RowOwnership_MatchesReferenceForEveryCell()
        {
            foreach (var cell in HexReference.Data.cells)
            {
                Assert.AreEqual(cell.neutralRow, Board.IsNeutralRow(cell.r),
                    $"neutral-row flag at ({cell.c},{cell.r})");

                var expected = cell.side == -1 ? BoardSide.Neutral
                             : cell.side == 1 ? BoardSide.Ai
                             : BoardSide.Player;
                Assert.AreEqual(expected, Board.SideForRow(cell.r),
                    $"row ownership at ({cell.c},{cell.r})");
            }
        }

        [Test]
        public void Index_IsUniqueAndRowMajor()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            int expected = 0;
            foreach (var h in Board.AllCells())
            {
                Assert.AreEqual(expected, Board.Index(h), $"index order at {h}");
                Assert.IsTrue(seen.Add(Board.Index(h)), $"duplicate index at {h}");
                expected++;
            }
            Assert.AreEqual(Board.CellCount, seen.Count);
        }
    }
}
