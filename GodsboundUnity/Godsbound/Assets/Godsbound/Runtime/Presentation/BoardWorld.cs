using UnityEngine;
using Godsbound.Core;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Converts between the simulation's flat logical board space and Unity world space.
    /// </summary>
    /// <remarks>
    /// <para>This is the ONLY place the two spaces meet. <see cref="Godsbound.Core"/> knows
    /// nothing about Unity or world coordinates, and nothing in the simulation may call
    /// into this class — the browser game's HANDOFF is explicit that movement timing,
    /// pathfinding and hex distance all read logical positions directly, and routing them
    /// through a projection is how units end up moving at different speeds depending on
    /// their heading.</para>
    /// <para>The board is laid out on the XY plane with +Y up, viewed by a camera looking
    /// down −Z. Logical space has +Y pointing DOWN the board (row 0 at the top, matching
    /// the canvas game), so the conversion negates Y: row 0 lands at the top of the screen
    /// and the player's rows at the bottom, exactly as in the browser game.</para>
    /// <para><b>The tilt IS applied here</b>, and this is the right place for it. In the
    /// browser game the tilt is a 2D screen-space squash — <c>toScreen()</c> multiplies y by
    /// <see cref="HexLayout.TiltSquash"/> and <c>squashedHexPoly()</c> draws the hexes
    /// pre-flattened — not a 3D camera rotation. This class is the Unity analogue of
    /// <c>toScreen</c>, so the squash belongs in it. <see cref="HexLayout.Center"/> stays
    /// unsquashed, which is what HANDOFF actually requires: movement timing, congestion,
    /// pathfinding and hex distance all read logical positions and never see the tilt.</para>
    /// <para>An earlier revision kept the squash out of here and claimed the camera should be
    /// angled instead. That was wrong twice over: it wasted the vertical space the squash is
    /// supposed to reclaim (leaving the board narrow with unused width), and a real 3D tilt
    /// would foreshorten unit sprites, which the browser game deliberately draws upright.</para>
    /// <para><b>Never compose this with <see cref="HexLayout.ToScreen"/></b> — that is the
    /// pure-logical version of the same projection, and applying both squashes twice.</para>
    /// </remarks>
    public static class BoardWorld
    {
        /// <summary>
        /// World units per hex circumradius. The logical layout is built at
        /// <see cref="HexLayout.Hex"/> = 1 so that one logical unit is one world unit and
        /// this scale is the only size knob.
        /// </summary>
        public const float DefaultUnitsPerHex = 1f;

        /// <summary>
        /// The vertical compression the board is drawn with — the viewing angle. Same value
        /// the browser game uses, and it must stay the same value: the two boards should read
        /// identically.
        /// </summary>
        public const float TiltSquash = HexLayout.TiltSquash;

        /// <summary>
        /// The canonical board-space basis this converter is paired with. Delegates to
        /// <see cref="HexLayout.Unit"/> so there is exactly one definition — the simulation
        /// needs the same basis and cannot reference this layer.
        /// </summary>
        public static HexLayout UnitLayout() => HexLayout.Unit();

        /// <summary>
        /// Logical board point to world position on the XY plane, with the viewing-angle
        /// squash applied to Y. X is never touched.
        /// </summary>
        public static Vector3 ToWorld(BoardPoint p, float unitsPerHex = DefaultUnitsPerHex)
            => new Vector3(p.X * unitsPerHex, -p.Y * unitsPerHex * TiltSquash, 0f);

        /// <summary>World position back to a logical board point. Z is ignored.</summary>
        public static BoardPoint FromWorld(Vector3 w, float unitsPerHex = DefaultUnitsPerHex)
            => new BoardPoint(w.x / unitsPerHex, -w.y / (unitsPerHex * TiltSquash));

        /// <summary>
        /// Scale for geometry lying FLAT on the ground plane — hex cells, ward discs, range
        /// rings. Flattened in Y to match the viewing angle, exactly as the browser game's
        /// <c>squashedHexPoly()</c> pre-flattens its hexes.
        /// </summary>
        public static Vector3 GroundScale(float uniform)
            => new Vector3(uniform, uniform * TiltSquash, uniform);

        /// <summary>
        /// Scale for things that STAND UP on the board — unit and building sprites. Uniform,
        /// deliberately unsquashed: the browser game draws sprites undistorted at squashed
        /// positions, which is what makes the board read as a tilted ground plane with
        /// upright figures on it. Do not use <see cref="GroundScale"/> for these.
        /// </summary>
        public static Vector3 UprightScale(float uniform) => Vector3.one * uniform;

        /// <summary>World position of a hex's centre.</summary>
        public static Vector3 CenterOf(Hex h, HexLayout layout, float unitsPerHex = DefaultUnitsPerHex)
            => ToWorld(layout.Center(h), unitsPerHex);

        /// <summary>
        /// The board's world-space bounds, corner to corner of the outermost hex centres
        /// plus one hex of margin. Used to frame the camera.
        /// </summary>
        public static Bounds BoardBounds(HexLayout layout, float unitsPerHex = DefaultUnitsPerHex)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            var max = new Vector3(float.MinValue, float.MinValue, 0f);

            foreach (var h in Board.AllCells())
            {
                var w = CenterOf(h, layout, unitsPerHex);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }

            // One hex of margin, squashed vertically like everything else on the ground
            // plane — an unsquashed margin would inflate the board's apparent height and
            // give back some of the space the tilt just reclaimed.
            var margin = layout.Hex * unitsPerHex;
            var marginV = margin * TiltSquash;
            min -= new Vector3(margin, marginV, 0f);
            max += new Vector3(margin, marginV, 0f);

            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }
    }
}
