using System.Collections.Generic;
using UnityEngine;
using Godsbound.Core;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Board colours for each terrain code, matching <c>TINFO</c>'s <c>fill</c> and
    /// <c>fill2</c> in <c>godsbound_beta.html</c>.
    /// </summary>
    /// <remarks>
    /// Presentation data, so it lives here rather than in <see cref="Godsbound.Core"/> —
    /// the simulation must not know what colour a mountain is. The values are still checked
    /// against the exported browser-game fixture, so the Unity board reads the same as the
    /// canvas one and a palette change in the HTML surfaces as a failing test.
    /// <c>fill2</c> is the browser game's alternating shade, used here to chequer adjacent
    /// hexes so the grid is legible without drawing outlines.
    /// </remarks>
    public static class TerrainPalette
    {
        private static readonly Dictionary<TerrainType, (string fill, string fill2)> Hex =
            new Dictionary<TerrainType, (string, string)>
            {
                { TerrainType.Plains,     ("#6f8a41", "#647d3a") },
                { TerrainType.Desert,     ("#c7a15c", "#b99450") },
                { TerrainType.Forest,     ("#46672f", "#3e5d2b") },
                { TerrainType.Mountain,   ("#7b766d", "#6e6961") },
                { TerrainType.Water,      ("#2f7391", "#2a6884") },
                { TerrainType.Road,       ("#a08a5c", "#957f53") },
                { TerrainType.HighGround, ("#93905c", "#868353") }
            };

        /// <summary>The primary fill colour for a terrain type.</summary>
        public static Color Fill(TerrainType t) => Parse(Hex[t].fill);

        /// <summary>The alternate (darker) fill colour for a terrain type.</summary>
        public static Color Fill2(TerrainType t) => Parse(Hex[t].fill2);

        /// <summary>The hex string as it appears in the browser game, for verification.</summary>
        public static string FillHex(TerrainType t) => Hex[t].fill;

        public static string Fill2Hex(TerrainType t) => Hex[t].fill2;

        /// <summary>
        /// Fill for a cell, alternating between the two shades so neighbouring hexes of the
        /// same terrain stay distinguishable.
        /// </summary>
        public static Color For(TerrainType t, int c, int r)
            => ((c + r) & 1) == 0 ? Fill(t) : Fill2(t);

        private static Color Parse(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var col))
                throw new System.FormatException($"Bad palette colour '{hex}'");
            return col;
        }
    }
}
