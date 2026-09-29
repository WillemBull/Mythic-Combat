using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;

namespace Godsbound.Core.Setup
{
    /// <summary>
    /// The rules of the setup board: where a building may stand, what may be painted where, and how
    /// the two halves become one arena. Ports <c>canPlaceBuilding</c>, <c>paintTerrain</c> and
    /// <c>composeAbsoluteTMAP</c>/<c>composeAbsoluteBuildings</c>.
    /// </summary>
    /// <remarks>
    /// Reachability is answered by a breadth-first search rather than by <c>findPath</c>. The browser
    /// asks findPath only whether a route EXISTS, and findPath's tie-breaking jitter changes which
    /// route it returns but never whether one exists — so a BFS gives the same answer and gives it
    /// deterministically, which a setup rule the player is arguing with should.
    /// </remarks>
    public static class BoardSetup
    {
        /// <summary><c>canPlaceBuilding</c>: two adjacent, in-half, unblocked, unoccupied hexes.</summary>
        public static bool CanPlaceBuilding(BoardHalf half, DeckBuilding moving, Hex a, Hex b)
        {
            if (!half.Contains(a) || !half.Contains(b)) return false;
            if (Hex.Distance(a, b) != 1) return false;
            foreach (var h in new[] { a, b })
            {
                var t = half.TerrainAt(h);
                if (t == TerrainType.Mountain || t == TerrainType.Water) return false;
                var occupant = half.At(h);
                if (occupant != null && occupant != moving) return false;
            }
            return true;
        }

        /// <summary>Move a building, if the two hexes will take it.</summary>
        public static bool TryMoveBuilding(BoardHalf half, string type, Hex a, Hex b)
        {
            var moving = half.Buildings.FirstOrDefault(x => x.type == type);
            if (moving == null || !CanPlaceBuilding(half, moving, a, b)) return false;
            moving.c0 = a.C; moving.r0 = a.R; moving.c1 = b.C; moving.r1 = b.R;
            return true;
        }

        /// <summary>Every building can still walk out to the gate row — <c>playerBldsReachGate</c>.</summary>
        public static bool BuildingsReachGate(BoardHalf half)
        {
            foreach (var b in half.Buildings)
                if (!Adjacent(half, b).Any(h => Reaches(half, h, 0))) return false;
            return true;
        }

        /// <summary>The routable hexes beside a building — <c>bldAdjacent(b, false)</c>.</summary>
        public static IEnumerable<Hex> Adjacent(BoardHalf half, DeckBuilding b)
        {
            var seen = new HashSet<Hex>();
            foreach (var hex in new[] { b.A, b.B })
                foreach (var n in hex.Neighbors())
                {
                    if (!half.Contains(n) || !seen.Add(n)) continue;
                    if (RouteOk(half, n)) yield return n;
                }
        }

        /// <summary><c>routeOK</c> inside a half: on the board, not blocking terrain, no building.</summary>
        public static bool RouteOk(BoardHalf half, Hex h) =>
            half.Contains(h) && half.TerrainAt(h) != TerrainType.Mountain && half.At(h) == null;

        /// <summary>Can this hex walk to any cell of <paramref name="gateRow"/>?</summary>
        private static bool Reaches(BoardHalf half, Hex from, int gateRow)
        {
            if (!RouteOk(half, from)) return false;
            var seen = new HashSet<Hex> { from };
            var queue = new Queue<Hex>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var h = queue.Dequeue();
                if (h.R == gateRow) return true;
                foreach (var n in h.Neighbors())
                {
                    if (!half.Contains(n) || !RouteOk(half, n) || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            return false;
        }

        /// <summary>
        /// Egypt's Nile may only be a single one-hex-wide line: never a blob, never a branch, never a
        /// second disconnected segment (<c>nileLineOK</c>). Every other faction paints water freely.
        /// </summary>
        public static bool NileLineOk(BoardHalf half, Hex at)
        {
            if (half.PaintedCount('W') == 0) return true; // the first hex of the river goes anywhere
            int Degree(Hex h) => h.Neighbors().Count(n => half.Contains(n) && half.PaintedAt(n) == 'W');
            var wet = at.Neighbors().Where(n => half.Contains(n) && half.PaintedAt(n) == 'W').ToList();
            if (wet.Count == 0 || wet.Count > 2) return false;
            return wet.All(n => Degree(n) < 2);
        }

        /// <summary>
        /// <c>paintTerrain</c>. 'P' erases back to the shipped ground; a paintable code is charged
        /// against its budget, refused if it would wall a building in (mountains) or break the Nile
        /// (Egypt's water), and otherwise stands.
        /// </summary>
        public static bool TryPaint(BoardHalf half, GameDatabase db, Hex at, char code, out string problem)
        {
            problem = null;
            if (!half.Contains(at)) { problem = $"{at} is not on this half"; return false; }
            if (half.At(at) != null) { problem = "a building stands there"; return false; }
            char previous = half.PaintedAt(at);

            // 'P' is the browser's erase instruction, not a brush — the palette only paints F, M
            // and W, so asking for plain ground can only mean "put this hex back". It was 'D' until
            // Desert was removed (2026-09-28); 'D' is still honoured so an old caller does not
            // silently paint instead of erasing.
            if (code == 'P' || code == 'D')
            {
                if (previous == '\0') { problem = "nothing was painted there"; return false; }
                half[at] = BoardHalf.FreshCode(half.Faction, at.C, at.R);
                half.SetPaint(at, '\0');
                return true;
            }

            int budget = db.TerrainBudget(code);
            if (budget == 0) { problem = $"'{code}' cannot be painted"; return false; }
            if (previous == code) return true; // already this, and it costs nothing more

            // The browser refunds the OLD type before charging the new one, which is why repainting
            // a forest hex as rock is judged against the rock budget alone. Repainting the same type
            // returned above, so the hex being repainted never counts against itself here.
            int spent = half.PaintedCount(code);
            if (spent >= budget) { problem = $"only {budget} of '{code}' fit in a half"; return false; }

            char before = half[at];
            half[at] = code;
            if (code == 'M' && !BuildingsReachGate(half))
            {
                half[at] = before;
                problem = "that mountain would wall a building in";
                return false;
            }
            if (code == 'W' && half.Faction == "egypt" && !NileLineOk(half, at))
            {
                half[at] = before;
                problem = "the Nile runs in one unbroken line";
                return false;
            }
            half.SetPaint(at, code);
            return true;
        }

        public static bool TryPaint(BoardHalf half, GameDatabase db, Hex at, char code) =>
            TryPaint(half, db, at, code, out _);

        /// <summary>
        /// <c>composeAbsoluteTMAP</c>: the AI's half on top, no-man's land plain, the player's half
        /// below. Both halves are written whole, so nothing of the shipped map survives inside them.
        /// </summary>
        public static void Compose(TerrainMap map, BoardHalf player, IReadOnlyList<string> aiHalf)
        {
            for (int r = 0; r < Board.AiRows; r++)
                for (int c = 0; c < Board.Cols; c++)
                    map[c, r] = TerrainTable.FromCode(aiHalf[r][c]);
            // No-man's land belongs to neither faction, so it never takes a faction's filler.
            for (int r = Board.AiRows; r < Board.PlayerRow0; r++)
                for (int c = 0; c < Board.Cols; c++)
                    map[c, r] = TerrainType.Plains;
            for (int r = 0; r < player.Rows; r++)
                for (int c = 0; c < Board.Cols; c++)
                    map[c, Board.PlayerRow0 + r] = TerrainTable.FromCode(player[c, r]);
        }

        /// <summary>A half-local hex in absolute board rows.</summary>
        public static Hex ToAbsolute(Hex local) => new Hex(local.C, local.R + Board.PlayerRow0);

        /// <summary>
        /// <c>composeAbsoluteBuildings</c>: the six buildings of a match, the player's half-local three
        /// lifted into absolute rows and the AI's three where its layout put them. Per-type health comes
        /// from the export, so a composed match is worth exactly what a default one is.
        /// </summary>
        public static Buildings.BuildingMap ComposeBuildings(GameDatabase db, BoardHalf player,
                                                            IEnumerable<DeckBuilding> aiBuildings)
        {
            var map = new Buildings.BuildingMap();
            foreach (var b in player.Buildings) map.Add(Raise(db, 0, b, ToAbsolute(b.A), ToAbsolute(b.B)));
            foreach (var b in aiBuildings) map.Add(Raise(db, 1, b, b.A, b.B));
            return map;
        }

        private static Buildings.Building Raise(GameDatabase db, int side, DeckBuilding b, Hex a, Hex z)
        {
            var type = db.BuildingTypes.FirstOrDefault(t => t.type == b.type)
                ?? throw new System.ArgumentException($"unknown building type '{b.type}'", nameof(b));
            return new Buildings.Building(side, Buildings.Building.ParseType(b.type), type.name, a, z, type.hp);
        }

        /// <summary>
        /// Everything a started match needs from setup: both halves' terrain and all six buildings.
        /// The mirrored default in <see cref="GameDatabase.Buildings"/> stays what a match built
        /// without setup uses, so every seeded test keeps the arena it was written against.
        /// </summary>
        public static Buildings.BuildingMap Compose(TerrainMap map, GameDatabase db, BoardHalf player,
                                                   IReadOnlyList<string> aiHalf, IEnumerable<DeckBuilding> aiBuildings)
        {
            Compose(map, player, aiHalf);
            return ComposeBuildings(db, player, aiBuildings);
        }
    }
}
