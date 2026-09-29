using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;
using Godsbound.Core.Setup;
using Godsbound.Data;
using Godsbound.Presentation;
using Godsbound.Presentation.Setup;

namespace Godsbound.Tests
{
    /// <summary>
    /// U33: everything the setup scenes do that is not drawing — the step flow, the picking gates,
    /// the brushes, the building drag and what a slot ends up holding.
    /// </summary>
    public class SetupUiTests
    {
        private GameDatabase db;
        private string scratch;
        private GameObject root;

        [OneTimeSetUp] public void LoadDatabase()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
        }

        [SetUp] public void Scratch()
        {
            scratch = Path.Combine(Path.GetTempPath(), "godsbound-setup-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown] public void Sweep()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            root = null;
            try { if (File.Exists(scratch)) File.Delete(scratch); } catch (IOException) { }
        }

        private SetupSession Session(SetupStep step = SetupStep.Faction) =>
            new SetupSession(db, DeckRules.Default(db, "egypt"), step);

        private SetupController Controller()
        {
            root = new GameObject("Setup");
            var controller = root.AddComponent<SetupController>();
            controller.Initialize(db, scratch);
            return controller;
        }

        [Test] public void ASessionStartsOnADeckAndWalksTheSteps()
        {
            var session = Session(SetupStep.Home);
            Assert.That(session.Faction, Is.EqualTo("egypt"));
            Assert.That(session.Gods.Count, Is.EqualTo(DeckRules.GodCount));
            Assert.That(session.Step, Is.EqualTo(SetupStep.Home));
            int changes = 0;
            session.Changed += () => changes++;
            foreach (var step in new[] { SetupStep.Faction, SetupStep.Gods, SetupStep.Heroes, SetupStep.Units, SetupStep.Terrain })
            {
                session.Go(step);
                Assert.That(session.Step, Is.EqualTo(step));
            }
            Assert.That(changes, Is.EqualTo(5), "every step change tells the view once");
        }

        [Test] public void ChangingPantheonClearsTheDeckItCannotFight()
        {
            var session = Session();
            Assert.That(session.ToggleGod(session.Gods[0]), Is.True, "drop one god");
            Assert.That(session.ChooseFaction("china"), Is.True);
            Assert.That(session.Faction, Is.EqualTo("china"));
            Assert.That(session.Gods, Is.EqualTo(db.Faction("china").defaultGodPick));
            Assert.That(session.Loadout.All(k => db.Unit("china", k) != null), Is.True, "a Chinese hand");
            Assert.That(session.Half.Faction, Is.EqualTo("china"), "and its own ground");
            Assert.That(session.ChooseFaction("atlantis"), Is.False);
            Assert.That(session.Faction, Is.EqualTo("china"));
        }

        [Test] public void PickingIsCappedAndReversibleButNeverSilentlySwapped()
        {
            var session = Session();
            var roster = session.GodChoices().Select(g => g.key).ToArray();
            while (session.Gods.Count > 0) session.ToggleGod(session.Gods[0]);
            for (int i = 0; i < DeckRules.GodCount; i++) Assert.That(session.ToggleGod(roster[i]), Is.True, roster[i]);
            var full = session.Gods.ToArray();
            Assert.That(session.ToggleGod(roster[DeckRules.GodCount]), Is.False, "a full deck refuses");
            Assert.That(session.Gods, Is.EqualTo(full), "and keeps what was chosen");
            Assert.That(session.Hint, Does.Contain("drop one"));
            Assert.That(session.ToggleGod(full[0]), Is.True, "tapping a chosen god drops it");
            Assert.That(session.Gods, Is.EqualTo(full.Skip(1).ToArray()));
            Assert.That(session.ToggleGod("jade"), Is.False, "another pantheon's god is not offered");
            Assert.That(session.ToggleUnit(session.HeroChoices().First().key), Is.False, "a hero is not a card");
        }

        [Test] public void BrushesPaintTheHalfAndSpendTheirBudget()
        {
            var session = Session(SetupStep.Terrain);
            Assert.That(session.PaintAt(new Hex(0, 0)), Is.False, "no brush, no paint");
            session.ChooseBrush('F');
            Assert.That(session.Brush, Is.EqualTo('F'));
            Assert.That(session.PaintAt(new Hex(0, 0)), Is.True);
            Assert.That(session.Half.TerrainAt(new Hex(0, 0)), Is.EqualTo(TerrainType.Forest));
            Assert.That(session.Remaining('F'), Is.EqualTo(db.TerrainBudget('F') - 1));
            session.ChooseBrush('F');
            Assert.That(session.Brush, Is.EqualTo('\0'), "the same brush again puts it down");
            session.ChooseBrush('P'); // 'P' is the eraser; it was 'D' until Desert was removed
            Assert.That(session.PaintAt(new Hex(0, 0)), Is.True, "erased");
            Assert.That(session.Remaining('F'), Is.EqualTo(db.TerrainBudget('F')));
            session.ChooseBrush('M');
            var onBuilding = session.Half.Buildings[0].A;
            Assert.That(session.PaintAt(onBuilding), Is.False);
            Assert.That(session.Hint, Does.Contain("building"));
        }

        [Test] public void ABuildingIsDraggedAndOnlyLandsWhereItFits()
        {
            var session = Session(SetupStep.Terrain);
            var temple = session.Half.Buildings.Single(b => b.type == "temple");
            var from = temple.A;
            Assert.That(session.BeginDrag(new Hex(0, 0)), Is.False, "nothing to pick up there");
            Assert.That(session.BeginDrag(from), Is.True);
            Assert.That(session.Drag.Active, Is.True);
            Assert.That(session.DragTo(new Hex(Board.Cols - 1, 2)), Is.True);
            Assert.That(session.Drag.Legal, Is.False, "half of it would be off the board");
            Assert.That(session.DropDrag(), Is.False);
            Assert.That(temple.A, Is.EqualTo(from), "and it stays where it was");
            Assert.That(session.Hint, Does.Contain("temple"));

            Assert.That(session.BeginDrag(from), Is.True);
            Assert.That(session.DragTo(new Hex(3, 0)), Is.True);
            Assert.That(session.Drag.Legal, Is.True);
            Assert.That(session.DropDrag(), Is.True);
            Assert.That(temple.A, Is.EqualTo(new Hex(3, 0)));
            Assert.That(temple.B, Is.EqualTo(new Hex(4, 0)));

            Assert.That(session.BeginDrag(temple.A), Is.True);
            session.CancelDrag();
            Assert.That(session.Drag.Active, Is.False);
            Assert.That(session.DropDrag(), Is.False, "a cancelled drag drops nothing");
        }

        [Test] public void AnIncompleteDeckCannotStartAndSaysWhy()
        {
            var session = Session(SetupStep.Terrain);
            Assert.That(session.CanStart(), Is.True, "the shipped deck is ready");
            session.ToggleGod(session.Gods[0]);
            Assert.That(session.CanStart(out _, out var problem), Is.False);
            Assert.That(problem, Does.Contain("gods"));
        }

        [Test] public void TheControllerSavesPlaysAndReopensASlot()
        {
            var controller = Controller();
            var session = controller.Session;
            session.Go(SetupStep.Terrain);
            session.ChooseBrush('W');
            Assert.That(session.PaintAt(new Hex(8, 0)), Is.True);

            Assert.That(controller.Save(1), Is.True);
            Assert.That(controller.Store.Slot(1), Is.Not.Null);
            Assert.That(controller.Store.Slot(1).terrain[0][8], Is.EqualTo('W'), "the painted half was saved");

            Assert.That(controller.Play(1), Is.True);
            Assert.That(controller.RequestedScene, Is.EqualTo("Board"), "a started deck asks for the board");

            controller.Session.ChooseFaction("greek");
            controller.Edit(1);
            Assert.That(controller.Session.Faction, Is.EqualTo("egypt"), "editing a slot reopens what it holds");
            Assert.That(controller.Session.Step, Is.EqualTo(SetupStep.Faction));
            controller.Edit(0);
            Assert.That(controller.Session.Faction, Is.EqualTo("egypt"), "an empty slot opens on a default");
            Assert.That(controller.Store.Slot(0), Is.Null, "and saves nothing until asked");
        }

        [Test] public void AnIllegalDeckIsNeverSavedAndNeverStartsAMatch()
        {
            var controller = Controller();
            controller.Session.ToggleHero(controller.Session.Heroes[0]);
            Assert.That(controller.Save(0), Is.False);
            Assert.That(controller.LastProblem, Does.Contain("heroes"));
            Assert.That(controller.Play(0), Is.False);
            Assert.That(controller.RequestedScene, Is.Null, "no scene was asked for");
            Assert.That(controller.Store.Slot(0), Is.Null);
        }

        [Test] public void ThePreviewBoardShowsTheHalfBeingEdited()
        {
            var controller = Controller();
            controller.Session.Go(SetupStep.Terrain);
            controller.Session.ChooseBrush('M');
            Assert.That(controller.Session.PaintAt(new Hex(2, 1)), Is.True);
            Assert.That(controller.Preview[2, Board.PlayerRow0 + 1], Is.EqualTo(TerrainType.Mountain));
            for (int c = 0; c < Board.Cols; c++)
                Assert.That(controller.Preview[c, 0], Is.EqualTo(TerrainType.Plains), "the AI's half is not the player's to see");
        }

        [Test] public void TheScreenKeepsTheBoardsOwnBands()
        {
            var controls = SetupHud.Controls(1080f, 1920f);
            var title = SetupHud.Title(1080f, 1920f);
            Assert.That(title.height, Is.EqualTo(1920f * BoardViewport.DefaultTopFraction).Within(0.01f));
            Assert.That(controls.yMax, Is.EqualTo(1920f).Within(0.01f));
            Assert.That(controls.y, Is.GreaterThan(title.yMax), "the board sits between them");
            Assert.That(SetupHud.Counter(2, 4), Is.EqualTo("2/4"));
        }
    }
}
