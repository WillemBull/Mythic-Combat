using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Match;
using Godsbound.Core.Buildings;
using Godsbound.Core.Economy;
using Godsbound.Core.Training;
using System.Linq;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    public class MatchControllerTests
    {
        private GameObject root, board, buildings;
        private MatchController controller;
        [SetUp] public void Setup()
        {
            board = new GameObject("TestBoard"); board.AddComponent<BoardView>();
            buildings = new GameObject("TestBuildings"); buildings.AddComponent<BuildingsView>();
            root = new GameObject("TestMatch"); controller = root.AddComponent<MatchController>();
            controller.Initialize(board.GetComponent<BoardView>(), buildings.GetComponent<BuildingsView>(), enableAi: false,
                        deck: TestDeck.Egypt(), storePath: TestDeck.Scratch());
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(board); Object.DestroyImmediate(buildings);
        }
        [Test] public void TheEnemysOwnCastsAreDrawnEvenThoughNoUiAskedForThem()
        {
            var state = controller.State;
            state.Phase = MatchPhase.Battle;
            var fx = controller.GetComponent<PowerFx>();
            fx.Clear();
            state.Resources[1] = new Purse(0f, 300f);
            state.Gods[1].Grant("jade");
            state.Units.Spawn(0, state.Database.Unit("egypt", "spear"), new Hex(4, 4));
            Assert.That(state.Powers.TryCast(1, "jade", new Hex(4, 4)).Applied, Is.True);
            Assert.That(fx.ActiveCount, Is.GreaterThan(0), "the AI's cast is visible");
            int drawn = fx.ActiveCount;
            // The player's own casts are drawn by BattleHud, which knows the aim before the cast.
            state.Resources[0] = new Purse(0f, 300f);
            state.Gods[0].Grant("horus");
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Applied, Is.True);
            Assert.That(fx.ActiveCount, Is.EqualTo(drawn), "and not twice");
        }

        [Test] public void AMatchIsBuiltFromTheDeckItWasGiven()
        {
            var db = Godsbound.Data.GameDataLoader.Load();
            var deck = Godsbound.Core.Decks.DeckRules.Default(db, "aztec");
            deck.gods = db.Roster("aztec").Take(4).Select(g => g.key).ToArray();
            var go = new GameObject("DeckMatch");
            try
            {
                var built = go.AddComponent<MatchController>();
                built.Initialize(enableAi: false, deck: deck, storePath: System.IO.Path.GetTempFileName(),
                                 rng: new System.Random(4));
                Assert.That(built.Deck.faction, Is.EqualTo("aztec"));
                Assert.That(built.State.Gods[0].Selected.Select(g => g.Key), Is.EqualTo(deck.gods),
                    "the four gods the player chose");
                Assert.That(new[] { "egypt", "china" }, Does.Contain(built.AiFaction), "the AI plays one it knows");
                Assert.That(built.State.Gods[1].Faction, Is.EqualTo(built.AiFaction));
                Assert.That(built.State.FactionFor(0), Is.EqualTo("aztec"));
                // The player's half is the deck's, and the AI's is its own — not a mirror of ours.
                for (int c = 0; c < Board.Cols; c++)
                    Assert.That(built.State.Terrain.CodeAt(c, Board.PlayerRow0 + 1),
                        Is.EqualTo(deck.terrain[1][c]), "player row 1");
                foreach (var b in deck.buildings)
                    Assert.That(built.State.Buildings.StandingAt(new Hex(b.c0, b.r0 + Board.PlayerRow0)), Is.Not.Null,
                        b.type + " stands where the deck put it");
                var hud = go.AddComponent<BattleHud>();
                hud.Bind(built);
                Assert.That(hud.Deck.Take(4), Is.EqualTo(deck.loadout), "and deals the cards it chose");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void EgyptAndChinaAnswerEachOtherAndTheOthersDrawOne()
        {
            Assert.That(MatchController.OpponentFor("egypt", new System.Random(1)), Is.EqualTo("china"));
            Assert.That(MatchController.OpponentFor("china", new System.Random(1)), Is.EqualTo("egypt"));
            var drawn = new System.Collections.Generic.HashSet<string>();
            for (int seed = 0; seed < 20; seed++) drawn.Add(MatchController.OpponentFor("greek", new System.Random(seed)));
            Assert.That(drawn, Is.EquivalentTo(new[] { "egypt", "china" }), "both come up");
        }

        [Test] public void ViewsUseTheActualMatchObjects()
        {
            Assert.That(board.GetComponent<BoardView>().Terrain, Is.SameAs(controller.State.Terrain));
            Assert.That(buildings.GetComponent<BuildingsView>().Buildings, Is.SameAs(controller.State.Buildings));
        }
        [Test] public void SetupAndPauseFreezeClockAndResources()
        {
            var state = controller.State; var initial = state.Resources[0];
            controller.Advance(0.05f); Assert.That(state.Elapsed, Is.Zero);
            Assert.That(state.Resources[0].Food, Is.EqualTo(initial.Food));
            controller.Begin(); controller.Advance(0.05f); Assert.That(state.Elapsed, Is.GreaterThan(0));
            controller.SetPaused(true); float elapsed = state.Elapsed; var food = state.Resources[0].Food;
            controller.Advance(0.05f); Assert.That(state.Elapsed, Is.EqualTo(elapsed));
            Assert.That(state.Resources[0].Food, Is.EqualTo(food));
        }
        [Test] public void ResetRestoresExportedStartAndRemovesOldUnits()
        {
            controller.Begin(); var state = controller.State;
            state.Units.Spawn(0, state.Database.Unit("egypt", "spear"), new Hex(4, 7));
            root.GetComponent<UnitsView>().Sync(); controller.Advance(0.05f);
            controller.NewMatch(); var data = PresentationData.Load();
            Assert.That(state.Resources[0].Food, Is.EqualTo(data.food));
            Assert.That(state.Resources[0].Favor, Is.EqualTo(data.favor));
            Assert.That(state.Resources[1].Food, Is.EqualTo(data.aiFood));
            Assert.That(state.Elapsed, Is.Zero); Assert.That(state.Phase, Is.EqualTo(MatchPhase.Setup));
            Assert.That(state.Units.AliveCount, Is.Zero); Assert.That(root.GetComponent<UnitsView>().Count, Is.Zero);
        }
        [Test] public void DestroyedBuildingsResolveThenFreezeAndReset()
        {
            controller.Begin();
            foreach (var b in controller.State.Buildings.All) if (b.Side == 1) b.TakeDamage(b.Hp);
            controller.Advance(0.05f); Assert.That(controller.State.Objectives.Resolved, Is.True);
            float end = controller.State.Elapsed; controller.Advance(0.05f);
            Assert.That(controller.State.Elapsed, Is.EqualTo(end));
            controller.NewMatch(); Assert.That(controller.State.Buildings.StandingCount(1), Is.EqualTo(3));
            Assert.That(buildings.GetComponent<BuildingsView>().MarkerCount, Is.EqualTo(12));
        }
        [Test] public void SceneDeploymentTrainsRendersMarchesAndDamagesEnemyBuildings()
        {
            controller.Begin();
            for (int i = 0; i < 400; i++) controller.Advance(0.05f);
            var state = controller.State;
            var draft = new DeploymentDraft(state, state.Database.Unit("egypt", "elephant"));
            draft.Visit(state.Buildings.Of(0, BuildingType.Fortress).HexA);
            draft.Visit(new Hex(7, 8)); draft.Visit(new Hex(7, 7));
            Assert.That(draft.Commit(true), Is.True);
            float readyAt = state.Training.Pending.Single().ReadyAt;
            while (state.Elapsed <= readyAt + 0.1f) controller.Advance(0.05f);
            var unit = state.Units.All.Single();
            var view = root.GetComponent<UnitsView>(); view.Sync();
            Assert.That(view.BodyOf(unit.Id), Is.Not.Null);
            var start = unit.Position;
            for (int i = 0; i < 2000; i++) controller.Advance(0.05f);
            Assert.That(unit.Position, Is.Not.EqualTo(start));
            Assert.That(state.Buildings.All.Where(b => b.Side == 1).Any(b => b.Hp < b.MaxHp), Is.True);
            controller.NewMatch();
            Assert.That(view.Count, Is.Zero);
        }
    }
}
