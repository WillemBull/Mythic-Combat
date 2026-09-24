using System.Linq;
using NUnit.Framework;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>Buildings: their footprint, the hex index, and route blocking.</summary>
    public class BuildingTests
    {
        private static GameDatabase _db;

        [SetUp]
        public void Load()
        {
            if (_db != null) return;
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        private static BuildingMap Map() => BuildingMap.FromExport(_db.Buildings);

        // ---- the export -------------------------------------------------------------

        [Test]
        public void SixBuildingsWereExportedThreePerSide()
        {
            Assert.AreEqual(6, _db.Buildings.Count);
            foreach (var side in new[] { 0, 1 })
            {
                var mine = _db.Buildings.Where(b => b.side == side).ToList();
                Assert.AreEqual(3, mine.Count, $"side {side} should have three buildings");
                CollectionAssert.AreEquivalent(
                    new[] { "city", "temple", "fortress" },
                    mine.Select(b => b.type).ToList(),
                    $"side {side} should have one of each type");
            }
        }

        /// <summary>HP comes from the export, never a literal in the C#.</summary>
        [Test]
        public void BuildingHpComesFromTheExport()
        {
            var map = Map();
            foreach (var d in _db.Buildings)
            {
                var b = map.Of(d.side, Building.ParseType(d.type));
                Assert.IsNotNull(b);
                Assert.AreEqual(d.maxHp, b.MaxHp, 1e-3f, $"{d.type} max hp");
                Assert.AreEqual(b.MaxHp, b.Hp, 1e-3f, "buildings start at full health");
            }
        }

        /// <summary>The fortress is the toughest, the temple the softest.</summary>
        [Test]
        public void BuildingHpOrderingIsFortressCityTemple()
        {
            var map = Map();
            float fort = map.Of(0, BuildingType.Fortress).MaxHp;
            float city = map.Of(0, BuildingType.City).MaxHp;
            float temple = map.Of(0, BuildingType.Temple).MaxHp;
            Assert.Greater(fort, city);
            Assert.Greater(city, temple);
        }

        [Test]
        public void EverySideHasSymmetricHpByType()
        {
            var map = Map();
            foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
                Assert.AreEqual(map.Of(0, t).MaxHp, map.Of(1, t).MaxHp, 1e-3f,
                    $"{t} should be equally tough for both sides");
        }

        // ---- footprint --------------------------------------------------------------

        [Test]
        public void EveryBuildingOccupiesTwoAdjacentInBoundsHexes()
        {
            foreach (var b in Map().All)
            {
                Assert.AreEqual(2, b.Hexes().Count(), "exactly two hexes");
                Assert.IsTrue(Board.InBounds(b.HexA), $"{b} hex A on the board");
                Assert.IsTrue(Board.InBounds(b.HexB), $"{b} hex B on the board");
                Assert.AreEqual(1, Hex.Distance(b.HexA, b.HexB), $"{b} hexes must be adjacent");
            }
        }

        [Test]
        public void EachSidesBuildingsStandInItsOwnHalf()
        {
            foreach (var b in Map().All)
            {
                var want = b.Side == 0 ? BoardSide.Player : BoardSide.Ai;
                foreach (var h in b.Hexes())
                    Assert.AreEqual(want, Board.SideForRow(h.R),
                        $"{b} hex {h} should be in its owner's half, not no-man's land");
            }
        }

        [Test]
        public void NonAdjacentOrOffBoardFootprintsAreRejectedAtConstruction()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new Building(0, BuildingType.City, "City", new Hex(0, 8), new Hex(4, 8), 100f),
                "non-adjacent hexes must be refused");
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new Building(0, BuildingType.City, "City", new Hex(-1, 8), new Hex(0, 8), 100f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new Building(0, BuildingType.City, "City", new Hex(4, 8), new Hex(5, 8), 0f),
                "a building needs positive hp");
        }

        // ---- the hex index ----------------------------------------------------------

        [Test]
        public void TheMapResolvesBothHexesOfEveryBuilding()
        {
            var map = Map();
            foreach (var b in map.All)
                foreach (var h in b.Hexes())
                    Assert.AreSame(b, map.AnyAt(h), $"{h} should resolve to {b}");
        }

        [Test]
        public void HexesWithNoBuildingResolveToNull()
        {
            var map = Map();
            var empty = Board.AllCells().First(h => map.AnyAt(h) == null);
            Assert.IsNull(map.StandingAt(empty));
            Assert.IsNull(map.AnyAt(new Hex(-1, 0)), "off-board is null, not an exception");
        }

        [Test]
        public void TwoBuildingsClaimingTheSameHexIsRejected()
        {
            var a = new Building(0, BuildingType.City, "City", new Hex(4, 8), new Hex(5, 8), 100f);
            var b = new Building(0, BuildingType.Temple, "Temple", new Hex(5, 8), new Hex(6, 8), 100f);
            Assert.Throws<System.InvalidOperationException>(() => new BuildingMap(new[] { a, b }),
                "overlapping footprints must fail loudly, not resolve by list order");
        }

        // ---- route blocking: the reason U7 comes first ------------------------------

        /// <summary>
        /// This is what U7 exists to supply: <see cref="TerrainMap.RouteOk"/> has always
        /// accepted a building predicate and until now nothing provided one.
        /// </summary>
        [Test]
        public void StandingBuildingsBlockRoutesForBothLayers()
        {
            var terrain = new TerrainMap();
            var map = Map();
            var blocker = map.RouteBlocker;

            foreach (var b in map.All)
                foreach (var h in b.Hexes())
                {
                    Assert.IsFalse(terrain.RouteOk(h, false, blocker), $"{h} under {b} blocks ground");
                    Assert.IsFalse(terrain.RouteOk(h, true, blocker), $"{h} under {b} blocks flying");
                }
        }

        [Test]
        public void DestroyedBuildingsStopBlockingRoutes()
        {
            var terrain = new TerrainMap();
            var map = Map();
            var blocker = map.RouteBlocker;
            var city = map.Of(0, BuildingType.City);

            Assert.IsFalse(terrain.RouteOk(city.HexA, false, blocker), "standing city blocks");

            city.TakeDamage(city.MaxHp);
            Assert.IsTrue(city.Dead);

            Assert.IsTrue(terrain.RouteOk(city.HexA, false, blocker), "rubble is walkable");
            Assert.IsTrue(terrain.RouteOk(city.HexB, false, blocker));
            Assert.IsNull(map.StandingAt(city.HexA), "a dead building reports nothing standing");
            Assert.AreSame(city, map.AnyAt(city.HexA), "but it still occupies the hex for rendering");
        }

        [Test]
        public void EveryHexNotUnderABuildingStaysRoutable()
        {
            var terrain = new TerrainMap();
            var map = Map();
            var blocker = map.RouteBlocker;

            foreach (var h in Board.AllCells())
                if (map.AnyAt(h) == null)
                    Assert.IsTrue(terrain.RouteOk(h, false, blocker),
                        $"{h} has no building and the base map has no mountains");
        }

        // ---- damage and objectives --------------------------------------------------

        [Test]
        public void DamageAccumulatesAndFloorsAtZero()
        {
            var b = Map().Of(0, BuildingType.Temple);
            float absorbed = b.TakeDamage(100f);
            Assert.AreEqual(100f, absorbed, 1e-3f);
            Assert.AreEqual(100f, b.DamageTaken, 1e-3f);

            absorbed = b.TakeDamage(b.MaxHp * 10f);
            Assert.AreEqual(b.MaxHp - 100f, absorbed, 1e-3f, "only the remaining hp is absorbed");
            Assert.AreEqual(0f, b.Hp, 1e-3f, "hp floors at zero, never negative");
            Assert.IsTrue(b.Dead);

            Assert.Throws<System.ArgumentOutOfRangeException>(() => b.TakeDamage(-1f));
        }

        [Test]
        public void StandingCountAndAllDestroyedTrackTheWinCondition()
        {
            var map = Map();
            Assert.AreEqual(3, map.StandingCount(0));
            Assert.IsFalse(map.AllDestroyed(0));

            foreach (var b in map.Of(0).ToList()) b.TakeDamage(b.MaxHp);

            Assert.AreEqual(0, map.StandingCount(0));
            Assert.IsTrue(map.AllDestroyed(0), "all three down is an instant loss");
            Assert.AreEqual(3, map.StandingCount(1), "the other side is untouched");
        }

        [Test]
        public void ResetRestoresEveryBuilding()
        {
            var map = Map();
            foreach (var b in map.All) b.TakeDamage(b.MaxHp);
            map.ResetAll();
            Assert.AreEqual(3, map.StandingCount(0));
            Assert.AreEqual(3, map.StandingCount(1));
            Assert.AreEqual(0f, map.DamageTaken(0), 1e-3f);
        }

        // ---- placement legality -----------------------------------------------------

        [Test]
        public void PlacementNeedsTwoAdjacentInBoundsHexes()
        {
            var terrain = new TerrainMap();
            var empty = new BuildingMap();

            Assert.IsTrue(BuildingPlacement.CanPlace(new Hex(4, 8), new Hex(5, 8), terrain, empty));
            Assert.IsFalse(BuildingPlacement.CanPlace(new Hex(0, 8), new Hex(4, 8), terrain, empty),
                "non-adjacent");
            Assert.IsFalse(BuildingPlacement.CanPlace(new Hex(-1, 8), new Hex(0, 8), terrain, empty),
                "off the board");
        }

        [Test]
        public void MountainsAndWaterRefuseBuildings()
        {
            var terrain = new TerrainMap();
            var empty = new BuildingMap();
            var a = new Hex(4, 8);
            var b = a.Neighbors().First();

            terrain[a] = TerrainType.Mountain;
            Assert.IsFalse(BuildingPlacement.CanPlace(a, b, terrain, empty), "no building on mountain");

            terrain[a] = TerrainType.Water;
            Assert.IsFalse(BuildingPlacement.CanPlace(a, b, terrain, empty), "no building on water");

            foreach (var t in new[] { TerrainType.Plains, TerrainType.Desert, TerrainType.Forest,
                                      TerrainType.Road, TerrainType.HighGround })
            {
                terrain[a] = t;
                Assert.IsTrue(BuildingPlacement.CanPlace(a, b, terrain, empty),
                    $"{t} should accept a building");
            }
        }

        [Test]
        public void AnotherBuildingBlocksPlacementButTheMovedOneDoesNot()
        {
            var terrain = new TerrainMap();
            var map = Map();
            var city = map.Of(0, BuildingType.City);

            Assert.IsFalse(BuildingPlacement.CanPlace(city.HexA, city.HexB, terrain, map),
                "the city's own hexes are occupied");
            Assert.IsTrue(BuildingPlacement.CanPlace(city.HexA, city.HexB, terrain, map, ignoring: city),
                "re-placing a building onto its own hexes is legal");
        }

        [Test]
        public void HalfRestrictionRejectsNoMansLandAndTheEnemyHalf()
        {
            var neutral = new Hex(4, Board.AiRows);
            Assert.AreEqual(BoardSide.Neutral, Board.SideForRow(neutral.R), "row is no-man's land");

            Assert.IsFalse(BuildingPlacement.IsInHalf(neutral, neutral.Neighbors().First(), 0),
                "no-man's land is nobody's half");
            Assert.IsFalse(BuildingPlacement.IsInHalf(new Hex(4, 1), new Hex(5, 1), 0),
                "the player cannot build in the AI's half");
            Assert.IsTrue(BuildingPlacement.IsInHalf(new Hex(4, 1), new Hex(5, 1), 1));
        }

        /// <summary>
        /// Every legal footprint is genuinely legal, and there are plenty of them on an
        /// otherwise-empty half.
        /// </summary>
        [Test]
        public void LegalFootprintsAreAllValidAndEachPairAppearsOnce()
        {
            var terrain = new TerrainMap();
            var empty = new BuildingMap();
            var spots = BuildingPlacement.LegalFootprints(0, terrain, empty).ToList();

            Assert.Greater(spots.Count, 10, "an empty half should offer many footprints");

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var (a, b) in spots)
            {
                Assert.IsTrue(BuildingPlacement.CanPlace(a, b, terrain, empty), $"{a}-{b}");
                Assert.IsTrue(BuildingPlacement.IsInHalf(a, b, 0), $"{a}-{b} in the player's half");

                var lo = System.Math.Min(Board.Index(a), Board.Index(b));
                var hi = System.Math.Max(Board.Index(a), Board.Index(b));
                Assert.IsTrue(seen.Add($"{lo}-{hi}"), $"{a}-{b} listed twice");
            }
        }
    }
}
