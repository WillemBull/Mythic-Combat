using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Match;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    public class UnitUpdateTests
    {
        [Serializable] public class Cell { public int c, r, owner; public string code; }
        [Serializable] public class Target { public int unit, building; }
        [Serializable] public class Passive { public int side; public string key; }
        [Serializable] public class Seed
        {
            public UnitData def;
            public int side, c, r, index;
            public float x, y, attackTimer, stunUntil;
            public bool dead, free, hold, arrived, invisible;
            public Target target;
            public Cell[] route;
        }
        [Serializable] public class BuildingSeed { public int side, c0, r0, c1, r1; public string type; public float hp; }
        [Serializable] public class Frame
        {
            public Target target;
            public int march, index, c, r;
            public float x, y, attackTimer;
            public bool arrived, free;
            public float[] hp, buildingHp;
            public Cell[] route;
        }
        [Serializable] public class Sample
        {
            public string name;
            public string[] factions;
            public float elapsed, dt, veil;
            public float[] reveal;
            public int pick;
            public bool[] hidden;
            public Passive[] passives;
            public Cell[] terrain, groves;
            public Seed[] units;
            public BuildingSeed[] buildings;
            public Frame[] frames;
        }
        [Serializable] public class Fixture { public string source; public float hex, ox, oy; public Sample[] cases; }
        private static Fixture ReadFixture() => JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(
            Application.dataPath, "Godsbound/Tests/EditMode/Fixtures/unit_update_reference.json")));
        public static IEnumerable BrowserCases()
        {
            foreach (var sample in ReadFixture().cases)
                yield return new TestCaseData(sample.name).SetName("UnitUpdateBrowser_" + sample.name);
        }
        private Fixture _reference;
        private GameDatabase _db;
        private UnitUpdate _update;
        [OneTimeSetUp] public void ReadReference() => _reference = ReadFixture();
        [SetUp] public void Setup()
        {
            GameDataLoader.Invalidate(); _db = GameDataLoader.Load();
            _update = new UnitUpdate(new System.Random(1));
        }
        private MatchState State(BuildingMap buildings = null)
        {
            var terrain = new TerrainMap();
            foreach (var h in Board.AllCells()) terrain[h] = TerrainType.Plains;
            return new MatchState(_db, terrain, buildings ?? new BuildingMap(), pathJitter: false);
        }
        private static UnitData Def(float speed = 1f) => new UnitData
        { key = "test", cat = "human", dtype = "melee", armor = "light", hp = 10000f, dmg = 20f, range = 1, attackSpeed = 1f, speed = speed, size = 1 };
        private Unit Body(MatchState s, int side, int c, int r, UnitData def = null)
        {
            var u = s.Units.Spawn(side, def ?? Def(), new Hex(c, r)); u.AttackTimer = 0f; return u;
        }
        private static Building Building(MatchState s, int c, int r, BuildingType type)
        {
            var b = new Building(1, type, type.ToString(), new Hex(c, r), new Hex(c, r + 1), 10000f);
            s.Buildings.Add(b); return b;
        }
        private static MovementContact Contact(MatchState state, Target target) => target.unit >= 0
            ? new MovementContact(state.Units.All[target.unit]) : target.building >= 0
                ? new MovementContact(state.Buildings.All[target.building]) : null;
        private static void AssertTarget(MatchState s, Target expected, MovementContact actual, string message)
        {
            if (expected.unit >= 0) Assert.AreSame(s.Units.All[expected.unit], actual?.Unit, message);
            else if (expected.building >= 0) Assert.AreSame(s.Buildings.All[expected.building], actual?.Building, message);
            else Assert.IsNull(actual, message);
        }

        [TestCaseSource(nameof(BrowserCases))]
        public void TargetingAndOrdinaryUpdatesMatchBrowser(string name)
        {
            var sample = _reference.cases.Single(c => c.name == name);
            var s = State();
            s.UnitContext.FactionForSide = side => sample.factions[side];
            s.UnitContext.HasPassive = (side, key) => sample.passives.Any(p => p.side == side && p.key == key);
            s.UnitContext.InGrove = u => sample.groves.Any(g => g.c == u.Hex.C && g.r == u.Hex.R && g.owner == u.Side);
            s.UnitContext.VeilUntil = sample.veil;
            Array.Copy(sample.reveal, s.UnitContext.RevealedUntil, 2);
            foreach (var cell in sample.terrain) s.Terrain[new Hex(cell.c, cell.r)] = TerrainTable.FromCode(cell.code[0]);
            foreach (var b in sample.buildings) s.Buildings.Add(new Building(b.side, Godsbound.Core.Buildings.Building.ParseType(b.type), b.type,
                new Hex(b.c0, b.r0), new Hex(b.c1, b.r1), b.hp));
            foreach (var seed in sample.units)
            {
                var u = s.Units.Spawn(seed.side, seed.def, new Hex(seed.c, seed.r));
                u.SetRoute(seed.route.Length > 0 ? seed.route.Select(h => new Hex(h.c, h.r)) : null);
                typeof(Unit).GetProperty(nameof(Unit.RouteIndex)).SetValue(u, seed.index);
                typeof(Unit).GetProperty(nameof(Unit.Arrived)).SetValue(u, seed.arrived);
                u.MoveTo(new BoardPoint((seed.x - _reference.ox) / _reference.hex, (seed.y - _reference.oy) / _reference.hex));
                u.Free = seed.free; u.Hold = seed.hold; u.AttackTimer = seed.attackTimer;
                u.StunUntil = seed.stunUntil; u.Combat.Invisible = seed.invisible;
                if (seed.dead) u.Kill();
            }
            for (int i = 0; i < sample.units.Length; i++) s.Units.All[i].Engagement = Contact(s, sample.units[i].target);
            var actor = s.Units.All[0];
            Assert.AreSame(sample.pick >= 0 ? s.Units.All[sample.pick] : null, _update.ScanEncounter(s, actor, sample.elapsed), "scan pick");
            for (int i = 0; i < sample.units.Length; i++)
                Assert.AreEqual(sample.hidden[i], _update.HiddenFrom(s, s.Units.All[i], actor, sample.elapsed), "visibility unit " + i);
            for (int i = 0; i < sample.frames.Length; i++)
            {
                _update.Update(s, actor, sample.dt, sample.elapsed + i * sample.dt);
                var f = sample.frames[i]; string at = "frame " + i;
                AssertTarget(s, f.target, actor.Engagement, at + " target");
                Assert.AreSame(f.march >= 0 ? s.Buildings.All[f.march] : null, actor.MarchGoal, at + " march goal");
                Assert.AreEqual(f.attackTimer, actor.AttackTimer, 0.0001f, at + " attack timer");
                Assert.AreEqual(new Hex(f.c, f.r), actor.Hex, at + " hex");
                Assert.AreEqual((f.x - _reference.ox) / _reference.hex, actor.Position.X, 0.0002f, at + " X");
                Assert.AreEqual((f.y - _reference.oy) / _reference.hex, actor.Position.Y, 0.0002f, at + " Y");
                Assert.AreEqual(f.arrived, actor.Arrived, at + " arrived");
                Assert.AreEqual(f.free, actor.Free, at + " free");
                Assert.AreEqual(f.index, actor.RouteIndex, at + " route index");
                CollectionAssert.AreEqual(f.route.Select(h => new Hex(h.c, h.r)), actor.Route ?? (System.Collections.Generic.IEnumerable<Hex>)Array.Empty<Hex>(), at + " route");
                for (int j = 0; j < f.hp.Length; j++) Assert.AreEqual(sample.units[j].dead ? 0f : f.hp[j], s.Units.All[j].Hp, 0.001f, at + " hp " + j);
                for (int j = 0; j < f.buildingHp.Length; j++) Assert.AreEqual(f.buildingHp[j], s.Buildings.All[j].Hp, 0.001f, at + " building hp " + j);
            }
        }

        [Test] public void LostRouteIsNotRebuiltToSameDestination()
        {
            var s = State(); var u = Body(s, 0, 4, 8); var b = Building(s, 4, 2, BuildingType.City);
            _update.Update(s, u, 0.05f, 1f);
            Assert.AreSame(b, u.MarchGoal); Assert.IsTrue(u.HasRoute);
            u.SetRoute(null); u.Engagement = null;
            var pos = u.Position;
            for (int i = 0; i < 20; i++) _update.Update(s, u, 0.05f, 2f + i * 0.05f);
            Assert.AreSame(b, u.MarchGoal); Assert.IsFalse(u.HasRoute);
            Assert.AreEqual(pos, u.Position, "no same-destination recovery, per Willem");
        }

        [Test] public void ExhaustedRouteIsNotRebuiltToSameDestination()
        {
            var s = State(); var u = Body(s, 0, 4, 8); var b = Building(s, 4, 2, BuildingType.City);
            _update.Update(s, u, 0.05f, 1f);
            typeof(Unit).GetProperty(nameof(Unit.RouteIndex)).SetValue(u, u.Route.Count);
            u.Engagement = null;
            _update.Update(s, u, 0.05f, 2f);
            Assert.AreSame(b, u.MarchGoal); Assert.IsFalse(u.HasRoute);
        }

        [Test] public void DestinationStaysCommittedEvenWhenAnotherBuildingBecomesNearer()
        {
            var s = State(); var u = Body(s, 0, 4, 8); var b = Building(s, 4, 2, BuildingType.City);
            _update.Update(s, u, 0.05f, 1f);
            var route = u.Route;
            Building(s, 7, 8, BuildingType.Temple);
            u.Engagement = null;
            _update.Update(s, u, 0.05f, 2f);
            Assert.AreSame(b, u.MarchGoal); Assert.AreSame(route, u.Route);
        }

        [Test] public void DestroyedDestinationPicksAnotherBuildingAndBuildsNewRoute()
        {
            var s = State(); var u = Body(s, 0, 4, 8); var b = Building(s, 4, 4, BuildingType.City);
            var other = Building(s, 1, 1, BuildingType.Temple);
            _update.Update(s, u, 0.05f, 1f); var route = u.Route;
            Assert.AreSame(b, u.MarchGoal); b.TakeDamage(b.MaxHp);
            _update.Update(s, u, 0.05f, 2f);
            Assert.AreSame(other, u.MarchGoal); Assert.AreNotSame(route, u.Route); Assert.IsTrue(u.HasRoute);
        }

        [Test] public void LockedDestinationChangeRebuildsOnce()
        {
            var s = State(); var u = Body(s, 0, 4, 8); Building(s, 4, 2, BuildingType.City);
            var other = Building(s, 1, 1, BuildingType.Temple);
            _update.Update(s, u, 0.05f, 1f); var route = u.Route;
            u.LockedGoal = other; u.Engagement = null;
            _update.Update(s, u, 0.05f, 2f);
            Assert.AreSame(other, u.MarchGoal); Assert.AreNotSame(route, u.Route);
            route = u.Route; u.Engagement = null; _update.Update(s, u, 0.05f, 3f);
            Assert.AreSame(route, u.Route);
        }

        [Test] public void OrdinaryMatchLoopNowFightsAndCanDestroyLastBuilding()
        {
            var s = State();
            s.Buildings.Add(new Building(0, BuildingType.City, "home", new Hex(1, 10), new Hex(1, 11), 100f));
            var b = new Building(1, BuildingType.City, "enemy", new Hex(4, 4), new Hex(4, 3), 1f); s.Buildings.Add(b);
            Body(s, 0, 4, 5);
            var result = new MatchLoop().Tick(s, 0.05f);
            Assert.IsTrue(b.Dead); Assert.IsTrue(result.HasValue); Assert.IsTrue(result.Value.Instant);
            Assert.AreEqual(1f, s.Combat.BuildingDamage[0]);
        }

        [Test] public void LiveLoopDropsDeadTargetAndSelectsItsNextEnemy()
        {
            var s = new MatchState(_db, pathJitter: false);
            var a = Body(s, 0, 4, 6); var first = Body(s, 1, 4, 5); var next = Body(s, 1, 4, 7);
            a.Engagement = new MovementContact(first); first.Kill();
            new MatchLoop().Tick(s, 0.05f);
            Assert.AreSame(next, a.Engagement?.Unit); Assert.IsFalse(s.Units.All.Contains(first));
            Assert.Less(next.Hp, next.MaxHp);
        }

        [Test] public void SingleFileWaitSurvivesUnitUpdateAndFlyingUsesSeparateLayer()
        {
            var s = State(); var a = Body(s, 0, 4, 8); var b = Body(s, 0, 4, 7);
            a.SetRoute(new[] { new Hex(4, 7), new Hex(4, 6) }); b.SetRoute(new[] { new Hex(4, 6) });
            var start = a.Position;
            for (int i = 0; i < 30; i++) _update.Update(s, a, 0.05f, i * 0.05f);
            Assert.AreEqual(start, a.Position); Assert.AreEqual(new Hex(4, 7), a.Route[0]);
            b.Kill(); var flyerDef = Def(); flyerDef.extras.Add(new ExtraValue { key = "flying", kind = "bool", value = "true" });
            Body(s, 0, 4, 7, flyerDef);
            _update.Update(s, a, 0.05f, 2f); Assert.AreEqual(new Hex(4, 7), a.Hex);
        }

        [Test] public void InitialAttackDelayUsesExportedSpreadAndIsInitializedOnce()
        {
            var s = State(); var u = s.Units.Spawn(0, Def(), new Hex(4, 6)); u.Hold = true;
            float expected = (float)new System.Random(1).NextDouble() * _db.Combat.Value("initialAttackSpread");
            _update.Update(s, u, 0.01f, 1f);
            Assert.AreEqual(expected - 0.01f, u.AttackTimer, 0.00001f);
            _update.Update(s, u, 0.01f, 1.01f);
            Assert.AreEqual(expected - 0.02f, u.AttackTimer, 0.00001f);
        }

        [TestCase(1f, 0.05f)][TestCase(2f, 0.05f)][TestCase(1.25f, 0.05f)]
        [TestCase(1f, 0.016f)][TestCase(2f, 0.016f)][TestCase(1.25f, 0.016f)]
        public void AttackCadenceIsNeverFasterThanDefinition(float attacksPerSecond, float dt)
        {
            var s = State(); var def = Def(); def.attackSpeed = attacksPerSecond;
            var a = Body(s, 0, 4, 6, def); var t = Body(s, 1, 4, 5);
            double previousHit = -1, period = 1d / attacksPerSecond;
            int hits = 0;
            for (int i = 0; i < 1000; i++)
            {
                double now = i * (double)dt; float hp = t.Hp;
                _update.Update(s, a, dt, (float)now);
                if (t.Hp == hp) continue;
                if (previousHit >= 0)
                {
                    Assert.GreaterOrEqual(now - previousHit, period - 0.000001d);
                    Assert.LessOrEqual(now - previousHit, period + dt + 0.000001d);
                }
                previousHit = now; hits++;
            }
            Assert.Greater(hits, 1);
        }
    }
}
