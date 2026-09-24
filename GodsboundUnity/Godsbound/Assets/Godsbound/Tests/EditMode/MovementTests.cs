using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Movement;
using Godsbound.Core.Pathfinding;
using Godsbound.Core.Training;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    public class MovementTests
    {
        [Serializable] public class MovementFrame
        {
            public float x, y, wait, deviation;
            public int c, r, index;
            public bool arrived, free, sidestepped;
        }
        [Serializable] public class RouteTrace
        {
            public string name, terrain, faction, key;
            public int side;
            public bool flying, blocked, underFire;
            public float dt;
            public MovementFrame[] samples;
        }
        [Serializable] public class MovementFixture
        {
            public string source;
            public float hex, ox, oy;
            public RouteTrace[] routes;
            public HexReference.NeighborRef deviationTarget;
            public MovementFrame[] deviation;
        }

        private GameDatabase _db;
        private MovementFixture _reference;
        private TerrainMap _terrain;
        private BuildingMap _buildings;
        private UnitField _units;
        private RouteMovement _movement;
        private MovementContext _context;
        private UnitData Spear => _db.Unit("egypt", "spear");
        private UnitData Flyer => _db.Unit("egypt", "phoenix");

        [SetUp]
        public void Setup()
        {
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
            _reference = JsonUtility.FromJson<MovementFixture>(File.ReadAllText(Path.Combine(
                Application.dataPath, "Godsbound/Tests/EditMode/Fixtures/movement_reference.json")));
            Reset(new HexLayout(_reference.hex, _reference.ox, _reference.oy));
        }

        private void Reset(HexLayout layout)
        {
            _terrain = new TerrainMap();
            _buildings = new BuildingMap();
            _units = new UnitField(layout);
            _context = new MovementContext { FactionForSide = side => "greek" };
            _movement = new RouteMovement(_units, _buildings, _terrain,
                new PathFinder(_terrain, _buildings.RouteBlocker, _db.Pathfinding, jitter: false),
                _db.Movement, _context);
        }

        [TestCase("ground-P")][TestCase("ground-D")][TestCase("ground-F")]
        [TestCase("ground-W")][TestCase("ground-R")][TestCase("ground-H")]
        [TestCase("flying-P")][TestCase("flying-D")][TestCase("flying-F")]
        [TestCase("flying-W")][TestCase("flying-R")][TestCase("flying-H")]
        [TestCase("flying-mountains")][TestCase("egypt-water")]
        [TestCase("china-own-forest")][TestCase("china-enemy-forest")]
        [TestCase("routed-friendly-wait")][TestCase("under-fire")]
        public void FixedRouteMatchesEveryBrowserFrame(string name)
        {
            var trace = _reference.routes.Single(r => r.name == name);
            _context.FactionForSide = side => trace.faction;
            foreach (var h in Board.AllCells()) _terrain[h] = TerrainTable.FromCode(trace.terrain[0]);
            var def = trace.flying ? Flyer : Spear;
            var u = _units.Spawn(trace.side, def, new Hex(4, 9));
            u.SetRoute(new[] { new Hex(4, 9), new Hex(4, 8), new Hex(4, 7) });
            Unit blocker = null;
            if (trace.blocked)
            {
                blocker = _units.Spawn(trace.side, def, new Hex(4, 8));
                blocker.SetRoute(new[] { new Hex(4, 7), new Hex(4, 6) });
            }
            for (int i = 0; i < trace.samples.Length; i++)
            {
                if (i == 30 && blocker != null) blocker.Kill();
                if (trace.underFire) u.TakeDamage(0.1f);
                _movement.Tick(u, trace.dt, i * trace.dt);
                var f = trace.samples[i];
                string at = name + " frame " + i;
                Assert.AreEqual(f.x, u.Position.X, 0.002f, at + " X");
                Assert.AreEqual(f.y, u.Position.Y, 0.002f, at + " Y");
                Assert.AreEqual(new Hex(f.c, f.r), u.Hex, at + " reserved hex");
                Assert.AreEqual(f.index, u.RouteIndex, at + " route progress");
                Assert.AreEqual(f.arrived, u.Arrived, at + " arrival");
                Assert.AreEqual(f.free, u.Free, at + " route finished");
                Assert.AreEqual(f.wait, u.WaitSeconds, 0.0001f, at + " waiting");
            }
            Assert.IsTrue(u.Free, "travel time includes the whole route");
            Assert.AreEqual(new Hex(4, 7), u.Hex);
        }

        private Unit DeviationSetup()
        {
            var u = _units.Spawn(0, Spear, new Hex(4, 8));
            u.SetRoute(new[] { new Hex(4, 7), new Hex(4, 6), new Hex(4, 5) });
            u.Hold = true;
            var t = _reference.deviationTarget;
            u.Engagement = new MovementContact(_units.Spawn(1, Spear, new Hex(t.c, t.r)));
            return u;
        }

        [Test]
        public void DeviationWaitsTheFullDelayAndMatchesBrowserPosition()
        {
            var u = DeviationSetup();
            var target = u.Engagement;
            var start = u.Position;
            bool moved = false;
            for (int i = 0; i < _reference.deviation.Length; i++)
            {
                u.Engagement = target;
                _movement.Tick(u, 0.05f, i * 0.05f);
                var f = _reference.deviation[i];
                Assert.AreEqual(f.x, u.Position.X, 0.002f, "frame " + i + " X");
                Assert.AreEqual(f.y, u.Position.Y, 0.002f, "frame " + i + " Y");
                if ((i + 1) * 0.05f < _db.Movement.deviateDelay)
                    Assert.AreEqual(start, u.Position, "no movement before full delay");
                if (!u.Position.Equals(start)) moved = true;
            }
            Assert.IsTrue(moved, "a sustained reason to deviate eventually moves the unit");
        }

        [Test]
        public void VanishingThreatClearsContinuousDeviationTimer()
        {
            var u = DeviationSetup();
            var target = u.Engagement;
            _movement.Tick(u, 0.1f, 0f);
            Assert.Greater(u.DeviateSeconds, 0f);
            target.Unit.Kill(); u.Engagement = target;
            _movement.Tick(u, 0.1f, 0.1f);
            Assert.AreEqual(0f, u.DeviateSeconds);
            Assert.IsFalse(u.Sidestepped);
        }

        [Test]
        public void FreeUnitsDoNotWaitToChase()
        {
            var u = DeviationSetup();
            u.SetRoute(null);
            var before = u.Position;
            _movement.Tick(u, 0.05f, 0f);
            Assert.AreNotEqual(before, u.Position);
            Assert.AreEqual(0f, u.DeviateSeconds);
        }

        [TestCase(false, false)][TestCase(true, true)]
        [TestCase(false, true)][TestCase(true, false)]
        public void OccupancyIsOnePerLayer(bool followerFlying, bool leaderFlying)
        {
            var start = new Hex(4, 8); var next = new Hex(4, 7);
            var follower = _units.Spawn(0, followerFlying ? Flyer : Spear, start);
            var leader = _units.Spawn(0, leaderFlying ? Flyer : Spear, next);
            follower.SetRoute(new[] { next });
            leader.SetRoute(new[] { new Hex(4, 6) });
            for (int i = 0; i < 40; i++) _movement.Tick(follower, 0.05f, i * 0.05f);
            Assert.AreEqual(followerFlying == leaderFlying ? start : next, follower.Hex);
            Assert.AreEqual(1, _units.HexLoad(follower.Hex, followerFlying));
        }

        [Test]
        public void NextHexIsReservedBeforeTheSpriteArrives()
        {
            var next = new Hex(4, 7);
            var a = _units.Spawn(0, Spear, new Hex(4, 8));
            var b = _units.Spawn(0, Spear, new Hex(3, 7));
            a.SetRoute(new[] { next }); b.SetRoute(new[] { next });
            _movement.Tick(a, 0.01f, 0f); _movement.Tick(b, 0.01f, 0f);
            Assert.AreEqual(next, a.Hex); Assert.IsFalse(a.Arrived);
            Assert.AreEqual(1, _units.HexLoad(next, false));
            Assert.AreNotEqual(next, b.Hex);
        }

        [Test]
        public void SidestepsAlsoReserveTheirDestination()
        {
            var u = DeviationSetup();
            var target = u.Engagement;
            for (int i = 0; i < 8; i++) { u.Engagement = target; _movement.Tick(u, 0.05f, i * 0.05f); }
            Assert.IsFalse(u.Arrived);
            Assert.AreNotEqual(new Hex(4, 8), u.Hex);
            var other = _units.Spawn(0, Spear, new Hex(3, 8));
            other.SetRoute(new[] { u.Hex });
            _movement.Tick(other, 0.05f, 1f);
            Assert.AreEqual(1, _units.HexLoad(u.Hex, false));
        }

        [Test]
        public void ReturningToRouteUsesLegalAdjacentSteps()
        {
            _terrain[4, 7] = TerrainType.Mountain;
            var u = _units.Spawn(0, Spear, new Hex(4, 8));
            u.SetRoute(new[] { new Hex(4, 6) });
            var previous = u.Hex;
            for (int i = 0; i < 300 && u.HasRoute; i++)
            {
                _movement.Tick(u, 0.05f, i * 0.05f);
                if (previous != u.Hex)
                {
                    Assert.AreEqual(1, Hex.Distance(previous, u.Hex));
                    Assert.IsTrue(_terrain.RouteOk(u.Hex, false, _buildings.RouteBlocker));
                }
                previous = u.Hex;
            }
            Assert.AreEqual(new Hex(4, 6), u.Hex);
            Assert.IsFalse(u.HasRoute);
        }

        [Test]
        public void AttackRangeFinishesTheSlideBeforeAllowingAnAttack()
        {
            var u = _units.Spawn(0, Spear, new Hex(4, 8));
            u.SetRoute(new[] { new Hex(4, 7) });
            _movement.Tick(u, 0.05f, 0f);
            u.Engagement = new MovementContact(_units.Spawn(1, Spear, new Hex(4, 6)));
            Assert.IsFalse(_movement.Tick(u, 0.05f, 0.05f));
            for (int i = 2; i < 100 && !u.Arrived; i++) _movement.Tick(u, 0.05f, i * 0.05f);
            var before = u.Position;
            Assert.IsTrue(_movement.Tick(u, 0.05f, 5f));
            Assert.AreEqual(before, u.Position);
        }

        [Test]
        public void ScaleAndHeadingDoNotChangeTravelTime()
        {
            var frames = new List<int>();
            foreach (float scale in new[] { 1f, _reference.hex })
                foreach (var goal in new[] { new Hex(4, 7), new Hex(5, 8) })
                {
                    Reset(new HexLayout(scale, 0f, 0f));
                    var u = _units.Spawn(0, Spear, new Hex(4, 8)); u.SetRoute(new[] { goal });
                    int count = 0;
                    while (u.HasRoute && count < 200) { _movement.Tick(u, 0.02f, count * 0.02f); count++; }
                    Assert.Less(count, 200); frames.Add(count);
                }
            Assert.IsTrue(frames.All(f => f == frames[0]));
        }

        [Test]
        public void TrainingCarriesAnIndependentRouteIntoMovement()
        {
            var route = new List<Hex> { new Hex(4, 8), new Hex(4, 7) };
            var queue = new TrainingQueue(_db.Training);
            queue.TryEnqueue(new Purse(1000, 1000), 0, Spear, route[0], null,
                TrainingModifiers.None, 0f, out _, out var order, route);
            route.Clear();
            var u = queue.Tick(order.ReadyAt, _units, _buildings).Single();
            Assert.AreEqual(2, u.Route.Count); Assert.IsFalse(u.Free);
            for (int i = 0; i < 100 && u.HasRoute; i++) _movement.Tick(u, 0.05f, order.ReadyAt + i * 0.05f);
            Assert.AreEqual(new Hex(4, 7), u.Hex); Assert.IsFalse(u.HasRoute);
        }

        [Test]
        public void EnemyInTheNextHexBlocksEntry()
        {
            var start = new Hex(4, 8); var next = new Hex(4, 7);
            var u = _units.Spawn(0, Spear, start); u.SetRoute(new[] { next });
            _units.Spawn(1, Spear, next);
            _movement.Tick(u, 0.05f, 0f);
            Assert.AreEqual(start, u.Hex); Assert.AreEqual(_units.Layout.Center(start), u.Position);
            Assert.AreEqual(1, _units.HexLoad(next, false));
        }

        [Test]
        public void StationaryJamDetoursWithoutWalkingIntoTheOccupiedHex()
        {
            var start = new Hex(4, 8); var next = new Hex(4, 7);
            var u = _units.Spawn(0, Spear, start); u.SetRoute(new[] { next, new Hex(4, 6) });
            _units.Spawn(0, Spear, next);
            for (int i = 0; i < 30; i++)
            {
                _movement.Tick(u, 0.05f, i * 0.05f);
                Assert.AreNotEqual(next, u.Hex);
            }
            Assert.AreNotEqual(start, u.Hex, "stationary congestion can be sidestepped");
        }

        [Test]
        public void DestroyedBuildingsAreWalkableButStandingBuildingsAreNot()
        {
            var next = new Hex(4, 7);
            var building = new Building(1, BuildingType.City, "test", next, new Hex(5, 7), 100f);
            _buildings.Add(building);
            var u = _units.Spawn(0, Spear, new Hex(4, 8)); u.SetRoute(new[] { next });
            _movement.Tick(u, 0.05f, 0f);
            Assert.AreNotEqual(next, u.Hex);
            building.TakeDamage(building.MaxHp);
            u.SetRoute(new[] { next }); _movement.Tick(u, 0.05f, 0.05f);
            Assert.AreEqual(next, u.Hex);
        }

        [Test]
        public void BuildingOnlyUnitsNeverDeviateToAttackUnits()
        {
            var def = _db.AllUnits.First(d => d.GetBool("targetsBuildings"));
            var u = _units.Spawn(0, def, new Hex(4, 8));
            u.SetRoute(new[] { new Hex(4, 7) }); u.Hold = true;
            var enemy = _units.Spawn(1, Spear, new Hex(5, 6));
            for (int i = 0; i < 30; i++)
            { u.Engagement = new MovementContact(enemy); _movement.Tick(u, 0.05f, i * 0.05f); }
            Assert.AreEqual(new Hex(4, 8), u.Hex); Assert.IsNull(u.Engagement);
            Assert.AreEqual(0f, u.DeviateSeconds);
        }

        [Test]
        public void FriendlyCombatAssistPreservesTheDrawnRoute()
        {
            var u = _units.Spawn(0, Spear, new Hex(4, 8));
            var route = new[] { new Hex(4, 7), new Hex(4, 6) }; u.SetRoute(route);
            var leader = _units.Spawn(0, Spear, new Hex(4, 7));
            leader.Engagement = new MovementContact(_units.Spawn(1, Spear, new Hex(5, 6)));
            _movement.Tick(u, 0.05f, 0f);
            Assert.AreSame(leader.Engagement, u.Engagement);
            CollectionAssert.AreEqual(route, u.Route);
            Assert.AreEqual(0, u.RouteIndex);
            Assert.AreEqual(1, _units.HexLoad(u.Hex, false));
        }

        [Test]
        public void StunAndRootExpireUsingElapsedTime()
        {
            var u = _units.Spawn(0, Spear, new Hex(4, 8));
            u.SetRoute(new[] { new Hex(4, 7) }); u.StunUntil = 5f; u.RootUntil = 6f;
            var before = u.Position;
            _movement.Tick(u, 0.1f, 4f); Assert.AreEqual(before, u.Position);
            _movement.Tick(u, 0.1f, 5f); Assert.AreEqual(before, u.Position);
            _movement.Tick(u, 0.1f, 6f); Assert.AreNotEqual(before, u.Position);
        }
    }
}
