using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>
    /// The board must never be framed under the UI. Willem's requirement, 2026-09-07:
    /// "we need to be sure that there is room for the unit and god buttons."
    /// </summary>
    /// <remarks>
    /// These assert the invariant, not the exact reserved sizes — those are tunable. The
    /// invariant is that whatever is reserved, the board lands wholly inside the remaining
    /// band, at every aspect ratio the game might run at.
    /// </remarks>
    public class BoardViewportTests
    {
        private static readonly float[] Aspects =
        {
            9f / 16f,          // shipping portrait
            393f / 852f,       // the browser game's design frame
            3f / 4f,           // tablet portrait
            2f / 3f,
            1f                 // square, a stress case
        };

        private static Bounds BoardBounds() => BoardWorld.BoardBounds(BoardWorld.UnitLayout());

        [Test]
        public void ReservedBandsLeaveRoomForTheBoard()
        {
            Assert.Greater(BoardViewport.DefaultTopFraction, 0f, "top bar must reserve space");
            Assert.Greater(BoardViewport.DefaultBottomFraction, 0f,
                "the cards and god buttons must reserve space");
            Assert.Less(BoardViewport.DefaultTopFraction + BoardViewport.DefaultBottomFraction, 1f,
                "reserved UI must not consume the whole screen");
        }

        /// <summary>The bottom band carries four cards and four god buttons; it must be the
        /// larger of the two reservations.</summary>
        [Test]
        public void BottomBandIsLargerThanTheTopBand()
        {
            Assert.Greater(BoardViewport.DefaultBottomFraction, BoardViewport.DefaultTopFraction);
        }

        [Test]
        public void BoardFitsEntirelyInsideTheSafeAreaAtEveryAspect()
        {
            var board = BoardBounds();
            foreach (var aspect in Aspects)
            {
                var f = BoardViewport.Frame(board, aspect,
                    BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);

                Assert.LessOrEqual(board.max.y, f.SafeArea.max.y + 1e-3f,
                    $"board top intrudes into the top bar at aspect {aspect:0.###}");
                Assert.GreaterOrEqual(board.min.y, f.SafeArea.min.y - 1e-3f,
                    $"board bottom intrudes into the card/god bar at aspect {aspect:0.###}");
                Assert.LessOrEqual(board.max.x, f.SafeArea.max.x + 1e-3f,
                    $"board runs off the right edge at aspect {aspect:0.###}");
                Assert.GreaterOrEqual(board.min.x, f.SafeArea.min.x - 1e-3f,
                    $"board runs off the left edge at aspect {aspect:0.###}");
            }
        }

        /// <summary>
        /// The player's own back row is where deployment happens. If anything is going to be
        /// covered it must not be that, so check the bottom-most row explicitly.
        /// </summary>
        [Test]
        public void PlayersBackRowIsClearOfTheCardBar()
        {
            var layout = BoardWorld.UnitLayout();
            var board = BoardBounds();
            var f = BoardViewport.Frame(board, BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);

            for (int c = 0; c < Board.Cols; c++)
            {
                var w = BoardWorld.CenterOf(new Hex(c, Board.Rows - 1), layout);
                Assert.Greater(w.y, f.SafeArea.min.y,
                    $"player back row cell ({c},{Board.Rows - 1}) sits inside the card bar");
            }
        }

        [Test]
        public void AiBackRowIsClearOfTheTopBar()
        {
            var layout = BoardWorld.UnitLayout();
            var f = BoardViewport.Frame(BoardBounds(), BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);

            for (int c = 0; c < Board.Cols; c++)
            {
                var w = BoardWorld.CenterOf(new Hex(c, 0), layout);
                Assert.Less(w.y, f.SafeArea.max.y,
                    $"AI back row cell ({c},0) sits inside the top bar");
            }
        }

        /// <summary>
        /// WIDTH is the binding constraint at the design aspect, and that is the point of the
        /// viewing-angle tilt. Untilted, the board's 13 rows made height the constraint and
        /// the board sat narrow with unused width — Willem's observation, 2026-09-07. The
        /// squash reclaims that height, so the board grows until it fills the width instead.
        /// The browser game lands the same way at 393x852: its HEX comes out width-bound
        /// (393/14 = 28.1px) rather than height-bound (636/18.7 = 34.0px).
        /// </summary>
        [Test]
        public void AtTheDesignAspectScreenWidthIsTheConstraint()
        {
            var board = BoardBounds();
            float boardFraction = 1f - BoardViewport.DefaultTopFraction - BoardViewport.DefaultBottomFraction;
            float sizeForHeight = board.size.y / (2f * boardFraction);
            float sizeForWidth = board.size.x / (2f * BoardViewport.DesignAspect);
            Assert.Greater(sizeForWidth, sizeForHeight,
                "with the tilt the board should fill the width, not be capped by the band height");
        }

        /// <summary>The board should reach the full width of the frame, side to side.</summary>
        [Test]
        public void BoardFillsTheAvailableWidth()
        {
            var board = BoardBounds();
            var f = BoardViewport.Frame(board, BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
            Assert.AreEqual(board.size.x, f.SafeArea.size.x, 1e-3f,
                "the board should span the frame's full width with no wasted side margin");
        }

        /// <summary>
        /// Reserving UI space must never COST board size. Because the tilt leaves the board
        /// width-bound, the vertical slack absorbs the bands outright — the cards and god
        /// buttons are free, which is the real payoff of the viewing angle.
        /// </summary>
        [Test]
        public void ReservingUiDoesNotShrinkTheCamera()
        {
            var board = BoardBounds();
            var full = BoardViewport.Frame(board, BoardViewport.DesignAspect, 0f, 0f);
            var reserved = BoardViewport.Frame(board, BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
            Assert.GreaterOrEqual(reserved.OrthographicSize, full.OrthographicSize - 1e-4f,
                "reserving UI must never shrink what the camera shows");
            Assert.AreEqual(full.OrthographicSize, reserved.OrthographicSize, 1e-3f,
                "while width-bound, the UI bands should cost no board size at all");
        }

        /// <summary>Vertical slack inside the band is expected, and is what pays for the UI.</summary>
        [Test]
        public void BandHasVerticalSlackForTheUiToLiveIn()
        {
            var board = BoardBounds();
            var f = BoardViewport.Frame(board, BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
            Assert.Greater(f.SafeArea.size.y, board.size.y,
                "the tilted board should not need the band's full height");
        }

        [Test]
        public void SafeAreaIsCentredOnTheBoard()
        {
            var board = BoardBounds();
            var f = BoardViewport.Frame(board, BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
            Assert.AreEqual(board.center.y, f.SafeArea.center.y, 1e-3f,
                "the board should sit centred in its band, not drifted toward one bar");
        }

        [Test]
        public void ImpossibleReservationsAreRejected()
        {
            var board = BoardBounds();
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => BoardViewport.Frame(board, BoardViewport.DesignAspect, 0.6f, 0.5f),
                "reserving more than the whole screen must fail loudly");
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => BoardViewport.Frame(board, 0f, 0.1f, 0.2f),
                "a zero aspect must fail loudly");
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => BoardViewport.Frame(board, BoardViewport.DesignAspect, -0.1f, 0.2f));
        }

        [Test]
        public void DesignFrameMatchesTheBrowserGame()
        {
            Assert.AreEqual(393f, BoardViewport.DesignWidthPx, "PHONE_W from fitAppToPhone");
            Assert.AreEqual(852f, BoardViewport.DesignHeightPx, "PHONE_H from fitAppToPhone");
            Assert.Less(BoardViewport.DesignAspect, 1f, "the design frame is portrait");
        }
    }
}
