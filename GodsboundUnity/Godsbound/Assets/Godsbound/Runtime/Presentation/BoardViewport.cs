using UnityEngine;
using Godsbound.Core;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Frames the board into the part of the screen the UI does not occupy.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> In the browser game the board never overlaps the UI:
    /// <c>#topbar</c> and <c>#bottombar</c> are <c>flex:0 0 auto</c> and <c>#boardwrap</c>
    /// is <c>flex:1 1 auto</c>, so <c>computeCamera()</c> only ever fits the board into
    /// whatever height is left over. Framing the board to the FULL screen in Unity would
    /// put the unit cards and god buttons on top of the player's own rows — precisely
    /// where deployment happens, so it is the worst possible place to occlude.</para>
    /// <para><b>The reserved sizes are DERIVED, not measured.</b> They are computed from the
    /// browser game's CSS at its design frame of 393x852 (<c>fitAppToPhone</c>'s
    /// <c>PHONE_W</c>/<c>PHONE_H</c>): the bottom band covers the god row
    /// (<c>.colhead</c> + <c>#playergods</c>) and the card row (<c>.colhead</c> +
    /// <c>#cards</c> at its 68px <c>min-height</c> plus padding), the top band covers
    /// <c>#topbar</c>'s names, timer, enemy god icons and resource bars. Expect them to be
    /// within roughly 20px of the real rendered heights — they are a starting point to be
    /// tuned against the running game, which is why <see cref="BoardView"/> exposes them
    /// as editable fields rather than burying these constants.</para>
    /// <para>The bottom bar's height is fixed at runtime in the browser game and must stay
    /// that way here — see HANDOFF. A band that grows and shrinks would reframe the board
    /// mid-match.</para>
    /// </remarks>
    public static class BoardViewport
    {
        /// <summary>Design frame width, from <c>fitAppToPhone</c>'s <c>PHONE_W</c>.</summary>
        public const float DesignWidthPx = 393f;

        /// <summary>Design frame height, from <c>fitAppToPhone</c>'s <c>PHONE_H</c>.</summary>
        public const float DesignHeightPx = 852f;

        /// <summary>Derived height of <c>#topbar</c> at the design frame.</summary>
        public const float TopUiPx = 62f;

        /// <summary>
        /// Derived height of <c>#bottombar</c> at the design frame — the god buttons and
        /// the four unit cards. This is the band Willem asked to be certain about.
        /// </summary>
        public const float BottomUiPx = 153f;

        public static float DefaultTopFraction => TopUiPx / DesignHeightPx;

        public static float DefaultBottomFraction => BottomUiPx / DesignHeightPx;

        /// <summary>The design frame's aspect (width / height) — portrait, so below 1.</summary>
        public static float DesignAspect => DesignWidthPx / DesignHeightPx;

        /// <summary>How a camera should be set up to frame the board clear of the UI.</summary>
        public readonly struct Framing
        {
            /// <summary>Orthographic size — HALF the visible world height.</summary>
            public readonly float OrthographicSize;

            /// <summary>Where the camera sits on the board plane (Z untouched).</summary>
            public readonly Vector2 Center;

            /// <summary>The world-space band left for the board once the UI is reserved.</summary>
            public readonly Bounds SafeArea;

            public Framing(float orthographicSize, Vector2 center, Bounds safeArea)
            {
                OrthographicSize = orthographicSize;
                Center = center;
                SafeArea = safeArea;
            }
        }

        /// <summary>
        /// Fit <paramref name="board"/> into the screen minus the reserved UI bands.
        /// </summary>
        /// <param name="board">World-space bounds of the board.</param>
        /// <param name="aspect">Viewport aspect, width / height.</param>
        /// <param name="topFraction">Share of screen height reserved at the top.</param>
        /// <param name="bottomFraction">Share of screen height reserved at the bottom.</param>
        public static Framing Frame(Bounds board, float aspect,
                                    float topFraction, float bottomFraction)
        {
            if (aspect <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(aspect), aspect, "Aspect must be positive");
            if (topFraction < 0f || bottomFraction < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(topFraction), "Reserved fractions cannot be negative");
            if (topFraction + bottomFraction >= 1f)
                throw new System.ArgumentOutOfRangeException(nameof(topFraction),
                    $"Reserved UI ({topFraction:P0} + {bottomFraction:P0}) leaves no room for the board");

            float boardFraction = 1f - topFraction - bottomFraction;

            // Visible height is 2*size. The board must fit the safe band's height AND the
            // full viewport width, so take whichever constraint demands the larger camera.
            float sizeForHeight = board.size.y / (2f * boardFraction);
            float sizeForWidth = board.size.x / (2f * aspect);
            float size = Mathf.Max(sizeForHeight, sizeForWidth);

            // Centre the SAFE BAND on the board, not the viewport. Solving
            // safeBandCentre == board.center.y gives this offset.
            float centerY = board.center.y - size * (bottomFraction - topFraction);
            var center = new Vector2(board.center.x, centerY);

            float halfWidth = size * aspect;
            float safeTop = centerY + size - 2f * size * topFraction;
            float safeBottom = centerY - size + 2f * size * bottomFraction;

            var safe = new Bounds();
            safe.SetMinMax(new Vector3(center.x - halfWidth, safeBottom, 0f),
                           new Vector3(center.x + halfWidth, safeTop, 0f));

            return new Framing(size, center, safe);
        }

        /// <summary>Frame the standard board at the browser game's design aspect.</summary>
        public static Framing FrameDefaultBoard(float unitsPerHex = BoardWorld.DefaultUnitsPerHex)
            => Frame(BoardWorld.BoardBounds(BoardWorld.UnitLayout(), unitsPerHex),
                     DesignAspect, DefaultTopFraction, DefaultBottomFraction);
    }
}
