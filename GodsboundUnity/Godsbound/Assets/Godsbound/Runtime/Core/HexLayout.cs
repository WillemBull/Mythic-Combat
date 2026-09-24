using System;

namespace Godsbound.Core
{
    /// <summary>A point in the game's flat, unsquashed logical space.</summary>
    public readonly struct BoardPoint
    {
        public readonly float X;
        public readonly float Y;
        public BoardPoint(float x, float y) { X = x; Y = y; }
        public override string ToString() => $"({X:0.######}, {Y:0.######})";
    }

    /// <summary>
    /// Converts between hex coordinates and positions, ported from <c>hexCenter</c>,
    /// <c>toScreen</c>/<c>fromScreen</c> and <c>pixelToHex</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>The critical rule from the browser game's HANDOFF:</b> the simulation
    /// lives in a flat, isotropic top-down grid. <see cref="TiltSquash"/> is a
    /// <i>screen-space</i> effect only. Movement timing, congestion, pathfinding and hex
    /// distance all read <see cref="Center"/> directly and must never route through
    /// <see cref="ToScreen"/> / <see cref="FromScreen"/>. Squashing the simulation
    /// instead of the presentation makes units move at different speeds depending on
    /// their heading, which is the exact bug this separation exists to prevent.</para>
    /// <para>Kept free of UnityEngine types so it is testable in EditMode without a
    /// scene. The presentation layer converts <see cref="BoardPoint"/> to world space.</para>
    /// </remarks>
    public sealed class HexLayout
    {
        /// <summary>Uniform 4/5 vertical compression applied at draw time only.</summary>
        public const float TiltSquash = 0.8f;

        public static readonly float Sqrt3 = (float)Math.Sqrt(3.0);

        /// <summary>Hex size (circumradius) in logical units.</summary>
        public float Hex { get; }

        /// <summary>Board origin offsets, set on resize.</summary>
        public float OffsetX { get; }

        public float OffsetY { get; }

        /// <summary>
        /// The canonical basis: hex size 1, no offsets, so one logical unit is one hex
        /// radius. Lives here rather than in the presentation layer because the simulation
        /// needs it too — <c>UnitField</c> positions units with it, and Core may not
        /// reference Presentation.
        /// </summary>
        public static HexLayout Unit() => new HexLayout(1f, 0f, 0f);

        public HexLayout(float hex = 24f, float offsetX = 0f, float offsetY = 0f)
        {
            Hex = hex;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        /// <summary>
        /// Centre of a hex in flat logical space. Ports <c>hexCenter(c, r)</c>:
        /// columns step by <c>1.5 * HEX</c> and odd columns are offset half a row down.
        /// </summary>
        public BoardPoint Center(int c, int r)
        {
            float x = OffsetX + Hex * 1.5f * c + Hex;
            float y = OffsetY + Hex * Sqrt3 * (r + 0.5f * (c & 1)) + Hex * Sqrt3 / 2f;
            return new BoardPoint(x, y);
        }

        public BoardPoint Center(Hex h) => Center(h.C, h.R);

        /// <summary>Vertical centre of the current unsquashed board — the tilt pivot.</summary>
        public float BoardPivotY() => OffsetY + Hex * Sqrt3 * (Board.Rows + 0.5f) / 2f;

        /// <summary>Project a logical point to screen space. Drawing only.</summary>
        public BoardPoint ToScreen(BoardPoint p)
        {
            float pivot = BoardPivotY();
            return new BoardPoint(p.X, pivot + (p.Y - pivot) * TiltSquash);
        }

        /// <summary>Un-project a screen point back to logical space. Input hit-testing only.</summary>
        public BoardPoint FromScreen(BoardPoint p)
        {
            float pivot = BoardPivotY();
            return new BoardPoint(p.X, pivot + (p.Y - pivot) / TiltSquash);
        }

        /// <summary>
        /// Hit-test a raw pointer position to a hex, or <c>null</c> if the tap landed off
        /// the board. Ports <c>pixelToHex</c>, including its inverse projection and its
        /// <c>1.15 * HEX</c> acceptance radius — callers pass raw pointer coordinates and
        /// must not pre-unproject.
        /// </summary>
        public Hex? PixelToHex(float px, float py)
        {
            var logical = FromScreen(new BoardPoint(px, py));
            px = logical.X;
            py = logical.Y;

            Hex? best = null;
            float bestDist = float.MaxValue;

            int rGuess = (int)Math.Round((py - OffsetY - Hex * Sqrt3 / 2f) / (Hex * Sqrt3),
                                         MidpointRounding.AwayFromZero);
            int rLo = Math.Max(0, rGuess - 1);
            int rHi = Math.Min(Board.Rows - 1, rGuess + 1);

            for (int r = rLo; r <= rHi; r++)
                for (int c = 0; c < Board.Cols; c++)
                {
                    var ct = Center(c, r);
                    float d = (ct.X - px) * (ct.X - px) + (ct.Y - py) * (ct.Y - py);
                    if (d < bestDist) { bestDist = d; best = new Hex(c, r); }
                }

            float accept = Hex * 1.15f;
            return bestDist < accept * accept ? best : null;
        }
    }
}
