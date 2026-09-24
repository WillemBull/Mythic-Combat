using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Economy;
using Godsbound.Core.Match;
using Godsbound.Core.Training;
using Godsbound.Data;

namespace Godsbound.Tests
{
    public class DeploymentDraftTests
    {
        [Serializable] public class Cell { public int c, r; public Hex Hex => new Hex(c, r); }
        [Serializable] public class Case { public string name; public Cell[] source, moves, route; }
        [Serializable] public class Fixture { public Case[] cases; }
        private MatchState state;
        private DeploymentDraft draft;
        private Building source;
        [SetUp] public void Setup()
        {
            state = new MatchState(GameDataLoader.Load(), pathJitter: false);
            foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
            state.Resources[0] = new Purse(120, 100);
            source = state.Buildings.Of(0, BuildingType.Fortress);
            draft = new DeploymentDraft(state, state.Database.Unit("egypt", "spear"));
        }
        private void Draw()
        {
            draft.Visit(source.HexA); draft.Visit(new Hex(7, 8)); draft.Visit(new Hex(7, 7));
        }
        [Test] public void DragPathsMatchBrowserFixtures()
        {
            var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/deployment_reference.json")));
            foreach (var sample in fixture.cases)
            {
                var d = new DeploymentDraft(state, draft.Definition); d.Visit(sample.source[0].Hex);
                foreach (var cell in sample.moves) d.Visit(cell.Hex);
                Assert.That(d.Route, Is.EqualTo(sample.route.Select(h => h.Hex).ToArray()), sample.name);
            }
        }
        [Test] public void PreviewGoalSurvivesPaymentAndTraining()
        {
            Draw(); Assert.That(draft.Goal, Is.Not.Null);
            var goal = draft.Goal; var route = draft.Route.ToArray();
            Assert.That(draft.Commit(true), Is.True); Assert.That(draft.Commit(true), Is.False);
            Assert.That(state.Resources[0].Food, Is.EqualTo(120 - draft.Definition.costFood));
            var order = state.Training.Pending.Single();
            state.Training.Tick(order.ReadyAt, state.Units, state.Buildings);
            var unit = state.Units.All.Single();
            Assert.That(unit.LockedGoal, Is.SameAs(goal)); Assert.That(unit.Route, Is.EqualTo(route));
        }
        [Test] public void DrawingTimeCountsTowardTraining()
        {
            Draw(); state.Objectives.Clock.Advance(draft.Definition.train + 1);
            Assert.That(draft.Commit(true), Is.True);
            Assert.That(state.Training.Pending.Single().ReadyAt, Is.EqualTo(state.Elapsed));
        }
        [TestCase(false)] [TestCase(true)] public void CancelOrOffBoardReleaseDoesNotSpend(bool cancel)
        {
            Draw(); if (cancel) draft.Cancel();
            Assert.That(draft.Commit(false), Is.False); Assert.That(state.Training.PendingCount, Is.Zero);
            Assert.That(state.Resources[0].Food, Is.EqualTo(120));
        }
        [Test] public void MissingSourceOrFundsAndDestroyedSourceCannotTrain()
        {
            draft.Visit(new Hex(4, 6)); Assert.That(draft.Commit(true), Is.False);
            draft = new DeploymentDraft(state, draft.Definition); Draw(); state.Resources[0] = Purse.Empty;
            Assert.That(draft.Commit(true), Is.False);
            state.Resources[0] = new Purse(120, 100); draft = new DeploymentDraft(state, draft.Definition); Draw();
            source.TakeDamage(source.Hp); Assert.That(draft.Commit(true), Is.False);
            Assert.That(state.Training.PendingCount, Is.Zero);
        }
        [Test] public void OccupiedExitStillUsesStandingBuildingFallback()
        {
            Draw(); var exit = draft.Route[0];
            state.Units.Spawn(0, draft.Definition, exit);
            Assert.That(draft.Commit(true), Is.True);
            var at = state.Training.Pending.Single().ReadyAt;
            var spawned = state.Training.Tick(at, state.Units, state.Buildings).Single();
            Assert.That(source.Occupies(spawned.Hex), Is.True); Assert.That(state.Training.PendingCount, Is.Zero);
        }
        [Test] public void ChangedTerrainAndSetupRejectCommit()
        {
            Draw(); state.Terrain[draft.Route[0]] = TerrainType.Mountain;
            Assert.That(draft.Commit(true), Is.False);
            state.Terrain[new Hex(7, 8)] = TerrainType.Plains;
            draft = new DeploymentDraft(state, draft.Definition); Draw(); state.Phase = MatchPhase.Setup;
            Assert.That(draft.Commit(true), Is.False);
        }
    }
}
