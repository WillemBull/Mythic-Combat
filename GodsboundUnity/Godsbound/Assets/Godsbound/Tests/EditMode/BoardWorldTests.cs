using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>Logical board space to Unity world space, and back.</summary>
    public class BoardWorldTests
    {
        private static HexLayout Layout() => BoardWorld.UnitLayout();

        [Test]
        public void EveryCellRoundTripsThroughWorldSpace()
        {
            var layout = Layout();
            foreach (var h in Board.AllCells())
            {
                var logical = layout.Center(h);
                var round = BoardWorld.FromWorld(BoardWorld.ToWorld(logical));
                Assert.AreEqual(logical.X, round.X, 1e-4f, $"x round trip at {h}");
                Assert.AreEqual(logical.Y, round.Y, 1e-4f, $"y round trip at {h}");
            }
        }

        [Test]
        public void RoundTripHoldsAtNonUnitScale()
        {
            var layout = Layout();
            foreach (var h in Board.AllCells())
            {
                var logical = layout.Center(h);
                var round = BoardWorld.FromWorld(BoardWorld.ToWorld(logical, 3.5f), 3.5f);
                Assert.AreEqual(logical.X, round.X, 1e-3f, $"x round trip at {h}");
                Assert.AreEqual(logical.Y, round.Y, 1e-3f, $"y round trip at {h}");
            }
        }

        [Test]
        public void AllCellsGetDistinctWorldPositions()
        {
            var layout = Layout();
            var seen = new Dictionary<string, Hex>();
            foreach (var h in Board.AllCells())
            {
                var w = BoardWorld.CenterOf(h, layout);
                var key = $"{w.x:0.###},{w.y:0.###}";
                Assert.IsFalse(seen.ContainsKey(key),
                    $"{h} lands on top of {(seen.ContainsKey(key) ? seen[key].ToString() : "")} at {key}");
                seen[key] = h;
            }
            Assert.AreEqual(Board.CellCount, seen.Count, "every cell should occupy its own position");
        }

        /// <summary>
        /// Row 0 is the AI's back row and must render at the TOP of the screen, matching
        /// the browser game. Logical Y grows downward, so world Y must be negated.
        /// </summary>
        [Test]
        public void RowZeroRendersAboveTheLastRow()
        {
            var layout = Layout();
            float top = BoardWorld.CenterOf(new Hex(0, 0), layout).y;
            float bottom = BoardWorld.CenterOf(new Hex(0, Board.Rows - 1), layout).y;
            Assert.Greater(top, bottom, "row 0 must sit above the last row in world space");
        }

        [Test]
        public void PlayerHalfIsBelowTheAiHalf()
        {
            var layout = Layout();
            float ai = BoardWorld.CenterOf(new Hex(4, Board.AiRows - 1), layout).y;
            float neutral = BoardWorld.CenterOf(new Hex(4, Board.AiRows), layout).y;
            float player = BoardWorld.CenterOf(new Hex(4, Board.PlayerRow0), layout).y;
            Assert.Greater(ai, neutral, "AI half must be above no-man's land");
            Assert.Greater(neutral, player, "no-man's land must be above the player's half");
        }

        /// <summary>Columns step by 1.5 hex radii — the flat-top spacing.</summary>
        [Test]
        public void AdjacentColumnsAreOneAndAHalfRadiiApart()
        {
            var layout = Layout();
            for (int c = 0; c + 1 < Board.Cols; c++)
            {
                float a = BoardWorld.CenterOf(new Hex(c, 3), layout).x;
                float b = BoardWorld.CenterOf(new Hex(c + 1, 3), layout).x;
                Assert.AreEqual(1.5f, b - a, 1e-4f, $"column spacing between {c} and {c + 1}");
            }
        }

        [Test]
        public void BoardBoundsContainEveryCellCentre()
        {
            var layout = Layout();
            var bounds = BoardWorld.BoardBounds(layout);
            foreach (var h in Board.AllCells())
            {
                var w = BoardWorld.CenterOf(h, layout);
                Assert.IsTrue(bounds.Contains(new Vector3(w.x, w.y, bounds.center.z)),
                    $"{h} at {w} falls outside the board bounds {bounds}");
            }
        }

        [Test]
        public void BoardBoundsAreTallerThanWide()
        {
            var bounds = BoardWorld.BoardBounds(Layout());
            Assert.Greater(bounds.size.y, bounds.size.x,
                "the board is portrait — 13 rows against 9 columns");
        }

        /// <summary>
        /// World space carries the viewing-angle squash: rows sit closer together than their
        /// logical spacing, by exactly the tilt factor. Spacing must still be UNIFORM — a
        /// per-row or perspective-style falloff would be a different projection entirely.
        /// </summary>
        [Test]
        public void WorldConversionAppliesTheTiltSquashUniformly()
        {
            var layout = Layout();
            var gaps = new List<float>();
            for (int r = 0; r + 1 < Board.Rows; r++)
                gaps.Add(BoardWorld.CenterOf(new Hex(0, r), layout).y
                       - BoardWorld.CenterOf(new Hex(0, r + 1), layout).y);

            foreach (var g in gaps)
                Assert.AreEqual(gaps[0], g, 1e-4f, "row spacing must stay uniform under the tilt");

            float logicalGap = layout.Center(0, 1).Y - layout.Center(0, 0).Y;
            Assert.AreEqual(logicalGap * BoardWorld.TiltSquash, gaps[0], 1e-4f,
                "world row spacing should be the logical spacing times the tilt factor");
            Assert.Less(gaps[0], logicalGap, "the tilt must COMPRESS the board vertically");
        }

        /// <summary>
        /// The tilt must never reach the simulation. HANDOFF is explicit: movement timing,
        /// congestion, pathfinding and hex distance read logical positions, and a squash
        /// there makes units move at different speeds depending on their heading.
        /// </summary>
        [Test]
        public void SimulationSpaceIsUnaffectedByTheTilt()
        {
            var layout = Layout();
            float first = layout.Center(0, 1).Y - layout.Center(0, 0).Y;
            for (int r = 0; r + 1 < Board.Rows; r++)
                Assert.AreEqual(first, layout.Center(0, r + 1).Y - layout.Center(0, r).Y, 1e-4f,
                    "logical row spacing must be uniform and unsquashed");

            Assert.AreEqual(HexLayout.Sqrt3, first, 1e-4f,
                "logical rows are sqrt(3) apart at hex size 1 — no tilt applied");
        }

        /// <summary>The tilt compresses height only; column spacing is untouched.</summary>
        [Test]
        public void TiltDoesNotAffectHorizontalSpacing()
        {
            var layout = Layout();
            float a = BoardWorld.CenterOf(new Hex(0, 3), layout).x;
            float b = BoardWorld.CenterOf(new Hex(1, 3), layout).x;
            Assert.AreEqual(1.5f, b - a, 1e-4f, "the tilt must not touch X");
        }

        /// <summary>
        /// Ground geometry flattens; things that stand up do not. Getting these the same way
        /// round would either un-tilt the board or squash every unit sprite.
        /// </summary>
        [Test]
        public void GroundGeometryFlattensButUprightThingsDoNot()
        {
            var ground = BoardWorld.GroundScale(2f);
            Assert.AreEqual(2f, ground.x, 1e-4f);
            Assert.AreEqual(2f * BoardWorld.TiltSquash, ground.y, 1e-4f, "ground flattens in Y");

            var upright = BoardWorld.UprightScale(2f);
            Assert.AreEqual(2f, upright.x, 1e-4f);
            Assert.AreEqual(2f, upright.y, 1e-4f, "upright sprites must stay undistorted");
        }

        [Test]
        public void TiltMatchesTheBrowserGame()
        {
            Assert.AreEqual(HexLayout.TiltSquash, BoardWorld.TiltSquash, 1e-6f,
                "the two boards must read identically");
            Assert.AreEqual(0.8f, BoardWorld.TiltSquash, 1e-6f, "TILT_SQUASH from the browser game");
        }
    }
}
