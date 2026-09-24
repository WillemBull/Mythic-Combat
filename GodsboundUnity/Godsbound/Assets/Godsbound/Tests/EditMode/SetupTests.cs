using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.AI;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;
using Godsbound.Core.Match;
using Godsbound.Core.Setup;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// U32: the setup board — painting a half, placing its buildings, the AI's layouts and the
    /// composition of the two halves, against <c>setup_reference.json</c>
    /// (<c>node tools/export_unity_setup.js</c>).
    /// </summary>
    public class SetupTests
    {
        [Serializable] public class HexRow { public int c, r; public Hex Hex => new Hex(c, r); }
        [Serializable] public class PairRow { public int c0, r0, c1, r1; public bool ok; }
        [Serializable] public class BuildingRow { public string type; public int c0, r0, c1, r1; }
        [Serializable] public class FreshHalf { public string faction; public string[] rows; }
        [Serializable] public class PlacementBoard
        {
            public string name, faction, moving; public string[] rows;
            public BuildingRow[] buildings; public PairRow[] pairs;
        }
        [Serializable] public class PaintStep { public int c, r; public string type; public bool ok; }
        [Serializable] public class PaintRun
        {
            public string name, faction; public PaintStep[] steps; public string[] rows;
            public BuildingRow[] buildings; public int[] painted;
        }
        [Serializable] public class LayoutCheck
        {
            public string name; public bool valid, complete; public PairRow city, temple, fortress;
        }
        [Serializable] public class TerrainCheck { public string name; public bool valid; public HexRow[] F, M, W; }
        [Serializable] public class Composition
        {
            public string player, ai; public string[] playerRows, aiRows, absolute; public BuildingRow[] buildings;
        }
        [Serializable] public class BudgetRow { public string code; public int max; }
        [Serializable] public class Fixture
        {
            public int aiRows, playerRows, cols;
            public BudgetRow[] budget;
            public FreshHalf[] fresh; public PlacementBoard[] placement; public PaintRun[] paint;
            public LayoutCheck[] layoutChecks; public TerrainCheck[] terrainChecks; public Composition[] compose;
        }

        private GameDatabase db;
        private AiData ai;
        private Fixture fx;

        [OneTimeSetUp] public void Load()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
            ai = AiDataLoader.Load();
            fx = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/setup_reference.json")));
        }

        private static DeckBuilding[] Buildings(BuildingRow[] rows) =>
            rows.Select(b => new DeckBuilding { type = b.type, c0 = b.c0, r0 = b.r0, c1 = b.c1, r1 = b.r1 }).ToArray();

        private static BoardHalf Half(string faction, BuildingRow[] buildings) =>
            new BoardHalf(faction, Buildings(buildings));

        /// <summary>A half exactly as a fixture describes it, through the same loader a saved deck uses.</summary>
        private static BoardHalf Loaded(string faction, string[] rows, BuildingRow[] buildings) =>
            BoardHalf.FromPreset(new DeckPreset
            { faction = faction, terrain = (string[])rows.Clone(), buildings = Buildings(buildings) });

        private static AiLayoutData Layout(PairRow city, PairRow temple, PairRow fortress) => new AiLayoutData
        {
            city = new HexPairData { c0 = city.c0, r0 = city.r0, c1 = city.c1, r1 = city.r1 },
            temple = new HexPairData { c0 = temple.c0, r0 = temple.r0, c1 = temple.c1, r1 = temple.r1 },
            fortress = new HexPairData { c0 = fortress.c0, r0 = fortress.r0, c1 = fortress.c1, r1 = fortress.r1 }
        };

        private static AiLayout.TerrainPlan Plan(TerrainCheck c)
        {
            var plan = new AiLayout.TerrainPlan();
            foreach (var pair in new[] { ("F", c.F), ("M", c.M), ("W", c.W) })
                foreach (var h in pair.Item2) plan.Of(pair.Item1).Add(h.Hex);
            return plan;
        }

        [Test] public void TheBoardAgreesWithTheBrowserAboutItsOwnShape()
        {
            Assert.That(fx.aiRows, Is.EqualTo(Board.AiRows));
            Assert.That(fx.playerRows, Is.EqualTo(Board.PlayerRows));
            Assert.That(fx.cols, Is.EqualTo(Board.Cols));
            foreach (var row in fx.budget) Assert.That(db.TerrainBudget(row.code[0]), Is.EqualTo(row.max), row.code);
        }

        [Test] public void AFreshHalfIsTheFactionsOwnGround()
        {
            Assert.That(fx.fresh.Length, Is.GreaterThan(3));
            foreach (var half in fx.fresh)
                Assert.That(new BoardHalf(half.faction).RowStrings(), Is.EqualTo(half.rows), half.faction);
        }

        [Test] public void EveryPlacementAnswerMatchesTheBrowser()
        {
            Assert.That(fx.placement.Sum(b => b.pairs.Length), Is.GreaterThan(500));
            foreach (var board in fx.placement)
            {
                var half = Loaded(board.faction, board.rows, board.buildings);
                var moving = half.Buildings.Single(b => b.type == board.moving);
                foreach (var p in board.pairs)
                    Assert.That(BoardSetup.CanPlaceBuilding(half, moving, new Hex(p.c0, p.r0), new Hex(p.c1, p.r1)),
                        Is.EqualTo(p.ok), $"{board.name}: {p.c0},{p.r0} -> {p.c1},{p.r1}");
            }
        }

        [Test] public void EveryPaintRunEndsWhereTheBrowserEndsIt()
        {
            Assert.That(fx.paint.Length, Is.GreaterThan(5));
            foreach (var run in fx.paint)
            {
                var half = Half(run.faction, run.buildings);
                foreach (var step in run.steps)
                {
                    bool ok = BoardSetup.TryPaint(half, db, new Hex(step.c, step.r), step.type[0], out var problem);
                    Assert.That(ok, Is.EqualTo(step.ok),
                        $"{run.name}: {step.type} at {step.c},{step.r}" + (ok ? "" : " — " + problem));
                    if (!ok) Assert.That(problem, Is.Not.Null.And.Not.Empty, run.name + " needs a reason");
                }
                Assert.That(half.RowStrings(), Is.EqualTo(run.rows), run.name + " terrain");
                Assert.That(new[] { half.PaintedCount('F'), half.PaintedCount('M'), half.PaintedCount('W') },
                    Is.EqualTo(run.painted), run.name + " budget spent");
            }
        }

        [Test] public void EveryAiLayoutIsJudgedAsTheBrowserJudgesIt()
        {
            Assert.That(fx.layoutChecks.Length, Is.GreaterThan(5));
            var map = new TerrainMap();
            foreach (var check in fx.layoutChecks)
            {
                // The two blocking-terrain cases are the shipped layout under painted rock and water.
                var terrain = new TerrainMap();
                if (check.name.Contains("water")) terrain[4, 1] = TerrainType.Water;
                if (check.name.Contains("rock")) terrain[4, 1] = TerrainType.Mountain;
                var layout = check.complete ? Layout(check.city, check.temple, check.fortress)
                    : new AiLayoutData { city = new HexPairData { c0 = check.city.c0, r0 = check.city.r0, c1 = check.city.c1, r1 = check.city.r1 } };
                Assert.That(AiLayout.Valid(layout, h => terrain[h.C, h.R]), Is.EqualTo(check.valid), check.name);
            }
            Assert.That(ai.layouts.Length, Is.EqualTo(3), "the three shipped arrangements are exported");
            foreach (var shipped in ai.layouts)
                Assert.That(AiLayout.Valid(shipped, h => map[h.C, h.R]), Is.True, "a shipped layout is legal");
        }

        [Test] public void EveryAiTerrainPlanIsJudgedAsTheBrowserJudgesIt()
        {
            Assert.That(fx.terrainChecks.Length, Is.GreaterThan(4));
            var buildings = AiLayout.Buildings(ai.layouts[0]).ToList();
            foreach (var check in fx.terrainChecks)
                Assert.That(AiLayout.PlanValid(Plan(check), db, ai, buildings, "china"),
                    Is.EqualTo(check.valid), check.name);
        }

        [Test] public void ComposingTheTwoHalvesMatchesTheBrowsersBoard()
        {
            Assert.That(fx.compose.Length, Is.GreaterThan(2));
            foreach (var c in fx.compose)
            {
                var local = Buildings(c.buildings)
                    .Select(b => new DeckBuilding(b.type, new Hex(b.c0, b.r0 - Board.PlayerRow0),
                                                          new Hex(b.c1, b.r1 - Board.PlayerRow0))).ToArray();
                var half = BoardHalf.FromPreset(new DeckPreset
                { faction = c.player, terrain = (string[])c.playerRows.Clone(), buildings = local });
                var map = new TerrainMap();
                BoardSetup.Compose(map, half, c.aiRows);
                for (int r = 0; r < Board.Rows; r++)
                {
                    var row = new char[Board.Cols];
                    for (int col = 0; col < Board.Cols; col++) row[col] = map.CodeAt(col, r);
                    Assert.That(new string(row), Is.EqualTo(c.absolute[r]), $"{c.player} v {c.ai} row {r}");
                }
                foreach (var b in half.Buildings)
                {
                    var want = c.buildings.Single(x => x.type == b.type);
                    Assert.That(BoardSetup.ToAbsolute(b.A), Is.EqualTo(new Hex(want.c0, want.r0)), b.type);
                    Assert.That(BoardSetup.ToAbsolute(b.B), Is.EqualTo(new Hex(want.c1, want.r1)), b.type);
                }
            }
        }

        [Test] public void AComposedMatchStandsOnBothHalvesTerrainAndBuildings()
        {
            var deck = DeckRules.Default(db, "greek");
            var half = BoardHalf.FromPreset(deck);
            Assert.That(BoardSetup.TryPaint(half, db, new Hex(0, 0), 'M'), Is.True);
            var layout = ai.layouts[2];
            var aiBuildings = AiLayout.Buildings(layout).ToList();
            var plan = AiLayout.GenerateTerrain(db, ai, aiBuildings, "china", Roll(3));
            var rows = AiLayout.Rows(ai, plan, "china");
            var terrain = new TerrainMap();
            var buildings = BoardSetup.Compose(terrain, db, half, rows, aiBuildings);

            Assert.That(terrain[0, Board.PlayerRow0], Is.EqualTo(TerrainType.Mountain), "the painted hex carried over");
            for (int c = 0; c < Board.Cols; c++)
                Assert.That(terrain[c, Board.AiRows], Is.EqualTo(TerrainType.Plains), "no-man's land is plain");
            for (int r = 0; r < Board.AiRows; r++)
                for (int c = 0; c < Board.Cols; c++)
                    Assert.That(terrain.CodeAt(c, r), Is.EqualTo(rows[r][c]), $"AI row {r}");

            Assert.That(buildings.All.Count, Is.EqualTo(6));
            foreach (var b in half.Buildings)
            {
                var placed = buildings.Of(0, Godsbound.Core.Buildings.Building.ParseType(b.type));
                Assert.That(placed.HexA, Is.EqualTo(BoardSetup.ToAbsolute(b.A)), b.type);
                Assert.That(placed.MaxHp, Is.GreaterThan(0f), b.type + " keeps its health");
            }
            foreach (var b in aiBuildings)
                Assert.That(buildings.Of(1, Godsbound.Core.Buildings.Building.ParseType(b.type)).HexA,
                    Is.EqualTo(b.A), "the AI's " + b.type);
            // A match built on this setup runs like any other.
            var state = new MatchState(db, terrain: terrain, buildings: buildings);
            Assert.That(new MatchLoop().Tick(state, 0.05f), Is.Null, "the composed match ticks");
        }

        [Test] public void AGeneratedAiHalfIsLegalAndTheSameForTheSameSeed()
        {
            foreach (var faction in new[] { "egypt", "china" })
                foreach (var index in new[] { 0, 1, 2 })
                {
                    var buildings = AiLayout.Buildings(ai.layouts[index]).ToList();
                    var first = AiLayout.GenerateTerrain(db, ai, buildings, faction, Roll(7));
                    var again = AiLayout.GenerateTerrain(db, ai, buildings, faction, Roll(7));
                    string label = $"{faction} layout {index}";
                    foreach (var code in new[] { "F", "M", "W" })
                    {
                        Assert.That(first.Of(code).Count, Is.EqualTo(db.TerrainBudget(code[0])), label + " " + code);
                        Assert.That(first.Of(code), Is.EqualTo(again.Of(code)), label + " is deterministic");
                    }
                    Assert.That(AiLayout.PlanValid(first, db, ai, buildings, faction), Is.True, label + " is legal");
                    var blocked = AiLayout.Blocked(buildings);
                    foreach (var code in new[] { "F", "M", "W" })
                        foreach (var h in first.Of(code))
                        {
                            Assert.That(h.R, Is.GreaterThan(0), label + ": row 0 is off limits");
                            Assert.That(blocked.Contains(h), Is.False, label + ": a doorway was painted over");
                        }
                }
        }

        [Test] public void TheFallbackHalfNeverPaintsOverABuilding()
        {
            foreach (var layout in ai.layouts)
            {
                var buildings = AiLayout.Buildings(layout).ToList();
                var plan = AiLayout.Fallback(ai, buildings);
                var occupied = buildings.SelectMany(b => new[] { b.A, b.B }).ToHashSet();
                foreach (var code in new[] { "F", "M", "W" })
                    foreach (var h in plan.Of(code))
                        Assert.That(occupied.Contains(h), Is.False, "the fallback painted under a building");
            }
        }

        [Test] public void PickingALayoutWalksTheWholeShelf()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var roll in new[] { 0.0, 0.34, 0.5, 0.99 })
                seen.Add(AiLayout.Pick(ai, () => roll));
            Assert.That(seen, Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(AiLayout.Pick(ai, () => 0.999), Is.LessThan(ai.layouts.Length), "never off the end");
        }

        /// <summary>A small deterministic roll, so a "seed" means the same half twice.</summary>
        private static Func<double> Roll(int seed)
        {
            long state = seed;
            return () => { state = (state * 1103515245 + 12345) % 2147483648L; return state / 2147483648.0; };
        }
    }
}
