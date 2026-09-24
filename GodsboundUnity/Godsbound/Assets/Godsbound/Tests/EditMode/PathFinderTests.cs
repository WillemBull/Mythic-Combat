using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Pathfinding;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// Pathfinding. Deliberately asserts PROPERTIES, never exact paths.
    /// </summary>
    /// <remarks>
    /// <c>findPath</c> adds random jitter per edge so units do not all funnel down one lane,
    /// which means a repeated query can legitimately return a different near-equal route.
    /// HANDOFF is explicit that tests must not assume path identity. Everything here checks
    /// legality, reachability and bounds instead; the deterministic core is exercised
    /// separately with jitter switched off.
    /// </remarks>
    public class PathFinderTests
    {
        private static GameDatabase _db;

        [SetUp]
        public void Load()
        {
            if (_db != null) return;
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        private static PathFinder Finder(TerrainMap t, BuildingMap b = null, int? seed = null,
                                         bool jitter = true)
        {
            var rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random(12345);
            return new PathFinder(t, b?.RouteBlocker, _db.Pathfinding, rng, jitter);
        }

        private static void AssertPathIsWellFormed(List<Hex> path, Hex start, Hex goal,
                                                   TerrainMap terrain, BuildingMap buildings, bool flying)
        {
            Assert.IsNotNull(path, $"expected a path from {start} to {goal}");
            Assert.AreEqual(start, path[0], "a path must begin at the start");
            Assert.AreEqual(goal, path[path.Count - 1], "a path must end at the goal");

            for (int i = 0; i < path.Count; i++)
            {
                Assert.IsTrue(Board.InBounds(path[i]), $"step {i} {path[i]} left the board");

                // The START is exempt: findPath never route-checks it, on purpose.
                if (i > 0)
                    Assert.IsTrue(terrain.RouteOk(path[i], flying, buildings?.RouteBlocker),
                        $"step {i} {path[i]} is not a legal route hex");

                if (i > 0)
                    Assert.AreEqual(1, Hex.Distance(path[i - 1], path[i]),
                        $"steps {i - 1} and {i} are not adjacent");
            }

            Assert.AreEqual(path.Count, path.Distinct().Count(), "a path must not repeat a hex");
        }

        // ---- constants ---------------------------------------------------------------

        [Test]
        public void ConstantsWereExtractedFromFindPath()
        {
            Assert.AreEqual(0.05f, _db.Pathfinding.edgeJitter, 1e-6f, "per-edge jitter");
            Assert.AreEqual(0.3f, _db.Pathfinding.speedFloor, 1e-6f, "speed floor");
        }

        [Test]
        public void AZeroSpeedFloorIsRejected()
        {
            var bad = new PathfindingRates { edgeJitter = 0.05f, speedFloor = 0f };
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new PathFinder(new TerrainMap(), null, bad),
                "a zero floor would divide by zero on impassable terrain");
        }

        // ---- basic routing -----------------------------------------------------------

        [Test]
        public void FindsAPathAcrossAnEmptyBoard()
        {
            var t = new TerrainMap();
            var start = new Hex(0, 0);
            var goal = new Hex(8, 12);

            var path = Finder(t).FindPath(start, goal, flying: false);
            AssertPathIsWellFormed(path, start, goal, t, null, false);
        }

        [Test]
        public void AGoalEqualToTheStartReturnsASingleHex()
        {
            var t = new TerrainMap();
            var here = new Hex(4, 6);
            var path = Finder(t).FindPath(here, here, flying: false);

            Assert.IsNotNull(path);
            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(here, path[0]);
        }

        [Test]
        public void NoGoalsMeansNoPath()
        {
            var t = new TerrainMap();
            Assert.IsNull(Finder(t).FindPath(new Hex(0, 0), new Hex[0], false));
            Assert.IsNull(Finder(t).FindPath(new Hex(0, 0), null, false));
        }

        [Test]
        public void AnOffBoardStartOrGoalYieldsNoPath()
        {
            var t = new TerrainMap();
            Assert.IsNull(Finder(t).FindPath(new Hex(-1, 0), new Hex(4, 4), false));
            Assert.IsNull(Finder(t).FindPath(new Hex(0, 0), new Hex(99, 99), false),
                "an off-board goal is filtered out, leaving no goals");
        }

        /// <summary>Multiple goals: the path must end on one of them.</summary>
        [Test]
        public void WithSeveralGoalsThePathEndsOnOneOfThem()
        {
            var t = new TerrainMap();
            var goals = new[] { new Hex(0, 12), new Hex(4, 12), new Hex(8, 12) };
            var path = Finder(t).FindPath(new Hex(4, 0), goals, false);

            Assert.IsNotNull(path);
            CollectionAssert.Contains(goals, path[path.Count - 1]);
        }

        // ---- blocking ----------------------------------------------------------------

        [Test]
        public void MountainsBlockGroundButNotFlying()
        {
            var t = new TerrainMap();
            // Wall row 6 completely, cutting the halves apart for ground movers.
            for (int c = 0; c < Board.Cols; c++) t[c, 6] = TerrainType.Mountain;

            var start = new Hex(4, 0);
            var goal = new Hex(4, 12);

            Assert.IsNull(Finder(t).FindPath(start, goal, flying: false),
                "a full-width mountain wall must stop a ground unit");

            var air = Finder(t).FindPath(start, goal, flying: true);
            AssertPathIsWellFormed(air, start, goal, t, null, true);
        }

        [Test]
        public void AWalledOffGoalReturnsNoPath()
        {
            var t = new TerrainMap();
            var goal = new Hex(4, 6);
            foreach (var n in goal.Neighbors()) t[n] = TerrainType.Mountain;

            Assert.IsNull(Finder(t).FindPath(new Hex(0, 0), goal, false),
                "a goal ringed by mountains is unreachable on the ground");
        }

        [Test]
        public void StandingBuildingsAreRoutedAround()
        {
            var t = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var city = buildings.Of(0, BuildingType.City);

            var start = new Hex(4, 0);
            var goal = new Hex(0, 12);
            var path = Finder(t, buildings).FindPath(start, goal, false);

            AssertPathIsWellFormed(path, start, goal, t, buildings, false);
            CollectionAssert.DoesNotContain(path, city.HexA, "must not walk through a standing city");
            CollectionAssert.DoesNotContain(path, city.HexB);
        }

        [Test]
        public void RubbleIsWalkedOverOnceTheBuildingIsDestroyed()
        {
            var t = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var city = buildings.Of(0, BuildingType.City);

            Assert.IsNull(Finder(t, buildings).FindPath(new Hex(4, 0), city.HexA, false),
                "a standing building is not a reachable goal");

            city.TakeDamage(city.MaxHp);

            var path = Finder(t, buildings).FindPath(new Hex(4, 0), city.HexA, false);
            AssertPathIsWellFormed(path, new Hex(4, 0), city.HexA, t, buildings, false);
        }

        /// <summary>
        /// The start hex is never route-checked, so a unit sitting ON a building — which
        /// emergency placement does — can still path out. Checking it would strand them.
        /// </summary>
        [Test]
        public void AUnitStandingOnABuildingCanStillPathOut()
        {
            var t = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var city = buildings.Of(0, BuildingType.City);

            // Pick a goal that is genuinely open. Hardcoding one bit here: an earlier
            // version used (4,0), which a sampled AI layout happened to build on, so the
            // path was correctly null and the test looked like a port bug.
            var goal = Board.AllCells().First(h =>
                buildings.AnyAt(h) == null && t.RouteOk(h, false, buildings.RouteBlocker));

            var path = Finder(t, buildings).FindPath(city.HexA, goal, false);

            Assert.IsNotNull(path, "a unit on a building must not be stranded");
            Assert.AreEqual(city.HexA, path[0]);
            AssertPathIsWellFormed(path, city.HexA, goal, t, buildings, false);
        }

        // ---- costs -------------------------------------------------------------------

        [Test]
        public void StepCostIsTheInverseOfTerrainSpeed()
        {
            var t = new TerrainMap();
            var f = Finder(t);
            var h = new Hex(4, 4);

            foreach (var type in TerrainTable.AllTypes)
            {
                t[h] = type;
                float speed = TerrainTable.Info(type).Speed;
                float expected = 1f / System.Math.Max(speed, _db.Pathfinding.speedFloor);
                Assert.AreEqual(expected, f.StepCost(h, false), 1e-4f,
                    $"ground step cost on {TerrainTable.Info(type).Name}");
                Assert.AreEqual(1f, f.StepCost(h, true), 1e-4f,
                    $"flying should cost 1 regardless of {TerrainTable.Info(type).Name}");
            }
        }

        [Test]
        public void RoadsAreCheaperThanForestWhichIsCheaperThanWater()
        {
            var t = new TerrainMap();
            var f = Finder(t);
            var h = new Hex(4, 4);

            t[h] = TerrainType.Road; float road = f.StepCost(h, false);
            t[h] = TerrainType.Plains; float plains = f.StepCost(h, false);
            t[h] = TerrainType.Forest; float forest = f.StepCost(h, false);
            t[h] = TerrainType.Water; float water = f.StepCost(h, false);

            Assert.Less(road, plains, "roads accelerate movement");
            Assert.Less(plains, forest, "forest slows");
            Assert.Less(forest, water, "water slows more");
        }

        /// <summary>The speed floor caps how expensive one step can get.</summary>
        [Test]
        public void TheSpeedFloorBoundsTheWorstStepCost()
        {
            var t = new TerrainMap();
            var f = Finder(t);
            float worst = 1f / _db.Pathfinding.speedFloor;

            foreach (var type in TerrainTable.AllTypes)
            {
                t[new Hex(4, 4)] = type;
                Assert.LessOrEqual(f.StepCost(new Hex(4, 4), false), worst + 1e-4f);
            }
        }

        /// <summary>
        /// A road laid straight down the board should be preferred over open ground. Checked
        /// as a tendency over many runs, not a single path, because of the jitter.
        /// </summary>
        [Test]
        public void ARoadAttractsRoutesWithoutBeingGuaranteed()
        {
            var t = new TerrainMap();
            const int col = 4;
            for (int r = 0; r < Board.Rows; r++) t[col, r] = TerrainType.Road;

            int onRoad = 0;
            const int runs = 25;
            for (int i = 0; i < runs; i++)
            {
                var path = Finder(t, seed: i).FindPath(new Hex(col, 0), new Hex(col, 12), false);
                Assert.IsNotNull(path);
                if (path.All(h => h.C == col)) onRoad++;
            }

            Assert.Greater(onRoad, runs / 2,
                "most routes should stay on the road, even though jitter can divert some");
        }

        // ---- jitter ------------------------------------------------------------------

        [Test]
        public void WithoutJitterTheSameQueryIsReproducible()
        {
            var t = new TerrainMap();
            var a = Finder(t, jitter: false).WithoutJitter().FindPath(new Hex(0, 0), new Hex(8, 12), false);
            var b = Finder(t, jitter: false).WithoutJitter().FindPath(new Hex(0, 0), new Hex(8, 12), false);

            CollectionAssert.AreEqual(a, b, "with jitter off, routing must be deterministic");
        }

        /// <summary>
        /// Jitter must actually perturb something, or it is not doing its job. Asserted as
        /// "some run differs", never as a specific path.
        /// </summary>
        [Test]
        public void JitterCanChangeTheRouteChosen()
        {
            var t = new TerrainMap();
            var baseline = Finder(t, seed: 1).FindPath(new Hex(0, 0), new Hex(8, 12), false);

            bool sawDifferent = false;
            for (int seed = 2; seed < 40 && !sawDifferent; seed++)
            {
                var other = Finder(t, seed: seed).FindPath(new Hex(0, 0), new Hex(8, 12), false);
                if (!other.SequenceEqual(baseline)) sawDifferent = true;
            }

            Assert.IsTrue(sawDifferent,
                "jitter should produce a different near-equal route across seeds");
        }

        /// <summary>Jitter must not make paths illegal or wildly long.</summary>
        [Test]
        public void EveryJitteredPathStaysLegalAndReasonablyShort()
        {
            var t = new TerrainMap();
            var start = new Hex(0, 0);
            var goal = new Hex(8, 12);
            int floor = Hex.Distance(start, goal);

            for (int seed = 0; seed < 20; seed++)
            {
                var path = Finder(t, seed: seed).FindPath(start, goal, false);
                AssertPathIsWellFormed(path, start, goal, t, null, false);

                Assert.GreaterOrEqual(path.Count - 1, floor,
                    "a path cannot be shorter than the hex distance");
                Assert.LessOrEqual(path.Count - 1, floor * 3,
                    "jitter should nudge the route, not send it wandering");
            }
        }

        // ---- building goal sets ------------------------------------------------------

        [Test]
        public void AdjacentRoutableHexesRingAStandingBuildingWithoutIncludingIt()
        {
            var t = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var city = buildings.Of(0, BuildingType.City);

            var ring = buildings.AdjacentRoutableHexes(city, false, t).ToList();

            Assert.IsNotEmpty(ring);
            CollectionAssert.AllItemsAreUnique(ring, "deduplicated");
            CollectionAssert.DoesNotContain(ring, city.HexA, "its own hexes are not routable");
            CollectionAssert.DoesNotContain(ring, city.HexB);

            foreach (var h in ring)
            {
                Assert.IsTrue(t.RouteOk(h, false, buildings.RouteBlocker), $"{h} routable");
                Assert.IsTrue(city.Hexes().Any(bh => Hex.Distance(bh, h) == 1), $"{h} adjacent");
            }
        }

        [Test]
        public void AUnitCanPathToAttackABuilding()
        {
            var t = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var target = buildings.Of(1, BuildingType.City);

            var goals = buildings.AdjacentRoutableHexes(target, false, t).ToList();
            var start = new Hex(4, 12);

            var path = Finder(t, buildings).FindPath(start, goals, false);
            Assert.IsNotNull(path, "a unit should be able to reach the enemy city");
            CollectionAssert.Contains(goals, path[path.Count - 1]);
        }
    }
}
