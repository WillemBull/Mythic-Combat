using System;
using System.Collections.Generic;

namespace Godsbound.Core
{
    /// <summary>
    /// A board cell in flat-top, <b>odd-q offset</b> coordinates: <c>C</c> is the column,
    /// <c>R</c> is the row. This is a direct port of <c>neighbors(c,r)</c>,
    /// <c>offsetToCube</c> and <c>hexDist(a,b)</c> from <c>godsbound_beta.html</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Never infer adjacency or distance by subtracting coordinates.</b> On an
    /// odd-q board the neighbour offsets differ between even and odd columns, so
    /// coordinate arithmetic that looks right on column 0 is wrong on column 1. Go
    /// through <see cref="Neighbors"/> and <see cref="Distance"/>, exactly as the browser
    /// game's HANDOFF requires.</para>
    /// <para>This type is deliberately free of any UnityEngine dependency so the
    /// simulation stays testable in plain C# and independent of presentation. Screen
    /// projection lives in <see cref="HexLayout"/>.</para>
    /// </remarks>
    [Serializable]
    public readonly struct Hex : IEquatable<Hex>
    {
        public readonly int C;
        public readonly int R;

        public Hex(int c, int r)
        {
            C = c;
            R = r;
        }

        /// <summary>
        /// Neighbour offsets for even columns, in the same order the browser game lists
        /// them. Order is preserved so reference fixtures compare element by element.
        /// </summary>
        private static readonly (int dc, int dr)[] EvenOffsets =
        {
            (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (0, 1)
        };

        /// <summary>Neighbour offsets for odd columns.</summary>
        private static readonly (int dc, int dr)[] OddOffsets =
        {
            (1, 1), (1, 0), (0, -1), (-1, 0), (-1, 1), (0, 1)
        };

        /// <summary>
        /// The six adjacent hexes, in the browser game's order, <b>filtered to those on
        /// the board</b>. Edge and corner cells therefore return fewer than six — the
        /// bounds filter is part of the ported behaviour, not an optimisation.
        /// </summary>
        public IEnumerable<Hex> Neighbors()
        {
            var offsets = (C & 1) != 0 ? OddOffsets : EvenOffsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                var h = new Hex(C + offsets[i].dc, R + offsets[i].dr);
                if (Board.InBounds(h)) yield return h;
            }
        }

        /// <summary>
        /// The six adjacent hexes including any that fall off the board. Use this only
        /// when you need the raw ring; gameplay wants <see cref="Neighbors"/>.
        /// </summary>
        public IEnumerable<Hex> NeighborsUnclamped()
        {
            var offsets = (C & 1) != 0 ? OddOffsets : EvenOffsets;
            for (int i = 0; i < offsets.Length; i++)
                yield return new Hex(C + offsets[i].dc, R + offsets[i].dr);
        }

        /// <summary>
        /// Cube coordinates for this hex, matching <c>offsetToCube</c>: <c>x = c</c>,
        /// <c>z = r - (c - (c &amp; 1)) / 2</c>, <c>y = -x - z</c>.
        /// </summary>
        public (int x, int y, int z) ToCube()
        {
            int x = C;
            int z = R - (C - (C & 1)) / 2;
            return (x, -x - z, z);
        }

        /// <summary>
        /// Hex distance in steps: the cube-coordinate Chebyshev distance. This is the
        /// only correct way to measure range on this board.
        /// </summary>
        public static int Distance(Hex a, Hex b)
        {
            var (ax, ay, az) = a.ToCube();
            var (bx, by, bz) = b.ToCube();
            return Math.Max(Math.Abs(ax - bx), Math.Max(Math.Abs(ay - by), Math.Abs(az - bz)));
        }

        /// <summary>Hex distance from this hex to <paramref name="other"/>.</summary>
        public int DistanceTo(Hex other) => Distance(this, other);

        /// <summary>True when <paramref name="other"/> is one step away.</summary>
        public bool IsAdjacentTo(Hex other) => Distance(this, other) == 1;

        public bool Equals(Hex other) => C == other.C && R == other.R;
        public override bool Equals(object obj) => obj is Hex h && Equals(h);
        public override int GetHashCode() => (C * 397) ^ R;
        public static bool operator ==(Hex a, Hex b) => a.Equals(b);
        public static bool operator !=(Hex a, Hex b) => !a.Equals(b);
        public override string ToString() => $"({C},{R})";
    }
}
