using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.AI;
using Godsbound.Core.Decks;

namespace Godsbound.Core.Setup
{
    /// <summary>
    /// The AI's own half: which of the shipped building arrangements it fights behind this match, and
    /// the terrain it funnels the player through. Ports <c>validAiLayout</c>, <c>pickAiLayout</c>,
    /// <c>aiTerrainLayoutValid</c> and <c>generateAiTerrain</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Ordering is load-bearing, as in the browser:</b> the building layout is applied BEFORE
    /// the terrain is generated, because the generator reads the buildings live — it keeps their
    /// doorways clear and validates that each can still march out. Generating first would scatter
    /// terrain against the previous match's layout.</para>
    /// <para>The generator is NOT replayable against the browser: it shuffles with a comparator whose
    /// result is engine-specific, and validates through findPath's jitter. This port keeps its shape,
    /// its measured weights and its constraints, draws from an injected <c>random</c> so a seed gives
    /// the same half twice, and is verified by its PROPERTIES — budget, blocked hexes, row 0 left
    /// alone, every building able to exit, the player able to enter.</para>
    /// </remarks>
    public static class AiLayout
    {
        public const int GateRowOffset = 1; // the AI's own gate row is AiRows - 1
        private static readonly string[] Types = { "city", "temple", "fortress" };
        private static readonly string[] Paintable = { "F", "M", "W" };

        /// <summary>A generated half: which hexes take which feature.</summary>
        public sealed class TerrainPlan
        {
            public readonly Dictionary<string, List<Hex>> Hexes = new Dictionary<string, List<Hex>>
            { { "F", new List<Hex>() }, { "M", new List<Hex>() }, { "W", new List<Hex>() } };
            public List<Hex> Of(string code) => Hexes[code];
            public int Count(string code) => Hexes[code].Count;
        }

        /// <summary>The three buildings a layout places, as half-local hexes in the AI's own rows.</summary>
        public static IEnumerable<DeckBuilding> Buildings(AiLayoutData layout) =>
            Types.Select(t => new DeckBuilding(t, layout.Of(t).A, layout.Of(t).B));

        /// <summary>
        /// <c>validAiLayout</c>: three complete footprints, in the AI's own rows, adjacent, never on
        /// blocking terrain and never sharing a hex. Mirrors <c>canPlaceBuilding</c>'s rules for the
        /// half the player cannot edit.
        /// </summary>
        public static bool Valid(AiLayoutData layout, Func<Hex, TerrainType> terrain)
        {
            if (layout == null) return false;
            var seen = new HashSet<Hex>();
            foreach (var type in Types)
            {
                var pair = layout.Of(type);
                if (pair == null) return false;
                foreach (var h in new[] { pair.A, pair.B })
                {
                    if (h.C < 0 || h.C >= Board.Cols || h.R < 0 || h.R >= Board.AiRows) return false;
                    var t = terrain(h);
                    if (t == TerrainType.Mountain || t == TerrainType.Water) return false;
                    if (!seen.Add(h)) return false;
                }
                if (Hex.Distance(pair.A, pair.B) != 1) return false;
            }
            return true;
        }

        /// <summary>Re-roll the arrangement. Paired with a terrain regeneration at every call site.</summary>
        public static int Pick(AiData data, Func<double> random) =>
            (int)(random() * data.layouts.Length);

        /// <summary>Hexes the generator leaves alone: the buildings and everything beside them.</summary>
        public static HashSet<Hex> Blocked(IEnumerable<DeckBuilding> buildings)
        {
            var blocked = new HashSet<Hex>();
            foreach (var b in buildings)
                foreach (var h in new[] { b.A, b.B })
                {
                    blocked.Add(h);
                    foreach (var n in h.Neighbors()) blocked.Add(n);
                }
            return blocked;
        }

        /// <summary>The AI's half as terrain rows, with a plan painted onto the shipped ground.</summary>
        public static string[] Rows(AiData data, TerrainPlan plan, string faction)
        {
            var rows = new char[Board.AiRows][];
            for (int r = 0; r < Board.AiRows; r++)
            {
                rows[r] = new char[Board.Cols];
                for (int c = 0; c < Board.Cols; c++)
                {
                    char shipped = TerrainMap.Initial[r][c];
                    char filler = faction == "egypt" ? 'D' : 'P';
                    rows[r][c] = shipped == 'P' || shipped == 'D' ? filler : shipped;
                }
            }
            foreach (var code in Paintable)
                foreach (var h in plan.Of(code)) rows[h.R][h.C] = code[0];
            return rows.Select(r => new string(r)).ToArray();
        }

        /// <summary>
        /// <c>aiTerrainLayoutValid</c>'s shape rules: the full budget of each feature, nothing in row
        /// 0, nothing off the half, nothing on a blocked hex and nothing twice. The route half of the
        /// browser's validator is <see cref="RoutesHold"/>.
        /// </summary>
        public static bool ShapeHolds(TerrainPlan plan, Data.GameDatabase db, HashSet<Hex> blocked)
        {
            foreach (var code in Paintable)
                if (plan.Count(code) != db.TerrainBudget(code[0])) return false;
            var used = new HashSet<Hex>();
            foreach (var code in Paintable)
                foreach (var h in plan.Of(code))
                {
                    if (h.R <= 0 || h.R >= Board.AiRows || h.C < 0 || h.C >= Board.Cols) return false;
                    if (blocked.Contains(h) || !used.Add(h)) return false;
                }
            return true;
        }

        /// <summary>
        /// The other half of the browser's validator: every AI building can still reach its own gate
        /// row, and the player can still reach some AI building from no-man's land.
        /// </summary>
        public static bool RoutesHold(TerrainPlan plan, AiData data, IReadOnlyList<DeckBuilding> buildings, string faction)
        {
            var rows = Rows(data, plan, faction);
            bool Open(Hex h) =>
                h.C >= 0 && h.C < Board.Cols && h.R >= 0 && h.R < Board.AiRows &&
                TerrainTable.FromCode(rows[h.R][h.C]) != TerrainType.Mountain &&
                !buildings.Any(b => b.A == h || b.B == h);

            IEnumerable<Hex> Doorways(DeckBuilding b)
            {
                var seen = new HashSet<Hex>();
                foreach (var hex in new[] { b.A, b.B })
                    foreach (var n in hex.Neighbors())
                        if (Open(n) && seen.Add(n)) yield return n;
            }

            bool Reaches(Hex from, Func<Hex, bool> arrived)
            {
                if (!Open(from)) return false;
                var seen = new HashSet<Hex> { from };
                var queue = new Queue<Hex>();
                queue.Enqueue(from);
                while (queue.Count > 0)
                {
                    var h = queue.Dequeue();
                    if (arrived(h)) return true;
                    foreach (var n in h.Neighbors())
                        if (Open(n) && seen.Add(n)) queue.Enqueue(n);
                }
                return false;
            }

            int gate = Board.AiRows - GateRowOffset;
            foreach (var b in buildings)
                if (!Doorways(b).Any(h => Reaches(h, x => x.R == gate && x.C == data.terrain.gateCol))) return false;
            // The player enters along the AI's own gate row, which is where no-man's land meets it.
            var entrances = Enumerable.Range(0, Board.Cols).Select(c => new Hex(c, gate));
            return entrances.Any(start => buildings.Any(b =>
                Doorways(b).Any(door => Reaches(start, x => x == door))));
        }

        /// <summary>Both halves of <c>aiTerrainLayoutValid</c>.</summary>
        public static bool PlanValid(TerrainPlan plan, Data.GameDatabase db, AiData data,
                                     IReadOnlyList<DeckBuilding> buildings, string faction) =>
            ShapeHolds(plan, db, Blocked(buildings)) && RoutesHold(plan, data, buildings, faction);

        /// <summary>
        /// <c>generateAiTerrain</c>: pick a lane to leave open, wall the other two with rock, lay the
        /// faction's water (Egypt's one river, everyone else's deep pools off the protected routes),
        /// then fill in forest near the buildings and the rock. Falls back to the hand-authored layout
        /// when no attempt validates, with anything that would land ON a building dropped.
        /// </summary>
        public static TerrainPlan GenerateTerrain(Data.GameDatabase db, AiData data,
                                                  IReadOnlyList<DeckBuilding> buildings, string faction,
                                                  Func<double> random)
        {
            var rates = data.terrain;
            var blocked = Blocked(buildings);
            var lanes = rates.lanes.Select(l => l.cols).ToArray();
            for (int attempt = 0; attempt < rates.attempts; attempt++)
            {
                int openLane = (int)(random() * lanes.Length);
                var plan = new TerrainPlan();
                var used = new HashSet<Hex>();
                bool Legal(Hex h) => h.R > 0 && h.R < Board.AiRows && h.C >= 0 && h.C < Board.Cols &&
                                     !blocked.Contains(h) && !used.Contains(h);

                var rock = new List<(Hex h, double score)>();
                for (int r = rates.mountainRowMin; r <= rates.mountainRowMax; r++)
                    for (int c = 0; c < Board.Cols; c++)
                    {
                        var h = new Hex(c, r);
                        if (!Legal(h)) continue;
                        int lane = Array.FindIndex(lanes, cols => cols.Contains(c));
                        double score = (lane != openLane ? rates.mountainLaneBonus : 0) +
                                       (r == rates.mountainMidRow ? rates.mountainMidBonus : 0) +
                                       random() * rates.mountainJitter;
                        rock.Add((h, score));
                    }
                Take(rock, plan.Of("M"), used, db.TerrainBudget('M'));
                if (plan.Count("M") != db.TerrainBudget('M')) continue;

                if (faction == "egypt")
                {
                    var starts = new List<(Hex h, double score)>();
                    for (int c = 0; c < Board.Cols; c++)
                        for (int r = rates.nileStartRowMin; r <= rates.nileStartRowMax; r++)
                        {
                            var h = new Hex(c, r);
                            if (!Legal(h)) continue;
                            starts.Add((h, (lanes[openLane].Contains(c) ? rates.nileStartLaneBonus : 0) +
                                            random() * rates.nileStartJitter));
                        }
                    if (starts.Count == 0) continue;
                    var first = starts.OrderByDescending(x => x.score).First().h;
                    plan.Of("W").Add(first); used.Add(first);
                    while (plan.Count("W") < db.TerrainBudget('W'))
                    {
                        var river = plan.Of("W");
                        var options = river.SelectMany(h => h.Neighbors())
                            .Where(h => Legal(h) && WaterLineOk(h, river)).Distinct().ToList();
                        Shuffle(options, random);
                        var next = options
                            .OrderByDescending(h => lanes[openLane].Contains(h.C) ? 1 : 0)
                            .Select(h => (Hex?)h).FirstOrDefault();
                        if (!next.HasValue) break;
                        river.Add(next.Value);
                        used.Add(next.Value);
                    }
                }
                else
                {
                    var protectedRoutes = ProtectedRoutes(data, plan, buildings, faction);
                    var pools = new List<(Hex h, double score)>();
                    for (int r = rates.waterRowMin; r < Board.AiRows; r++)
                        for (int c = 0; c < Board.Cols; c++)
                        {
                            var h = new Hex(c, r);
                            if (!Legal(h) || protectedRoutes.Contains(h)) continue;
                            pools.Add((h, (r >= rates.waterDeepRow ? rates.waterDeepBonus : 0) +
                                           (lanes[openLane].Contains(c) ? rates.waterLaneBonus : 0) +
                                           random() * rates.waterJitter));
                        }
                    Take(pools, plan.Of("W"), used, db.TerrainBudget('W'));
                }
                if (plan.Count("W") != db.TerrainBudget('W')) continue;

                var trees = new List<(Hex h, double score)>();
                for (int r = rates.forestRowMin; r < Board.AiRows; r++)
                    for (int c = 0; c < Board.Cols; c++)
                    {
                        var h = new Hex(c, r);
                        if (!Legal(h)) continue;
                        bool nearBuilding = buildings.Any(b => new[] { b.A, b.B }
                            .Any(x => Hex.Distance(h, x) <= rates.forestNearBuildingRange));
                        bool nearRock = plan.Of("M").Any(m => Hex.Distance(h, m) == 1);
                        bool funnel = faction == "china" && lanes[openLane].Contains(c);
                        trees.Add((h, (nearBuilding ? rates.forestNearBuildingBonus : 0) +
                                       (nearRock ? rates.forestNearMountainBonus : 0) +
                                       (funnel ? rates.forestFunnelBonus : 0) +
                                       random() * rates.forestJitter));
                    }
                Take(trees, plan.Of("F"), used, db.TerrainBudget('F'));
                if (plan.Count("F") == db.TerrainBudget('F') && PlanValid(plan, db, data, buildings, faction))
                    return plan;
            }
            return Fallback(data, buildings);
        }

        /// <summary>
        /// The hand-authored half, with anything that would land ON a building dropped — filtered by
        /// building hexes only, NOT by the generator's spawn-clearance preference, which would prune
        /// this rare path far too hard.
        /// </summary>
        public static TerrainPlan Fallback(AiData data, IEnumerable<DeckBuilding> buildings)
        {
            var occupied = new HashSet<Hex>(buildings.SelectMany(b => new[] { b.A, b.B }));
            var plan = new TerrainPlan();
            foreach (var code in Paintable)
                foreach (var h in data.terrain.Fallback(code))
                    if (!occupied.Contains(h.Hex)) plan.Of(code).Add(h.Hex);
            return plan;
        }

        private static void Take(List<(Hex h, double score)> pool, List<Hex> into, HashSet<Hex> used, int count)
        {
            foreach (var entry in pool.OrderByDescending(x => x.score).Take(count))
            {
                into.Add(entry.h);
                used.Add(entry.h);
            }
        }

        /// <summary>
        /// The shortest way out of each AI building, which the water pass leaves dry so the AI is
        /// never flooded into its own doorway.
        /// </summary>
        private static HashSet<Hex> ProtectedRoutes(AiData data, TerrainPlan plan,
                                                    IReadOnlyList<DeckBuilding> buildings, string faction)
        {
            var rows = Rows(data, plan, faction);
            bool Open(Hex h) => h.C >= 0 && h.C < Board.Cols && h.R >= 0 && h.R < Board.AiRows &&
                                TerrainTable.FromCode(rows[h.R][h.C]) != TerrainType.Mountain &&
                                !buildings.Any(b => b.A == h || b.B == h);
            var gate = new Hex(data.terrain.gateCol, Board.AiRows - GateRowOffset);
            var keep = new HashSet<Hex>();
            foreach (var b in buildings)
            {
                List<Hex> best = null;
                foreach (var hex in new[] { b.A, b.B })
                    foreach (var door in hex.Neighbors())
                    {
                        if (!Open(door)) continue;
                        var path = ShortestPath(door, gate, Open);
                        if (path != null && (best == null || path.Count < best.Count)) best = path;
                    }
                if (best != null) foreach (var h in best) keep.Add(h);
            }
            return keep;
        }

        private static List<Hex> ShortestPath(Hex from, Hex to, Func<Hex, bool> open)
        {
            if (!open(from)) return null;
            var came = new Dictionary<Hex, Hex>();
            var seen = new HashSet<Hex> { from };
            var queue = new Queue<Hex>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (h == to)
                {
                    var path = new List<Hex> { h };
                    while (came.TryGetValue(path[path.Count - 1], out var previous)) path.Add(previous);
                    path.Reverse();
                    return path;
                }
                foreach (var n in h.Neighbors())
                {
                    if (!open(n) || !seen.Add(n)) continue;
                    came[n] = h;
                    queue.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary><c>aiWaterLineOK</c>: the AI's river obeys the same one-hex-wide rule Egypt's does.</summary>
        public static bool WaterLineOk(Hex at, IReadOnlyList<Hex> placed)
        {
            if (placed.Count == 0) return true;
            var wet = new HashSet<Hex>(placed);
            int Degree(Hex h) => h.Neighbors().Count(wet.Contains);
            var touching = at.Neighbors().Where(wet.Contains).ToList();
            if (touching.Count == 0 || touching.Count > 2) return false;
            return touching.All(h => Degree(h) < 2);
        }

        /// <summary>
        /// Fisher-Yates where the browser sorts with <c>() =&gt; Math.random() - 0.5</c>. That
        /// comparator's result depends on the JS engine's sort, so it cannot be reproduced here; both
        /// are "some order nobody can predict", which is all the river's next hex asks for.
        /// </summary>
        private static void Shuffle<T>(IList<T> list, Func<double> random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)(random() * (i + 1));
                var swap = list[i]; list[i] = list[j]; list[j] = swap;
            }
        }
    }
}
