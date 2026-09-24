using System.Collections.Generic;
using Godsbound.Core.Data;

namespace Godsbound.Core.Pathfinding
{
    /// <summary>
    /// Least-cost routing over the hex board, ported from <c>findPath(start, goals, flying)</c>.
    /// </summary>
    /// <remarks>
    /// <para>Dijkstra, with edge cost <c>1 / max(terrainSpeed, speedFloor)</c> plus a small
    /// random jitter. Flying movers pay a flat cost of 1 everywhere and ignore terrain.</para>
    /// <para><b>Three behaviours that look like bugs and are not</b>, all faithful to the
    /// browser game:</para>
    /// <list type="number">
    /// <item><description>The START hex is never route-checked. A unit standing on a
    /// building (emergency placement puts them there) or otherwise illegally placed can
    /// still path out. Checking it would strand those units.</description></item>
    /// <item><description>The goal test happens when a node is POPPED, not when pushed, and
    /// a goal equal to the start returns a single-hex path without any legality check at
    /// all.</description></item>
    /// <item><description>Edge cost is charged for the DESTINATION hex's terrain, not the
    /// one being left.</description></item>
    /// </list>
    /// <para>The jitter makes results non-deterministic by design, so <b>never assert an
    /// exact path</b>. Pass a seeded <see cref="System.Random"/> for reproducibility, or
    /// <see cref="WithoutJitter"/> to test the deterministic core.</para>
    /// <para>The frontier is scanned linearly rather than kept in a heap, matching the
    /// browser's <c>pq</c>. On a 117-cell board that is free, and it preserves the
    /// first-minimum-wins tie-break the original has — which is visible whenever jitter is
    /// disabled.</para>
    /// </remarks>
    public sealed class PathFinder
    {
        private readonly TerrainMap _terrain;
        private readonly StandingBuildingAt _buildingAt;
        private readonly PathfindingRates _rates;
        private readonly System.Random _rng;
        private readonly bool _jitter;

        public PathFinder(TerrainMap terrain, StandingBuildingAt buildingAt,
                          PathfindingRates rates, System.Random rng = null, bool jitter = true)
        {
            _terrain = terrain ?? throw new System.ArgumentNullException(nameof(terrain));
            _rates = rates ?? throw new System.ArgumentNullException(nameof(rates));

            if (_rates.speedFloor <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(rates),
                    "speedFloor must be positive, or edge costs can divide by zero");

            _buildingAt = buildingAt;
            _rng = rng ?? new System.Random();
            _jitter = jitter;
        }

        /// <summary>A finder with jitter off — deterministic, for tests and diagnostics.</summary>
        public PathFinder WithoutJitter() =>
            new PathFinder(_terrain, _buildingAt, _rates, _rng, jitter: false);

        /// <summary>
        /// Cost of stepping ONTO <paramref name="to"/>. Jitter excluded so it can be
        /// reasoned about and asserted.
        /// </summary>
        public float StepCost(Hex to, bool flying)
        {
            float speed = flying ? 1f : TerrainTable.Info(_terrain[to]).Speed;
            if (speed < _rates.speedFloor) speed = _rates.speedFloor;
            return 1f / speed;
        }

        /// <summary>
        /// Least-cost route from <paramref name="start"/> to the nearest of
        /// <paramref name="goals"/>, inclusive of both ends, or <c>null</c> if none is
        /// reachable.
        /// </summary>
        public List<Hex> FindPath(Hex start, IEnumerable<Hex> goals, bool flying)
        {
            if (!Board.InBounds(start)) return null;

            var goalSet = new HashSet<int>();
            if (goals != null)
                foreach (var g in goals)
                    if (Board.InBounds(g)) goalSet.Add(Board.Index(g));

            if (goalSet.Count == 0) return null;

            var dist = new Dictionary<int, float>();
            var prev = new Dictionary<int, int>();
            var frontier = new List<(float cost, Hex hex)>();

            int startKey = Board.Index(start);
            dist[startKey] = 0f;
            frontier.Add((0f, start));

            while (frontier.Count > 0)
            {
                // First minimum wins, as in the browser's linear scan.
                int best = 0;
                for (int i = 1; i < frontier.Count; i++)
                    if (frontier[i].cost < frontier[best].cost) best = i;

                var (d, h) = frontier[best];
                frontier.RemoveAt(best);

                int k = Board.Index(h);

                // Stale queue entry: a cheaper route to this hex was found after it was pushed.
                if (dist.TryGetValue(k, out var known) && d > known) continue;

                if (goalSet.Contains(k)) return Reconstruct(prev, k);

                foreach (var n in h.Neighbors())
                {
                    if (!_terrain.RouteOk(n, flying, _buildingAt)) continue;

                    float nd = d + StepCost(n, flying);
                    if (_jitter) nd += (float)_rng.NextDouble() * _rates.edgeJitter;

                    int nk = Board.Index(n);
                    if (!dist.TryGetValue(nk, out var cur) || nd < cur)
                    {
                        dist[nk] = nd;
                        prev[nk] = k;
                        frontier.Add((nd, n));
                    }
                }
            }

            return null;
        }

        /// <summary>Convenience overload for a single goal.</summary>
        public List<Hex> FindPath(Hex start, Hex goal, bool flying) =>
            FindPath(start, new[] { goal }, flying);

        private static List<Hex> Reconstruct(Dictionary<int, int> prev, int goalKey)
        {
            var path = new List<Hex>();
            int cur = goalKey;
            var guard = new HashSet<int>();

            while (true)
            {
                if (!guard.Add(cur))
                    throw new System.InvalidOperationException("cycle in the path predecessors");

                path.Add(new Hex(cur % Board.Cols, cur / Board.Cols));
                if (!prev.TryGetValue(cur, out var p)) break;
                cur = p;
            }

            path.Reverse();
            return path;
        }
    }
}
