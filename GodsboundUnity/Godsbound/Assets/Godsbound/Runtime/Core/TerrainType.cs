using System.Collections.Generic;

namespace Godsbound.Core
{
    /// <summary>
    /// Terrain codes, matching the single-character codes in <c>TMAP</c>.
    /// </summary>
    /// <remarks>
    /// <para>2026-09-28 (Willem's call): Desert, Road and High Ground were removed from the
    /// game. Road and High Ground were dead codes — nothing painted them, the starting map
    /// never held one, the AI generator never made one, and the deck validator already
    /// refused them. Desert was real but indistinguishable from Plains: same speed, no
    /// block, no cover, and every "is this bare ground" check accepted either. It was the
    /// player-half filler, told apart only by a fill colour that U43 stopped drawing when
    /// the board became an arena photograph.</para>
    /// <para>An earlier version of this comment insisted Plains and Desert must not be
    /// merged because "the art and the faction reads depend on both". That stopped being
    /// true at U43. Do not re-add a second base code without a mechanical difference to
    /// justify it.</para>
    /// </remarks>
    public enum TerrainType
    {
        Plains,
        Forest,
        Mountain,
        Water
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
                { TerrainType.Plains,   new TerrainInfo('P', "Plains",   1.00f, false, 0f,   false) },
                { TerrainType.Forest,   new TerrainInfo('F', "Forest",   0.75f, false, 0.8f, false) },
                { TerrainType.Mountain, new TerrainInfo('M', "Mountain", 0.00f, true,  0f,   false) },
                { TerrainType.Water,    new TerrainInfo('W', "Water",    0.50f, false, 0f,   false) }
            };

        public static TerrainInfo Info(TerrainType t) => Table[t];

        public static IEnumerable<TerrainType> AllTypes => Table.Keys;

        /// <summary>Parse a <c>TMAP</c> character. Throws on an unknown code rather than
        /// silently defaulting — a typo in a terrain string should fail loudly.</summary>
        /// <remarks>
        /// 'D' is still accepted and reads as Plains. It is not a terrain any more; it is a
        /// migration, matching <c>TERRAIN_MIGRATE</c> in the browser build. Decks saved
        /// before 2026-09-28 carry Desert tiles, and Desert always played as Plains, so
        /// loading one must not throw. 'R' and 'H' get no such mercy: the deck validator
        /// refused them even when they existed, so no saved deck can contain one.
        /// </remarks>
        public static TerrainType FromCode(char code)
        {
            switch (code)
            {
                case 'P': return TerrainType.Plains;
                case 'D': return TerrainType.Plains; // legacy Desert; see the remarks above
                case 'F': return TerrainType.Forest;
                case 'M': return TerrainType.Mountain;
                case 'W': return TerrainType.Water;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(code), code, "Unknown terrain code");
            }
        }

        public static char ToCode(TerrainType t) => Table[t].Code;
    }
}
