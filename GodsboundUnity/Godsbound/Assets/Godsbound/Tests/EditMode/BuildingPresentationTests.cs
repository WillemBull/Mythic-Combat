using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Match;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    public class BuildingPresentationTests
    {
        [Serializable] public class Geometry
        { public string faction,type; public int side,c; public bool mirror; public float x,y,size,imageSize,healthY,labelY; }
        [Serializable] public class Fixture { public Geometry[] cases; }
        private GameObject root;
        private BuildingsView view;
        [SetUp] public void Setup() { root=new GameObject("BuildingViewTest");view=root.AddComponent<BuildingsView>(); }
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(root); }
        private static Building BuildingAt(int side=0,int col=2) =>
            new Building(side,BuildingType.Fortress,"Fortress",new Hex(col,5),new Hex(col+1,5),660);
        [Test] public void EveryFactionSideAndColumnMatchesBrowserGeometry()
        {
            var fixtures=JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/building_presentation_reference.json")));
            Assert.That(fixtures.cases.Length,Is.EqualTo(48));
            foreach(var c in fixtures.cases)
            {
                var b=new Building(c.side,Building.ParseType(c.type),c.type,new Hex(c.c,5),new Hex(c.c+1,5),600);
                var state=new MatchState(GameDataLoader.Load(),buildings:new BuildingMap(new[]{b}));
                view.Bind(state,_=>c.faction);
                var body=view.BodyOf(b);var art=body.Find("Art").GetComponent<SpriteRenderer>();
                Assert.That(art.sprite,Is.Not.Null,c.faction+" "+c.type);
                Assert.That(art.flipX,Is.EqualTo(c.mirror));
                Assert.That(art.transform.localScale.x,Is.EqualTo(c.imageSize).Within(0.0001f));
                Assert.That(art.transform.localScale.y,Is.EqualTo(c.size).Within(0.0001f),"art remains upright");
                Assert.That(art.transform.localPosition.x-c.size/2,Is.EqualTo(c.x).Within(0.0001f));
                Assert.That(-art.transform.localPosition.y-c.size/2,Is.EqualTo(c.y).Within(0.0001f));
                Assert.That(body.Find("Health").localPosition.y,Is.EqualTo(-c.healthY).Within(0.0001f));
                Assert.That(body.Find("Name").localPosition.y,Is.EqualTo(-c.labelY).Within(0.0001f));
                var a=BoardWorld.CenterOf(b.HexA,BoardWorld.UnitLayout());var z=BoardWorld.CenterOf(b.HexB,BoardWorld.UnitLayout());
                Assert.That(body.localPosition.x,Is.EqualTo((a.x+z.x)/2).Within(0.0001f));
                Assert.That(body.localPosition.y,Is.EqualTo((a.y+z.y)/2).Within(0.0001f));
            }
        }
        [Test] public void AllTwelveImagesLoadAndUseExistingBrowserFiles()
        {
            var data=BuildingPresentationData.Load();Assert.That(data.art.Length,Is.EqualTo(12));
            using(var catalog=new BuildingArtCatalog(data))
                foreach(var entry in data.art)
                {
                    Assert.That(entry.source,Does.EndWith("_front.png"));
                    var sprite=catalog.Resolve(entry.faction,entry.type);Assert.That(sprite,Is.Not.Null);
                    Assert.That(sprite.texture.width,Is.EqualTo(sprite.texture.height));
                    Assert.That(catalog.Resolve(entry.faction,entry.type),Is.SameAs(sprite),"cached per catalog");
                }
        }
        [Test] public void MissingArtKeepsFootprintsHealthAndLabel()
        {
            var b=BuildingAt();var state=new MatchState(GameDataLoader.Load(),buildings:new BuildingMap(new[]{b}));
            view.Bind(state,_=>"missing-faction");
            var body=view.BodyOf(b);
            Assert.That(view.MarkerCount,Is.EqualTo(2));
            Assert.That(body.Find("Art").gameObject.activeSelf,Is.False);
            Assert.That(body.Find("Health").gameObject.activeSelf,Is.True);
            Assert.That(body.Find("Name").GetComponent<TextMesh>().text,Is.EqualTo("FORTRESS"));
            using(var catalog=new BuildingArtCatalog(new BuildingPresentationData { art=new[]{new BuildingArtEntry
                { faction="broken",type="fortress",resource="BuildingArt/no-such-image" }} }))
                Assert.That(catalog.Resolve("broken","fortress"),Is.Null,"missing texture also falls back");
        }
        [Test] public void DamageUpdatesInPlaceDestructionAndResetRestoreViews()
        {
            var b=BuildingAt();var map=new BuildingMap(new[]{b});view.Bind(map);
            var body=view.BodyOf(b);var health=body.Find("Health");float width=health.localScale.x;
            b.TakeDamage(b.MaxHp/2);view.Sync();
            Assert.That(view.BodyOf(b),Is.SameAs(body),"health updates must not recreate the view");
            Assert.That(health.localScale.x,Is.EqualTo(width/2).Within(0.0001f));
            Assert.That(health.localPosition.x-health.localScale.x/2,Is.EqualTo(-width/2).Within(0.0001f),"bar shrinks from the right");
            b.TakeDamage(b.MaxHp);view.Sync();
            Assert.That(body.Find("Art").gameObject.activeSelf,Is.False);
            Assert.That(health.gameObject.activeSelf,Is.False);Assert.That(view.MarkerCount,Is.EqualTo(2));
            Assert.That(map.StandingAt(b.HexA),Is.Null,"rubble is walkable");
            b.Reset();view.Sync();Assert.That(body.Find("Art").gameObject.activeSelf,Is.True);
            Assert.That(health.localScale.x,Is.EqualTo(width));
        }
        [Test] public void ShotsExpireOnMatchClockAndResetUnsubscribesOldMatch()
        {
            var state=new MatchState(GameDataLoader.Load());var b=state.Buildings.Of(0,BuildingType.Fortress);
            view.Bind(state,_=>"egypt");state.Units.Spawn(1,state.Database.Unit("egypt","spear"),b.HexA);
            state.Fortresses.Tick(0,0);Assert.That(view.TracerCount,Is.EqualTo(1));
            view.Sync();Assert.That(view.TracerCount,Is.EqualTo(1),"paused clock keeps the tracer");
            state.Objectives.Clock.Advance(state.Database.Fortresses.tracerLife);view.Sync();Assert.That(view.TracerCount,Is.Zero);
            b.AttackTimer=0;state.Fortresses.Tick(0,state.Elapsed);Assert.That(view.TracerCount,Is.EqualTo(1));
            state.Reset();view.Bind(state);Assert.That(view.TracerCount,Is.Zero);
            var next=new MatchState(GameDataLoader.Load());view.Bind(next);
            state.Units.Spawn(1,state.Database.Unit("egypt","spear"),b.HexA);state.Fortresses.Tick(0,0);
            Assert.That(view.TracerCount,Is.Zero,"old match subscription removed");
            view.enabled=false;Assert.That(view.MarkerCount,Is.Zero);Assert.That(root.transform.childCount,Is.Zero);
            var nb=next.Buildings.Of(0,BuildingType.Fortress);
            next.Units.Spawn(1,next.Database.Unit("egypt","spear"),nb.HexA);next.Fortresses.Tick(0,0);
            Assert.That(view.TracerCount,Is.Zero,"disabled view unsubscribed");
            view.enabled=true;Assert.That(view.MarkerCount,Is.EqualTo(12));nb.AttackTimer=0;next.Fortresses.Tick(0,0);
            Assert.That(view.TracerCount,Is.EqualTo(1),"re-enable subscribes once");
        }
    }
}
