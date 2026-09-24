using System;
using System.Collections.Generic;

namespace Godsbound.Core
{
    /// <summary>
    /// Answers "is there a standing building on this hex?". Destroyed buildings must
    /// report <c>false</c> — the browser game's rule is that rubble does not block a
    /// route. Supplied by the match layer; route legality does not own building state.
    /// </summary>
    public delegate bool StandingBuildingAt(int c, int r);

    /// <summary>
    /// The live terrain grid, ported from <c>TMAP_INITIAL</c> / <c>TMAP</c>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Initial"/> is the pristine base map; a <see cref="TerrainMap"/>
    /// instance is the mutable live map for one match. God powers that grow bamboo or
    /// raise terrain mutate the live map and must trigger a background refresh on the
    /// presentation side — the simulation deliberately knows nothing about that.</para>
    /// <para>Rows 0-5 use <c>P</c> and rows 7-12 use <c>D</c> as their blank filler tile;
    /// row 6 is no-man's land. Both are mechanically identical.</para>
    /// </remarks>
    public sealed class TerrainMap
    {
        /// <summary>
        /// The pristine 9x13 base map. Row strings are exactly as they appear in
        /// <c>TMAP_INITIAL</c>; the Unity tests assert this against the exported fixture,
        /// so if the browser map changes, re-run <c>tools/export_unity_reference.js</c>
        /// and the mismatch surfaces immediately.
        /// </summary>
        public static readonly string[] Initial =
        {
            "PPPPPPPPP",
            "PPPPPPPPP",
            "PPPPPPPPP",
            "PPPPPPPPP",
            "PPPPPPPPP",
            "PPPPPPPPP",
            "PPPPPPPPP",
            "DDDDDDDDD",
            "DDDDDDDDD",
            "DDDDDDDDD",
            "DDDDDDDDD",
            "DDDDDDDDD",
            "DDDDDDDDD"
        };

        private readonly TerrainType[] _cells = new TerrainType[Board.CellCount];
        private readonly TerrainType[] _base = new TerrainType[Board.CellCount];
        private readonly List<TerrainOverlay> _overlays = new List<TerrainOverlay>();

        /// <summary>
        /// Temporary god-power terrain (<c>S.floodedHexes</c>), oldest first. The live grid shows
        /// them; the base grid never changes during a match.
        /// </summary>
        public IReadOnlyList<TerrainOverlay> Overlays => _overlays;

        /// <summary>Bumped whenever live terrain changes, so presentation can refresh cheaply.</summary>
        public int Version { get; private set; }

        /// <summary>The terrain as painted before any power touched it.</summary>
        public TerrainType BaseAt(Hex h) => _base[Board.Index(h)];

        /// <summary>
        /// Lay a temporary tile (Dragon Rain, Broken Pillar, the Garden, Primordial Fire). The live
        /// tile becomes <paramref name="to"/>; the tile it replaced is remembered for expiry.
        /// </summary>
        public TerrainOverlay AddOverlay(Hex h, TerrainType to, float expiresAt, int owner = -1, float damagePerSecond = 0f)
        {
            if (!Board.InBounds(h)) throw new ArgumentOutOfRangeException(nameof(h));
            var o = new TerrainOverlay(h, this[h], to, owner, damagePerSecond, expiresAt);
            _overlays.Add(o);
            if (_cells[Board.Index(h)] != to) { _cells[Board.Index(h)] = to; Version++; }
            return o;
        }

        /// <summary>
        /// <c>tickFloods</c>'s expiry for one entry: once due, it is removed, and the tile it laid is
        /// put back to what it replaced — only if the tile still shows that overlay.
        /// </summary>
        public bool TryExpire(TerrainOverlay o, float elapsed)
        {
            if (elapsed < o.ExpiresAt) return false;
            int i = Board.Index(o.Hex);
            if (_cells[i] == o.To && o.Original != o.To) { _cells[i] = o.Original; Version++; }
            _overlays.Remove(o);
            return true;
        }

        /// <summary><c>inGrove</c>: a bamboo overlay grown by this side covers the hex.</summary>
        public bool InGrove(Hex h, int side)
        {
            foreach (var o in _overlays)
                if (o.To == TerrainType.Forest && o.Owner == side && o.Hex == h) return true;
            return false;
        }

        /// <summary>Drop every temporary tile and show the base map again.</summary>
        public void ClearOverlays()
        {
            if (_overlays.Count == 0) return;
            _overlays.Clear();
            Array.Copy(_base, _cells, _cells.Length);
            Version++;
        }

        /// <summary>A live map seeded from <see cref="Initial"/>.</summary>
        public TerrainMap()
        {
            Reset();
        }

        /// <summary>Restore every cell to the pristine base map.</summary>
        public void Reset()
        {
            _overlays.Clear();
            for (int r = 0; r < Board.Rows; r++)
                for (int c = 0; c < Board.Cols; c++)
                    _cells[Board.Index(c, r)] = _base[Board.Index(c, r)] = TerrainTable.FromCode(Initial[r][c]);
            Version++;
        }

        public TerrainType this[int c, int r]
        {
            get
            {
                if (!Board.InBounds(c, r))
                    throw new ArgumentOutOfRangeException($"({c},{r}) is off the board");
                return _cells[Board.Index(c, r)];
            }
            set
            {
                if (!Board.InBounds(c, r))
                    throw new ArgumentOutOfRangeException($"({c},{r}) is off the board");
                // Painting sets the base; it shows immediately unless a power covers the tile.
                int i = Board.Index(c, r);
                bool covered = false;
                foreach (var o in _overlays) if (o.Hex.C == c && o.Hex.R == r) { covered = true; break; }
                _base[i] = value;
                if (!covered) _cells[i] = value;
                Version++;
            }
        }

        public TerrainType this[Hex h]
        {
            get => this[h.C, h.R];
            set => this[h.C, h.R] = value;
        }

        /// <summary>The terrain code as it would appear in <c>TMAP</c>.</summary>
        public char CodeAt(int c, int r) => TerrainTable.ToCode(this[c, r]);

        /// <summary>
        /// Ports <c>passable(c, r, flying)</c>. Flying units ignore terrain entirely;
        /// ground units are stopped only by blocking terrain (mountains).
        /// </summary>
        public bool Passable(int c, int r, bool flying)
        {
            if (flying) return true;
            return !TerrainTable.Info(this[c, r]).Block;
        }

        public bool Passable(Hex h, bool flying) => Passable(h.C, h.R, flying);

        /// <summary>
        /// Ports <c>routeOK(c, r, flying)</c> — the <b>single</b> route-legality rule.
        /// A hex is routable when it is on the board, passable for the mover's layer, and
        /// carries no standing building.
        /// </summary>
        /// <param name="buildingAt">
        /// Standing-building test. Pass <c>null</c> when no buildings exist yet (early
        /// port stages and pure terrain tests); every hex then counts as building-free.
        /// </param>
        /// <remarks>
        /// Route legality has exactly one home, here. Duplicating this check with a
        /// slightly different clause elsewhere is how the browser game once let units
        /// spawn inside buildings, so resist adding a second version.
        /// </remarks>
        public bool RouteOk(int c, int r, bool flying, StandingBuildingAt buildingAt = null)
        {
            if (!Board.InBounds(c, r)) return false;
            if (!Passable(c, r, flying)) return false;
            if (buildingAt != null && buildingAt(c, r)) return false;
            return true;
        }

        public bool RouteOk(Hex h, bool flying, StandingBuildingAt buildingAt = null)
            => RouteOk(h.C, h.R, flying, buildingAt);

        /// <summary>Movement multiplier for the terrain on this hex.</summary>
        public float SpeedAt(int c, int r) => TerrainTable.Info(this[c, r]).Speed;

        /// <summary>Ranged cover multiplier for a unit standing on this hex. 0 = none.</summary>
        public float CoverAt(int c, int r) => TerrainTable.Info(this[c, r]).Cover;

        /// <summary>True when mountains here block ranged line of sight.</summary>
        public bool BlocksSightAt(int c, int r) => TerrainTable.Info(this[c, r]).Block;
    }

    /// <summary>One temporary tile laid by a god power.</summary>
    public sealed class TerrainOverlay
    {
        public Hex Hex { get; }
        /// <summary>The live tile it replaced (which may itself be an older overlay).</summary>
        public TerrainType Original { get; }
        public TerrainType To { get; }
        /// <summary>The casting side for owner-scoped effects (Xi Wangmu's grove); -1 for none.</summary>
        public int Owner { get; }
        /// <summary>Primordial Fire: damage per second to any unit standing here.</summary>
        public float DamagePerSecond { get; }
        public float ExpiresAt { get; }

        public TerrainOverlay(Hex hex, TerrainType original, TerrainType to, int owner, float damagePerSecond, float expiresAt)
        {
            Hex = hex; Original = original; To = to; Owner = owner;
            DamagePerSecond = damagePerSecond; ExpiresAt = expiresAt;
        }

        public override string ToString() => $"{Hex} {Original}->{To} until {ExpiresAt:0.##}";
    }
}
