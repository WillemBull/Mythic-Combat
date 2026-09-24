using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;

namespace Godsbound.Core.Buildings
{
    /// <summary>
    /// The hex-to-building index, ported from <c>bldHexMap</c> / <c>rebuildBldHexMap</c>,
    /// and the source of truth for whether a hex is blocked by a building.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Rebuild"/> must be called after any change to building positions —
    /// the browser game has the same requirement and the same failure mode if it is skipped
    /// (units routing through a building that moved).</para>
    /// <para><see cref="StandingAt(Hex)"/> returns only LIVING buildings, which is what makes
    /// rubble walkable. <see cref="AnyAt(Hex)"/> ignores death, for rendering.</para>
    /// </remarks>
    public sealed class BuildingMap
    {
        private readonly List<Building> _buildings = new List<Building>();
        private readonly Dictionary<int, Building> _byCell = new Dictionary<int, Building>();

        public BuildingMap() { }

        public BuildingMap(IEnumerable<Building> buildings)
        {
            _buildings.AddRange(buildings);
            Rebuild();
        }

        /// <summary>All six starting buildings, from the exported definitions.</summary>
        public static BuildingMap FromExport(IEnumerable<BuildingData> data) =>
            new BuildingMap(data.Select(Building.From));

        public IReadOnlyList<Building> All => _buildings;

        public void Add(Building b)
        {
            _buildings.Add(b);
            Rebuild();
        }

        /// <summary>
        /// Re-index every building's hexes. Throws when two buildings claim the same hex —
        /// silently letting the later one win would make route legality depend on list order.
        /// </summary>
        public void Rebuild()
        {
            _byCell.Clear();
            foreach (var b in _buildings)
                foreach (var h in b.Hexes())
                {
                    int key = Board.Index(h);
                    if (_byCell.TryGetValue(key, out var other) && other != b)
                        throw new System.InvalidOperationException(
                            $"{b} and {other} both claim {h}");
                    _byCell[key] = b;
                }
        }

        /// <summary>The building on this hex whether alive or dead, or null.</summary>
        public Building AnyAt(Hex h) =>
            Board.InBounds(h) && _byCell.TryGetValue(Board.Index(h), out var b) ? b : null;

        /// <summary>
        /// The STANDING building on this hex, or null. Ports <c>bldAt</c>: a destroyed
        /// building reports nothing, so its hexes become passable.
        /// </summary>
        public Building StandingAt(Hex h)
        {
            var b = AnyAt(h);
            return b != null && !b.Dead ? b : null;
        }

        public Building StandingAt(int c, int r) => StandingAt(new Hex(c, r));

        /// <summary>
        /// Adapter for <see cref="TerrainMap.RouteOk"/>, which takes a
        /// <see cref="StandingBuildingAt"/> delegate. This is what finally supplies it.
        /// </summary>
        public StandingBuildingAt RouteBlocker => _routeBlocker ?? (_routeBlocker = (c, r) => StandingAt(c, r) != null);

        // One delegate per map. Movement, the draft and the AI read this property inside per-hex
        // loops every frame, and a fresh closure per read was a steady allocation for nothing.
        private StandingBuildingAt _routeBlocker;

        public Building Of(int side, BuildingType type) =>
            _buildings.FirstOrDefault(b => b.Side == side && b.Type == type);

        public IEnumerable<Building> Of(int side) => _buildings.Where(b => b.Side == side);

        public int StandingCount(int side) => _buildings.Count(b => b.Side == side && !b.Dead);

        /// <summary>Total damage dealt to one side's buildings — the at-time tiebreaker.</summary>
        public float DamageTaken(int side) => _buildings.Where(b => b.Side == side).Sum(b => b.DamageTaken);

        /// <summary>True when every one of a side's buildings is destroyed: an instant loss.</summary>
        public bool AllDestroyed(int side) => StandingCount(side) == 0 && _buildings.Any(b => b.Side == side);

        /// <summary>
        /// The routable hexes ringing a building — the goal set for a unit coming to attack
        /// it. Ports <c>bldAdjacent</c>: deduplicated, and only hexes that pass route
        /// legality, since a goal nothing can stand on is useless.
        /// </summary>
        /// <remarks>
        /// The building's own two hexes are excluded automatically: while it stands, its
        /// hexes fail route legality. Once destroyed they become routable and appear here,
        /// which is correct — rubble can be walked over.
        /// </remarks>
        public IEnumerable<Hex> AdjacentRoutableHexes(Building b, bool flying, TerrainMap terrain)
        {
            if (b == null) throw new System.ArgumentNullException(nameof(b));
            if (terrain == null) throw new System.ArgumentNullException(nameof(terrain));

            var seen = new HashSet<int>();
            foreach (var h in b.Hexes())
                foreach (var n in h.Neighbors())
                {
                    if (!seen.Add(Board.Index(n))) continue;
                    if (terrain.RouteOk(n, flying, RouteBlocker)) yield return n;
                }
        }

        public void ResetAll()
        {
            foreach (var b in _buildings) b.Reset();
        }
    }
}
