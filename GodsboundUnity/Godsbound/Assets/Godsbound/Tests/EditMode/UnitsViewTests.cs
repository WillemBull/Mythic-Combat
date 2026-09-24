using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Match;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    public class UnitsViewTests
    {
        private GameObject root;
        private UnitsView view;
        private MatchState state;
        [SetUp] public void Setup()
        {
            state = new MatchState(GameDataLoader.Load()); root = new GameObject("UnitViewTest");
            view = root.AddComponent<UnitsView>(); view.Bind(state);
        }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);
        [Test] public void EveryExportedSpriteLoadsAndUnknownArtFallsBack()
        {
            using (var catalog = new UnitArtCatalog(PresentationData.Load()))
            {
                foreach (var art in catalog.Data.art) Assert.That(catalog.Resolve(art.key, art.side), Is.Not.Null, art.resource);
                Assert.That(catalog.Resolve("unregistered", 0), Is.Null);
            }
            var def = state.Database.AllUnits.First(u => !PresentationData.Load().art.Any(e => e.key == u.key));
            var unit = state.Units.Spawn(0, def, new Hex(4, 7)); view.Sync();
            Assert.That(view.BodyOf(unit.Id).Find("Art").GetComponent<SpriteRenderer>().sprite, Is.Not.Null);
        }
        [Test] public void ContinuousPositionAndScaleUseBrowserExport()
        {
            var unit = state.Units.Spawn(0, state.Database.Unit("egypt", "spear"), new Hex(4, 7));
            unit.MoveTo(new BoardPoint(4.25f, 8.75f)); view.Sync();
            var body = view.BodyOf(unit.Id); var expected = BoardWorld.ToWorld(unit.Position);
            Assert.That(body.localPosition.x, Is.EqualTo(expected.x).Within(0.0001));
            Assert.That(body.localPosition.y, Is.EqualTo(expected.y).Within(0.0001));
            var entry = PresentationData.Load().art.First(e => e.key == unit.Key && e.side == unit.Side);
            Assert.That(body.Find("Art").localScale, Is.EqualTo(Vector3.one * entry.height));
        }
        [Test] public void ConcealmentHidesHealthAndArtAndDeathRemovesBody()
        {
            var unit = state.Units.Spawn(1, state.Database.Unit("china", "dao"), new Hex(4, 5));
            unit.Combat.Invisible = true; view.Sync();
            Assert.That(view.BodyOf(unit.Id).gameObject.activeSelf, Is.False);
            unit.Combat.Invisible = false; view.Sync(); Assert.That(view.BodyOf(unit.Id).gameObject.activeSelf, Is.True);
            unit.Kill(); view.Sync(); Assert.That(view.Count, Is.Zero);
        }
        [Test] public void AttackTimerResetProducesBriefCueAndBindClearsOldBodies()
        {
            var unit = state.Units.Spawn(0, state.Database.Unit("egypt", "spear"), new Hex(4, 7));
            unit.AttackTimer = 0; view.Sync(); unit.AttackTimer = 1; view.Sync();
            var renderer = view.BodyOf(unit.Id).Find("Art").GetComponent<SpriteRenderer>();
            Assert.That(renderer.color, Is.Not.EqualTo(Color.white));
            state.Objectives.Clock.Advance(0.2f); view.Sync(); Assert.That(renderer.color, Is.EqualTo(Color.white));
            view.Bind(new MatchState(state.Database)); Assert.That(view.Count, Is.Zero);
        }
    }
}
