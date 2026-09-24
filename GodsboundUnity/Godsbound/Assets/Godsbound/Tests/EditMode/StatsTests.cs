using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.AI;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;
using Godsbound.Core.Economy;
using Godsbound.Core.Gods;
using Godsbound.Core.Match;
using Godsbound.Core.Movement;
using Godsbound.Core.Training;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>
    /// U34: the match counters (roadmap 7.1) and the screen that reports them (7.2). Every counter is
    /// asserted against events counted independently in the test, never against itself.
    /// </summary>
    public class StatsTests
    {
        private GameDatabase db;
        private GameObject root;

        [OneTimeSetUp] public void Load()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
        }

        [TearDown] public void Sweep()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            root = null;
        }

        private MatchState Match(string player = "egypt", string ai = "china")
        {
            Func<int, string> faction = side => side == 0 ? player : ai;
            var state = new MatchState(db, combatContext: new CombatContext { FactionForSide = faction },
                movementContext: new MovementContext { FactionForSide = faction },
                gods: new[] { GodState.Default(0, db, player), GodState.Default(1, db, ai) });
            foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
            return state;
        }

        [Test] public void TrainingCountsBodiesAndTheFavorTheyCost()
        {
            var state = Match();
            var city = state.Buildings.Of(0, Godsbound.Core.Buildings.BuildingType.City);
            var exit = state.Buildings.AdjacentRoutableHexes(city, false, state.Terrain).First();
            state.Resources[0] = new Purse(db.FoodCap, db.FavorCap);
            // Humans are bought with food and myths with favor, so the favor counter is measured
            // on a myth — the only thing that ever spends the scarce currency on a body.
            var myth = db.AllUnits.First(u => u.faction == "egypt" && u.costFavor > 0);
            float before = state.Resources[0].Favor;
            Assert.That(state.Training.TryEnqueue(state.Resources[0], 0, myth, exit, city,
                state.TrainingModifiersFor(0), state.Elapsed, out var remaining, out var order, new[] { exit }), Is.True);
            state.Resources[0] = remaining;
            Assert.That(state.Stats.FavorOnUnits[0], Is.EqualTo(before - remaining.Favor).Within(1e-3),
                "what left the purse is what the counter says");
            Assert.That(state.Stats.Trained[0], Is.EqualTo(0), "paying for a unit is not training one");
            var spawned = state.Training.Tick(order.ReadyAt, state.Units, state.Buildings);
            Assert.That(state.Stats.Trained[0], Is.EqualTo(spawned.Count));
            Assert.That(state.Stats.Trained[1], Is.EqualTo(0));
        }

        [Test] public void DeathsAreCountedForTheSideThatLostTheBody()
        {
            var state = Match();
            var mine = state.Units.Spawn(0, db.Unit("egypt", "spear"), new Hex(4, 8));
            var theirs = state.Units.Spawn(1, db.Unit("china", "ji"), new Hex(4, 4));
            state.Combat.KillUnit(mine, state.Elapsed);
            state.Combat.KillUnit(theirs, state.Elapsed);
            state.Combat.KillUnit(theirs, state.Elapsed); // already dead: not a second loss
            Assert.That(state.Stats.Lost, Is.EqualTo(new[] { 1, 1 }));
        }

        [Test] public void CastingCountsTheFavorItPaidEvenWhenNothingWasThere()
        {
            var state = Match();
            state.Objectives.Clock.Advance(30f);
            state.Resources[0] = new Purse(0f, 250f);
            state.Gods[0].Grant("horus");
            int cost = state.GodPowerCost(0, state.Gods[0].Slot("horus").Def);
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Paid, Is.True);
            Assert.That(state.Stats.FavorOnPowers[0], Is.EqualTo(cost).Within(1e-3), "an empty row still costs");
            // A refused cast pays nothing, so it counts nothing.
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Paid, Is.False, "still recharging");
            Assert.That(state.Stats.FavorOnPowers[0], Is.EqualTo(cost).Within(1e-3));
            Assert.That(state.Stats.FavorOnPowers[1], Is.EqualTo(0f));
        }

        [Test] public void AWholeSeededMatchCountsWhatActuallyHappened()
        {
            var state = Match();
            state.Ai = AiController.Create(state, AiDataLoader.Load(), "china", new System.Random(5));
            state.Resources[0] = new Purse(60f, 60f);
            state.Resources[1] = new Purse(60f, 60f);
            int spawns = 0, deaths = 0;
            state.Training.Spawned += _ => spawns++;
            state.Combat.UnitDied += _ => deaths++;
            var loop = new MatchLoop();
            for (int i = 0; i < 4000 && !state.Objectives.Resolved; i++) loop.Tick(state, MatchLoop.MaxStep);
            Assert.That(state.Stats.Trained.Sum(), Is.EqualTo(spawns), "every body that appeared was counted");
            Assert.That(state.Stats.Lost.Sum(), Is.EqualTo(deaths), "and every one that fell");
            Assert.That(spawns, Is.GreaterThan(0), "the AI did train something");
            state.Reset();
            Assert.That(state.Stats.Trained.Concat(state.Stats.Lost), Is.All.EqualTo(0), "a reset match starts at nothing");
            Assert.That(state.Stats.FavorOnPowers.Concat(state.Stats.FavorOnUnits), Is.All.EqualTo(0f));
        }

        [Test] public void TheEndScreenReportsWhatCoreSettledOn()
        {
            root = new GameObject("Match");
            var controller = root.AddComponent<MatchController>();
            controller.Initialize(enableAi: false, deck: DeckRules.Default(db, "greek"),
                                  storePath: System.IO.Path.GetTempFileName(), rng: new System.Random(1));
            var end = root.AddComponent<EndScreen>();
            end.Bind(controller);
            controller.Begin(); // nothing is settled while the match is still in setup
            var state = controller.State;
            Assert.That(state.Objectives.Resolved, Is.False);
            // Take the enemy's capital: an instant win, whatever the clock says.
            foreach (var b in state.Buildings.Of(1).ToArray()) b.TakeDamage(b.Hp);
            controller.Advance(MatchLoop.MaxStep);
            Assert.That(state.Objectives.Resolved, Is.True);
            var result = state.Objectives.Result.Value;
            Assert.That(EndScreen.Title(result.Outcome), Is.EqualTo("⚜ VICTORY ⚜"));
            Assert.That(EndScreen.Detail(result), Does.Contain("You: 3"), "three of theirs fell");
            Assert.That(EndScreen.Stats(state), Does.Contain("Units trained"));
            var panel = EndScreen.Panel(1080f, 1920f);
            Assert.That(panel.center.x, Is.EqualTo(540f).Within(0.01f));
            Assert.That(panel.width, Is.LessThanOrEqualTo(380f), "it fits a narrow phone");
        }

        [TestCase("egypt")] [TestCase("china")] [TestCase("greek")] [TestCase("aztec")]
        public void ASeededMatchOfEveryPantheonReachesAnEnding(string faction)
        {
            root = new GameObject("Match");
            var controller = root.AddComponent<MatchController>();
            controller.Initialize(deck: DeckRules.Default(db, faction),
                                  storePath: System.IO.Path.GetTempFileName(), rng: new System.Random(9));
            var end = root.AddComponent<EndScreen>();
            end.Bind(controller);
            controller.Begin();
            var state = controller.State;
            state.Resources[0] = new Purse(60f, 60f);
            state.Resources[1] = new Purse(60f, 60f);
            for (int i = 0; i < 5000 && !state.Objectives.Resolved; i++) controller.Advance(MatchLoop.MaxStep);
            Assert.That(state.Objectives.Resolved, Is.True, faction + " never finished");
            Assert.That(EndScreen.Title(state.Objectives.Result.Value.Outcome), Is.Not.Empty);
            Assert.That(state.Stats.Trained[1], Is.GreaterThan(0), "the AI fought back");
            Assert.That(state.FactionFor(0), Is.EqualTo(faction));
        }

        [Test] public void RematchFightsTheSameArenaAndMenuLeavesIt()
        {
            root = new GameObject("Match");
            var controller = root.AddComponent<MatchController>();
            controller.Initialize(enableAi: false, deck: DeckRules.Default(db, "china"),
                                  storePath: System.IO.Path.GetTempFileName(), rng: new System.Random(2));
            var end = root.AddComponent<EndScreen>();
            end.Bind(controller);
            var before = Rows(controller.State);
            var buildings = controller.State.Buildings.All.Select(b => $"{b.Side}{b.Type}{b.HexA}").ToArray();
            controller.State.Units.Spawn(0, db.Unit("china", "ji"), new Hex(4, 8));
            end.Rematch();
            Assert.That(Rows(controller.State), Is.EqualTo(before), "the same board");
            Assert.That(controller.State.Buildings.All.Select(b => $"{b.Side}{b.Type}{b.HexA}").ToArray(),
                Is.EqualTo(buildings), "and the same walls");
            Assert.That(controller.State.Units.All, Is.Empty, "swept clean");
            end.ToMenu();
            Assert.That(end.RequestedScene, Is.EqualTo("Menu"));
        }

        private static string[] Rows(MatchState state)
        {
            var rows = new string[Board.Rows];
            for (int r = 0; r < Board.Rows; r++)
            {
                var row = new char[Board.Cols];
                for (int c = 0; c < Board.Cols; c++) row[c] = state.Terrain.CodeAt(c, r);
                rows[r] = new string(row);
            }
            return rows;
        }
    }
}
