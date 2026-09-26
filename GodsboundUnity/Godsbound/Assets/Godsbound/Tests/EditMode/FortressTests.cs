using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Match;
using Godsbound.Core.Units;
using Godsbound.Core.Economy;
using Godsbound.Core.Training;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    public class FortressTests
    {
        [Serializable] public class Body
        { public int c, r; public float hp, invuln; public bool flying, ally, friendly, invisible, revives, farPosition; }
        [Serializable] public class Case
        {
            public string name, type; public int side, shots, humans; public bool dead;
            public float timer, nextTimer; public Body[] units; public float[] expectedHp, favor; public bool[] expectedDead;
        }
        [Serializable] public class Fixture { public Case[] cases; }
        [Test] public void BrowserFortressScenariosMatchIncludingDeathRewards()
        {
            var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/fortress_reference.json")));
            Assert.That(fixture.cases.Length, Is.EqualTo(40));
            foreach (var c in fixture.cases)
            {
                var b = new Building(c.side, Building.ParseType(c.type), "Test", new Hex(4,5), new Hex(5,5), 660);
                if (c.dead) b.TakeDamage(b.MaxHp);
                b.AttackTimer = c.timer;
                var state = new MatchState(GameDataLoader.Load(), buildings: new BuildingMap(new[] {b}),
                    combatContext: new CombatContext { FactionForSide = s => s == 0 ? "aztec" : "egypt",
                        HasPassive = (s,k) => s == c.side && k == "inevitable" });
                int shots = 0, deaths = 0;
                state.Fortresses.Fired += shot => { shots++; Assert.That(shot.Source, Is.SameAs(b)); };
                state.Combat.UnitDied += u => deaths++;
                foreach (var v in c.units)
                {
                    int side = v.friendly ? c.side : 1-c.side;
                    var def = JsonUtility.FromJson<UnitData>(JsonUtility.ToJson(state.Database.Unit(side == 0 ? "aztec" : "egypt", side == 0 ? "atlatl" : "spear")));
                    foreach (var flag in new[] { ("flying", v.flying), ("allyTargetOnly", v.ally), ("revivesOnce", v.revives) })
                        def.extras.Add(new ExtraValue { key = flag.Item1, kind = "bool", value = flag.Item2 ? "true" : "false" });
                    var u = state.Units.Spawn(side, def, new Hex(v.c,v.r));
                    u.TakeDamage(u.Hp-v.hp); u.Combat.InvulnerableUntil = v.invuln;
                    u.Combat.Invisible = v.invisible;
                    if (v.farPosition) u.MoveTo(HexLayout.Unit().Center(new Hex(0,0)));
                }
                state.Fortresses.Tick(0.05f, 10f);
                for (int i=0;i<c.units.Length;i++)
                {
                    Assert.That(state.Units.All[i].Hp, Is.EqualTo(c.expectedHp[i]).Within(0.001f), c.name);
                    Assert.That(state.Units.All[i].Dead, Is.EqualTo(c.expectedDead[i]), c.name);
                }
                Assert.That(b.AttackTimer, Is.EqualTo(c.nextTimer).Within(0.001f), c.name);
                Assert.That(shots, Is.EqualTo(c.shots), c.name);
                Assert.That(state.Combat.DeadHumans.Count, Is.EqualTo(c.humans), c.name);
                Assert.That(deaths, Is.EqualTo(c.humans), c.name);
                for(int side=0;side<2;side++) Assert.That(state.Resources[side].Favor, Is.EqualTo(c.favor[side]).Within(0.001f), c.name);
                foreach(var dead in state.Units.All.Where(u=>u.Dead)) state.Combat.KillUnit(dead, 11f);
                Assert.That(deaths, Is.EqualTo(c.humans), "repeat kill never rewards twice");
            }
        }
        private sealed class Observe : IUnitUpdate
        {
            public float SeenHp = -1;
            public void Update(MatchState s, Unit u, float dt, float elapsed) { SeenHp = u.Hp; }
        }
        [Test] public void TrainingSpawnsBeforeGunAndGunBeforeUnits()
        {
            var s = new MatchState(GameDataLoader.Load());
            var b = s.Buildings.Of(1, BuildingType.Fortress);
            var def = s.Database.Unit("egypt","spear");
            var purse = new Purse(120,250);
            Assert.That(s.Training.TryEnqueue(purse,0,def,b.HexA,s.Buildings.Of(0,BuildingType.City),
                default(TrainingModifiers),0,out _,out _,startedAt:-100), Is.True);
            var observe = new Observe(); new MatchLoop(observe).Tick(s,0.05f);
            Assert.That(observe.SeenHp, Is.EqualTo(def.hp-s.Database.Fortresses.damage));
        }
        [Test] public void ReadyRetriesCadenceAndReset()
        {
            var s = new MatchState(GameDataLoader.Load()); var b=s.Buildings.Of(0,BuildingType.Fortress);
            s.Fortresses.Tick(2f,2f); Assert.That(b.AttackTimer, Is.LessThan(0));
            var u=s.Units.Spawn(1,s.Database.Unit("egypt","spear"),b.HexB);
            int shots=0;s.Fortresses.Fired+=_=>shots++;
            s.Fortresses.Tick(0f,2f); s.Fortresses.Tick(0.5f,2.5f);
            Assert.That(shots,Is.EqualTo(1));
            s.Fortresses.Tick(0.5f,3f); Assert.That(shots,Is.EqualTo(2));
            s.Reset();Assert.That(b.AttackTimer,Is.Zero);
            s.Units.Spawn(1,s.Database.Unit("egypt","spear"),b.HexA);
            s.Fortresses.Tick(0f,0f);Assert.That(shots,Is.EqualTo(3));
        }
        [Test] public void SetupPauseAndFinishedSceneDoNotFire()
        {
            var go=new GameObject("FortressGateTest");
            try
            {
                var controller=go.AddComponent<MatchController>(); controller.Initialize(enableAi:false, deck: TestDeck.Egypt(), storePath: TestDeck.Scratch());
                var s=controller.State;var b=s.Buildings.Of(0,BuildingType.Fortress);
                var u=s.Units.Spawn(1,s.Database.Unit("egypt","spear"),b.HexA);
                controller.Advance(0.05f);Assert.That(u.Hp,Is.EqualTo(u.MaxHp));
                controller.Begin();controller.SetPaused(true);controller.Advance(0.05f);
                Assert.That(u.Hp,Is.EqualTo(u.MaxHp));
                controller.SetPaused(false);controller.Advance(0.05f);Assert.That(u.Hp,Is.LessThan(u.MaxHp));
                foreach(var enemy in s.Buildings.Of(1))enemy.TakeDamage(enemy.MaxHp);
                s.Objectives.Evaluate(); b.AttackTimer=0;float hp=u.Hp;
                controller.Advance(0.05f);Assert.That(u.Hp,Is.EqualTo(hp));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
