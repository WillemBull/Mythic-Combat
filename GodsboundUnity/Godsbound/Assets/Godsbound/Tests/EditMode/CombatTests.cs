using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    public class CombatTests
    {
        [Serializable] public class Cell { public int c, r; public string code; }
        [Serializable] public class Passive { public int side; public string key; }
        [Serializable] public class UnitState
        {
            public int side, c, r, index;
            public string key;
            public UnitData def;
            public Cell[] route;
            public float x, y, hp, buffUntil, buffDmg, stunUntil, rootUntil, slowUntil,
                invulnUntil, aegisUntil, bloodlustUntil, weakenUntil, frenzyUntil,
                poisonUntil, poisonDps, lastStandEndsAt, retreatDeadline;
            public bool dead, ambushUsed, invisible, hasRevived, lastStandUsed, retreating, free, hold;
        }
        [Serializable] public class BuildingState
        {
            public int side, c0, r0, c1, r1;
            public string type;
            public float hp, maxHp, invulnUntil, lastHitAt;
            public bool dead;
        }
        [Serializable] public class DeathState { public string key; public int side, c, r; public float at; }
        [Serializable] public class CombatCase
        {
            public string name;
            public bool hit, targetBuilding;
            public float elapsed, expectedDamage;
            public int targetIndex;
            public string[] factions;
            public Passive[] passives;
            public Cell[] terrain;
            public UnitState[] units, after;
            public BuildingState[] buildings, buildingsAfter;
            public float[] food, favor, buildingDamage, gardenDamage;
            public DeathState[] deaths;
        }
        [Serializable] public class Fixture
        {
            public string source;
            public float hex, ox, oy;
            public CombatCase[] cases;
        }
        private static Fixture ReadFixture() => JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(
            Application.dataPath, "Godsbound/Tests/EditMode/Fixtures/combat_reference.json")));
        public static IEnumerable BrowserCases()
        {
            foreach (var sample in ReadFixture().cases)
                yield return new TestCaseData(sample.name).SetName("CombatBrowser_" + sample.name);
        }

        private GameDatabase _db;
        private UnitField _units;
        private BuildingMap _buildings;
        private TerrainMap _terrain;
        private CombatContext _context;
        private CombatSystem _combat;
        private Fixture _reference;

        [OneTimeSetUp] public void LoadReference() => _reference = ReadFixture();

        [SetUp] public void Setup()
        {
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
            var fixture = _reference;
            _units = new UnitField(new HexLayout(fixture.hex, fixture.ox, fixture.oy));
            _buildings = new BuildingMap();
            _terrain = new TerrainMap();
            foreach (var h in Board.AllCells()) _terrain[h] = TerrainType.Plains;
            _context = new CombatContext { FactionForSide = side => "greek" };
            _combat = new CombatSystem(_units, _buildings, _terrain, _db, _context);
        }

        private Unit Spawn(UnitState s)
        {
            var u = _units.Spawn(s.side, s.def, new Hex(s.c, s.r));
            u.TakeDamage(u.MaxHp - s.hp);
            u.MoveTo(new BoardPoint(s.x, s.y));
            u.SetRoute(s.route != null && s.route.Length > 0 ? s.route.Select(h => new Hex(h.c, h.r)) : null);
            typeof(Unit).GetProperty(nameof(Unit.RouteIndex)).SetValue(u, s.index);
            u.Free = s.free; u.Hold = s.hold;
            u.BuffUntil = s.buffUntil; u.Combat.BuffDamage = s.buffDmg;
            u.StunUntil = s.stunUntil; u.RootUntil = s.rootUntil; u.SlowUntil = s.slowUntil;
            u.Combat.InvulnerableUntil = s.invulnUntil;
            u.Combat.AegisUntil = s.aegisUntil; u.Combat.BloodlustUntil = s.bloodlustUntil;
            u.Combat.WeakenUntil = s.weakenUntil; u.Combat.FrenzyUntil = s.frenzyUntil;
            u.Combat.PoisonUntil = s.poisonUntil; u.Combat.PoisonDps = s.poisonDps;
            u.Combat.LastStandEndsAt = s.lastStandEndsAt; u.Combat.RetreatDeadline = s.retreatDeadline;
            u.Combat.AmbushUsed = s.ambushUsed; u.Combat.Invisible = s.invisible;
            u.Combat.HasRevived = s.hasRevived; u.Combat.LastStandUsed = s.lastStandUsed;
            u.Combat.Retreating = s.retreating;
            return u;
        }

        [TestCaseSource(nameof(BrowserCases))]
        public void MatchesBrowser(string name)
        {
            var sample = _reference.cases.Single(c => c.name == name);
            _context.FactionForSide = side => sample.factions[side];
            _context.HasPassive = (side, key) => sample.passives.Any(p => p.side == side && p.key == key);
            _context.ChaosBuffUntil = side => 20f;
            _context.GardenDamage = side => sample.gardenDamage[side];
            foreach (var s in sample.terrain) _terrain[new Hex(s.c, s.r)] = TerrainTable.FromCode(s.code[0]);
            foreach (var s in sample.buildings)
            {
                var b = new Building(s.side, Building.ParseType(s.type), s.type, new Hex(s.c0, s.r0), new Hex(s.c1, s.r1), s.maxHp);
                b.TakeDamage(b.MaxHp - s.hp); b.InvulnerableUntil = s.invulnUntil;
                _buildings.Add(b);
            }
            foreach (var s in sample.units) Spawn(s);
            _context.Resources[0] = _context.Resources[1] = new Purse(10f, 10f);
            var attacker = _units.All[0];
            var target = sample.targetBuilding ? new MovementContact(_buildings.All[sample.targetIndex]) : new MovementContact(_units.All[sample.targetIndex]);
            Assert.AreEqual(sample.expectedDamage, _combat.CalculateDamage(attacker, target, sample.elapsed), 0.001f, "calculated damage");
            int deathEvents = 0, buildingEvents = 0;
            _combat.UnitDied += u => deathEvents++;
            _combat.BuildingDestroyed += b => buildingEvents++;
            if (sample.hit) _combat.DealDamage(attacker, target, sample.elapsed);
            Assert.AreEqual(sample.after.Length, _units.All.Count, "spawned copies");
            for (int i = 0; i < sample.after.Length; i++) AssertUnit(sample.after[i], _units.All[i], i);
            for (int i = 0; i < sample.buildingsAfter.Length; i++)
            {
                var s = sample.buildingsAfter[i]; var b = _buildings.All[i];
                Assert.AreEqual(s.hp, b.Hp, 0.001f, "building health");
                Assert.AreEqual(s.dead, b.Dead, "building death");
                Assert.AreEqual(s.lastHitAt, b.LastHitAt, 0.001f, "building hit time");
            }
            for (int side = 0; side < 2; side++)
            {
                Assert.AreEqual(sample.food[side], _context.Resources[side].Food, 0.001f, "food side " + side);
                Assert.AreEqual(sample.favor[side], _context.Resources[side].Favor, 0.001f, "favor side " + side);
                Assert.AreEqual(sample.buildingDamage[side], _combat.BuildingDamage[side], 0.001f, "building damage side " + side);
            }
            Assert.AreEqual(sample.deaths.Length, _combat.DeadHumans.Count);
            for (int i = 0; i < sample.deaths.Length; i++)
            {
                var s = sample.deaths[i]; var d = _combat.DeadHumans[i];
                Assert.AreEqual(s.key, d.Key); Assert.AreEqual(s.side, d.Side);
                Assert.AreEqual(new Hex(s.c, s.r), d.Hex); Assert.AreEqual(s.at, d.At, 0.001f);
            }
            Assert.AreEqual(sample.after.Count(s => s.dead) - sample.units.Count(s => s.dead), deathEvents);
            Assert.AreEqual(sample.buildingsAfter.Count(s => s.dead) - sample.buildings.Count(s => s.dead), buildingEvents);
        }

        private static void AssertUnit(UnitState s, Unit u, int index)
        {
            string at = "unit " + index + " ";
            // Browser execution marks dead without clearing HP. U9's model represents
            // every death as zero HP; preserve the death outcome, not a corpse's stale HP.
            Assert.AreEqual(s.dead ? 0f : s.hp, u.Hp, 0.002f, at + "health");
            Assert.AreEqual(s.dead, u.Dead, at + "dead");
            Assert.AreEqual(new Hex(s.c, s.r), u.Hex, at + "hex");
            Assert.AreEqual(s.x, u.Position.X, 0.002f, at + "position X");
            Assert.AreEqual(s.y, u.Position.Y, 0.002f, at + "position Y");
            Assert.AreEqual(s.index, u.RouteIndex, at + "route index");
            // JsonUtility reads JSON null arrays as empty arrays.
            CollectionAssert.AreEqual(s.route?.Select(h => new Hex(h.c, h.r)) ?? Enumerable.Empty<Hex>(),
                u.Route ?? (IEnumerable<Hex>)Array.Empty<Hex>(), at + "route");
            Assert.AreEqual(s.free, u.Free, at + "free"); Assert.AreEqual(s.hold, u.Hold, at + "hold");
            Assert.AreEqual(s.ambushUsed, u.Combat.AmbushUsed, at + "ambush");
            Assert.AreEqual(s.invisible, u.Combat.Invisible, at + "invisibility");
            Assert.AreEqual(s.hasRevived, u.Combat.HasRevived, at + "revival");
            Assert.AreEqual(s.lastStandUsed, u.Combat.LastStandUsed, at + "last stand");
            Assert.AreEqual(s.retreating, u.Combat.Retreating, at + "retreat");
            Assert.AreEqual(s.invulnUntil, u.Combat.InvulnerableUntil, 0.001f, at + "invulnerability");
            Assert.AreEqual(s.rootUntil, u.RootUntil, 0.001f, at + "root");
            Assert.AreEqual(s.slowUntil, u.SlowUntil, 0.001f, at + "slow");
            Assert.AreEqual(s.weakenUntil, u.Combat.WeakenUntil, 0.001f, at + "weakness");
            Assert.AreEqual(s.frenzyUntil, u.Combat.FrenzyUntil, 0.001f, at + "frenzy");
            Assert.AreEqual(s.poisonUntil, u.Combat.PoisonUntil, 0.001f, at + "poison");
            Assert.AreEqual(s.poisonDps, u.Combat.PoisonDps, 0.001f, at + "poison damage");
            Assert.AreEqual(s.lastStandEndsAt, u.Combat.LastStandEndsAt, 0.001f, at + "last stand expiry");
            Assert.AreEqual(s.retreatDeadline, u.Combat.RetreatDeadline, 0.001f, at + "retreat expiry");
        }

        private Unit Body(int side, params ExtraValue[] extras) => _units.Spawn(side,
            new UnitData { key = "test", hp = 100f, dmg = 1000f, cat = "human", dtype = "melee", armor = "light", size = 1, extras = extras.ToList() }, new Hex(4, 6 - side));

        [Test] public void DeadTargetCannotTakeAnotherHitOrRepeatRewards()
        {
            var a = Body(0); var t = Body(1);
            _context.HasPassive = (side, key) => key == "bloodFavor" || key == "inevitable";
            int deaths = 0; _combat.UnitDied += u => deaths++;
            _combat.DealDamage(a, t, 10f);
            var purse = _context.Resources[0];
            a.Combat.AmbushUsed = false; a.Combat.Invisible = true;
            Assert.AreEqual(0f, _combat.DealDamage(a, t, 11f));
            Assert.AreEqual(1, deaths); Assert.AreEqual(1, _combat.DeadHumans.Count);
            Assert.AreEqual(purse.Favor, _context.Resources[0].Favor);
            Assert.IsFalse(a.Combat.AmbushUsed); Assert.IsTrue(a.Combat.Invisible);
            Assert.IsFalse(_combat.KillUnit(t, 12f));
            Assert.IsTrue(_units.HasCapacity(t.Hex, t.Flying));
        }

        [Test] public void DestroyedBuildingCannotRepeatRewardsOrDamageCredit()
        {
            var a = Body(0);
            var b = new Building(1, BuildingType.City, "City", new Hex(3, 3), new Hex(3, 4), 10f);
            _buildings.Add(b);
            _context.HasPassive = (side, key) => key == "newGrowth" || key == "deathFavorSurge";
            int events = 0; _combat.BuildingDestroyed += building => events++;
            _combat.DealDamage(a, b, 10f); var purse = _context.Resources[1];
            Assert.AreEqual(0f, _combat.DealDamage(a, b, 11f));
            Assert.AreEqual(10f, _combat.BuildingDamage[0]); Assert.AreEqual(1, events);
            Assert.AreEqual(purse.Favor, _context.Resources[1].Favor); Assert.AreEqual(purse.Food, _context.Resources[1].Food);
            Assert.IsNull(_buildings.StandingAt(b.HexA));
        }

        [Test] public void ArmorSwitchIsOffButCanRestoreItsExportedMatchups()
        {
            Assert.IsFalse(_db.Combat.armorDamageMultipliers);
            var a = Body(0); var t = Body(1); t.Def.armor = "shielded";
            Assert.AreEqual(a.Def.dmg, _combat.CalculateDamage(a, t, 10f));
            _db.Combat.armorDamageMultipliers = true;
            Assert.AreEqual(a.Def.dmg * _db.Combat.Value("armorStrong"), _combat.CalculateDamage(a, t, 10f), 0.001f);
            t.Def.armor = "heavy";
            Assert.AreEqual(a.Def.dmg * _db.Combat.Value("armorWeak"), _combat.CalculateDamage(a, t, 10f), 0.001f);
        }

        [Test] public void UnattributedDeathPaysOnlyDeathIncomeAndHonorsCaps()
        {
            var u = Body(1);
            _context.HasPassive = (side, key) => true;
            _context.Resources[0] = new Purse(_db.FoodCap, _db.FavorCap);
            Assert.IsTrue(_combat.KillUnit(u, 12f));
            Assert.AreEqual(_db.FavorCap, _context.Resources[0].Favor);
            Assert.AreEqual(0f, _context.Resources[1].Favor);
            Assert.AreEqual(0f, _context.Resources[1].Food);
            Assert.AreEqual(12f, _combat.DeadHumans.Single().At);
        }
    }
}
