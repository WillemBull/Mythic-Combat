using Godsbound.Core;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Where each faction's arena painting sits behind the board, and how the two blend through
    /// no-man's land. Ported from the browser's <c>arenaBgPlacement</c> / <c>drawArenaBgRect</c>
    /// and the neutral-band cross-fade it grew on 2026-07-29 (U43).
    /// </summary>
    /// <remarks>
    /// Pure: the placement is arithmetic, and getting it wrong is the kind of thing that looks
    /// "roughly right" on screen and is obviously wrong in a number. The view below draws what this
    /// decides and owns no geometry of its own.
    /// </remarks>
    public static class ArenaBackdrop
    {
        /// <summary>
        /// How many strips the cross-fade is built from. The browser ramps alpha continuously across
        /// the band through a clip; quads cannot do that without a shader, so the ramp is stepped —
        /// and every strip samples the SAME placement, which is the browser's own warning: fitting
        /// the image to each strip's little box instead would tear the scene apart.
        /// </summary>
        public const int FadeStrips = 12;

        /// <summary>The flat colour used while art is missing, from <c>ARENA_BG_FALLBACK</c>.</summary>
        public static UnityEngine.Color Fallback(string faction) =>
            faction == "egypt" ? Hex(0xC7, 0xA1, 0x5C) : Hex(0x6F, 0x8A, 0x41);

        private static UnityEngine.Color Hex(int r, int g, int b) =>
            new UnityEngine.Color(r / 255f, g / 255f, b / 255f);

        /// <summary>A cover fit: aspect preserved, centred, overflowing the box on one axis.</summary>
        public readonly struct Placement
        {
            public readonly float X, Y, Width, Height;
            public Placement(float x, float y, float width, float height)
            { X = x; Y = y; Width = width; Height = height; }
            public float CenterX => X + Width * 0.5f;
            public float CenterY => Y + Height * 0.5f;
        }

        /// <summary>
        /// The browser's <c>arenaBgPlacement</c>: fill the box, keep the image's aspect, centre the
        /// overflow. An image wider than the box matches the box's height and spills sideways.
        /// </summary>
        public static Placement Cover(float x, float y, float width, float height, float imageAspect)
        {
            if (height <= 0f || width <= 0f || imageAspect <= 0f) return new Placement(x, y, width, height);
            float boxAspect = width / height, w, h;
            if (imageAspect > boxAspect) { h = height; w = h * imageAspect; }
            else { w = width; h = w / imageAspect; }
            return new Placement(x + width * 0.5f - w * 0.5f, y + height * 0.5f - h * 0.5f, w, h);
        }

        /// <summary>
        /// The alpha of the player's painting at a height inside the cross-fade band, running 0 at
        /// the band's far edge (all the AI's scene) to 1 at the near edge (all the player's).
        /// </summary>
        public static float FadeAt(float y, float bandFar, float bandNear)
        {
            if (bandNear == bandFar) return y >= bandNear ? 1f : 0f;
            float t = (y - bandFar) / (bandNear - bandFar);
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        /// <summary>
        /// The vertical span of no-man's land in board space, across BOTH column parities — odd
        /// columns sit half a row lower, so taking one column's bounds clips the fade short. The
        /// browser learned this the same way (<c>neutralBandSpan</c>).
        /// </summary>
        public static bool NeutralBand(HexLayout layout, out float top, out float bottom)
        {
            top = bottom = 0f;
            if (Board.NeutralRows < 1) return false;
            float half = layout.Hex * HexLayout.Sqrt3 * 0.5f;
            bool any = false;
            for (int c = 0; c < Board.Cols; c++)
                for (int r = Board.AiRows; r < Board.PlayerRow0; r++)
                {
                    var centre = layout.Center(c, r);
                    float hi = centre.Y - half, lo = centre.Y + half;
                    if (!any) { top = hi; bottom = lo; any = true; continue; }
                    if (hi < top) top = hi;
                    if (lo > bottom) bottom = lo;
                }
            return any;
        }
    }
}
