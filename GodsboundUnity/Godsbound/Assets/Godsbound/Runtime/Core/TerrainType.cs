using System.Collections.Generic;

namespace Godsbound.Core
{
    /// <summary>
    /// Terrain codes, matching the single-character codes in <c>TMAP</c>.
    /// </summary>
    /// <remarks>
    /// Plains and Desert are mechanically identical faction-themed base tiles: Plains is
    /// the northern/China-side filler, Desert the southern/player-side filler. Do not
    /// "simplify" them into one code — the art and the faction reads depend on both.
    /// </remarks>
    public enum TerrainType
    {
        Plains,
        Desert,
        Forest,
        Mountain,
        Water,
        Road,
        HighGround
    }

    /// <summary>Movement and combat properties of one terrain code.</summary>
    public readonly struct TerrainInfo
    {
        /// <summary>The single character used in <c>TMAP</c>.</summary>
        public readonly char Code;
        public readonly string Name;
        /// <summary>Movement multiplier. 0 means impassable to ground units.</summary>
        public readonly float Speed;
        /// <summary>True when ground routes and ranged line of sight are blocked.</summary>
        public readonly bool Block;
        /// <summary>Incoming ranged damage multiplier for a unit standing here. 0 = none.</summary>
        public readonly float Cover;
        public readonly bool HighGround;

        public TerrainInfo(char code, string name, float speed, bool block, float cover, bool highGround)
        {
            Code = code;
            Name = name;
            Speed = speed;
            Block = block;
            Cover = cover;
            HighGround = highGround;
        }
    }

    /// <summary>
    /// The terrain table, ported from <c>TINFO</c> in <c>godsbound_beta.html</c>.
    /// </summary>
    /// <remarks>
    /// <para>Balance belongs in this table, not in special-case logic elsewhere. If a
    /// terrain needs to behave differently, change the numbers here.</para>
    /// <para><b>Named <c>TerrainTable</c>, not <c>Terrain</c>, on purpose.</b>
    /// <c>UnityEngine.Terrain</c> exists, so any presentation file with both
    /// <c>using UnityEngine;</c> and <c>using Godsbound.Core;</c> gets CS0104 'ambiguous
    /// reference' on the bare name. That bit during U2. Do not rename it back.</para>
    /// </remarks>
    public static class TerrainTable
    {
        private static readonly Dictionary<TerrainType, TerrainInfo> Table =
            new Dictionary<TerrainType, TerrainInfo>
            {
                { TerrainType.Plains,     new TerrainInfo('P', "Plains",       1.00f, false, 0f,   false) },
                { TerrainType.Desert,     new TerrainInfo('D', "Desert",       1.00f, false, 0f,   false) },
                { TerrainType.Forest,     new TerrainInfo('F', "Forest",       0.75f, false, 0.8f, false) },
                { TerrainType.Mountain,   new TerrainInfo('M', "Mountain",     0.00f, true,  0f,   false) },
                { TerrainType.Water,      new TerrainInfo('W', "Water",        0.50f, false, 0f,   false) },
                { TerrainType.Road,       new TerrainInfo('R', "Road",         1.50f, false, 0f,   false) },
                { TerrainType.HighGround, new TerrainInfo('H', "High Ground",  0.85f, false, 0f,   true)  }
            };

        public static TerrainInfo Info(TerrainType t) => Table[t];

        public static IEnumerable<TerrainType> AllTypes => Table.Keys;

        /// <summary>Parse a <c>TMAP</c> character. Throws on an unknown code rather than
        /// silently defaulting — a typo in a terrain string should fail loudly.</summary>
        public static TerrainType FromCode(char code)
        {
            switch (code)
            {
                case 'P': return TerrainType.Plains;
                case 'D': return TerrainType.Desert;
                case 'F': return TerrainType.Forest;
                case 'M': return TerrainType.Mountain;
                case 'W': return TerrainType.Water;
                case 'R': return TerrainType.Road;
                case 'H': return TerrainType.HighGround;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(code), code, "Unknown terrain code");
            }
        }

        public static char ToCode(TerrainType t) => Table[t].Code;
    }
}
