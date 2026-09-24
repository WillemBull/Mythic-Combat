using System.Linq;
using NUnit.Framework;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>Unit instances, per-layer hex capacity, and emergency spawn placement.</summary>
    public class UnitFieldTests
    {
        private static GameDatabase _db;

        [SetUp]
        public void Load()
        {
            if (_db != null) return;
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        private static UnitData Ground() => _db.Unit("egypt", "spear");
        private static UnitData Flyer() =>
            _db.AllUnits.FirstOrDefault(u => u.IsFlying)
            ?? throw new System.InvalidOperationException("no flying unit in the export");

        /// <summary>An open hex with no building on it — never hardcode one.</summary>
        private static Hex OpenHex(BuildingMap buildings, TerrainMap terrain, int skip = 0)
        {
            return Board.AllCells()
                .Where(h => buildings.AnyAt(h) == null && terrain.RouteOk(h, false, buildings.RouteBlocker))
                .Skip(skip)
                .First();
        }

        // ---- the record ---------------------------------------------------------------

        [Test]
        public void ASpawnedUnitTakesItsStatsFromTheExport()
        {
            var field = new UnitField();
            var def = Ground();
            var u = field.Spawn(0, def, new Hex(4, 10));

            Assert.AreEqual(0, u.Side);
            Assert.AreEqual("spear", u.Key);
            Assert.AreEqual(def.hp, u.MaxHp, 1e-3f);
            Assert.AreEqual(def.hp, u.Hp, 1e-3f, "a unit starts at full health");
            Assert.AreEqual(def.size, u.Size);
            Assert.IsFalse(u.Flying, "spearmen walk");
            Assert.IsFalse(u.Dead);
        }

        [Test]
        public void PositionStartsAtTheHexCentreInLogicalSpace()
        {
            var field = new UnitField();
            var hex = new Hex(3, 9);
            var u = field.Spawn(0, Ground(), hex);

            var expected = field.Layout.Center(hex);
            Assert.AreEqual(expected.X, u.Position.X, 1e-4f);
            Assert.AreEqual(expected.Y, u.Position.Y, 1e-4f);
        }

        [Test]
        public void IdsAreUniqueAndIncrease()
        {
            var field = new UnitField();
            var ids = Enumerable.Range(0, 5)
                .Select(i => field.Spawn(0, Ground(), new Hex(i, 10)).Id)
                .ToList();

            CollectionAssert.AllItemsAreUnique(ids);
            CollectionAssert.AreEqual(ids.OrderBy(i => i).ToList(), ids);
        }

        [Test]
        public void SpawningOffTheBoardIsRejected()
        {
            var field = new UnitField();
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => field.Spawn(0, Ground(), new Hex(-1, 0)));
        }

        [Test]
        public void DamageHealAndDeathBehave()
        {
            var field = new UnitField();
            var u = field.Spawn(0, Ground(), new Hex(4, 10));

            Assert.AreEqual(10f, u.TakeDamage(10f), 1e-3f);
            Assert.AreEqual(10f, u.Heal(50f), 1e-3f, "healing cannot exceed max hp");
            Assert.AreEqual(u.MaxHp, u.Hp, 1e-3f);

            u.TakeDamage(u.MaxHp * 5f);
            Assert.AreEqual(0f, u.Hp, 1e-3f, "hp floors at zero");
            Assert.IsTrue(u.Dead);
            Assert.AreEqual(0f, u.Heal(50f), 1e-3f, "healing must not revive the dead");

            Assert.Throws<System.ArgumentOutOfRangeException>(() => u.TakeDamage(-1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => u.Heal(-1f));
        }

        // ---- capacity: the core rule --------------------------------------------------

        [Test]
        public void TwoGroundUnitsCannotShareAHex()
        {
            var field = new UnitField();
            var hex = new Hex(4, 10);

            Assert.IsTrue(field.HasCapacity(hex, flying: false), "an empty hex has room");
            field.Spawn(0, Ground(), hex);
            Assert.IsFalse(field.HasCapacity(hex, flying: false), "one ground unit fills the layer");
            Assert.AreEqual(1, field.HexLoad(hex, false));
        }

        [Test]
        public void AGroundAndAFlyingUnitCanShareAHex()
        {
            var field = new UnitField();
            var hex = new Hex(4, 10);

            field.Spawn(0, Ground(), hex);
            Assert.IsTrue(field.HasCapacity(hex, flying: true),
                "the flying layer is separate, so it is still free");

            field.Spawn(0, Flyer(), hex);
            Assert.AreEqual(1, field.HexLoad(hex, false), "one ground unit");
            Assert.AreEqual(1, field.HexLoad(hex, true), "one flying unit");
            Assert.AreEqual(2, field.AllAt(hex).Count(), "both share the hex");
        }

        [Test]
        public void TwoFlyingUnitsCannotShareAHex()
        {
            var field = new UnitField();
            var hex = new Hex(4, 10);
            field.Spawn(0, Flyer(), hex);
            Assert.IsFalse(field.HasCapacity(hex, flying: true));
        }

        /// <summary>
        /// Capacity is side-agnostic: an enemy unit blocks a hex just as a friendly one does.
        /// </summary>
        [Test]
        public void CapacityIgnoresWhichSideOccupiesTheHex()
        {
            var field = new UnitField();
            var hex = new Hex(4, 6);
            field.Spawn(1, Ground(), hex);
            Assert.IsFalse(field.HasCapacity(hex, flying: false),
                "an enemy occupant blocks the hex too");
        }

        /// <summary>
        /// A unit must not count itself, or it would find its own cell full and be unable
        /// to stay put.
        /// </summary>
        [Test]
        public void AUnitDoesNotCountItselfTowardCapacity()
        {
            var field = new UnitField();
            var hex = new Hex(4, 10);
            var u = field.Spawn(0, Ground(), hex);

            Assert.IsFalse(field.HasCapacity(hex, null, false), "occupied to anyone else");
            Assert.IsTrue(field.HasCapacity(hex, u, false), "but not to itself");
            Assert.AreEqual(0, field.HexLoad(hex, u, false));
        }

        [Test]
        public void DeadUnitsDoNotBlockTheHexTheyFellOn()
        {
            var field = new UnitField();
            var hex = new Hex(4, 10);
            var u = field.Spawn(0, Ground(), hex);

            Assert.IsFalse(field.HasCapacity(hex, false));
            u.Kill();
            Assert.IsTrue(field.HasCapacity(hex, false), "a corpse must not hold the hex");
            Assert.AreEqual(0, field.HexLoad(hex, false));
            Assert.IsNull(field.At(hex, false));
        }

        [Test]
        public void RemoveDeadDropsOnlyTheDead()
        {
            var field = new UnitField();
            var a = field.Spawn(0, Ground(), new Hex(4, 10));
            field.Spawn(0, Ground(), new Hex(3, 10));
            a.Kill();

            Assert.AreEqual(1, field.RemoveDead());
            Assert.AreEqual(1, field.All.Count);
            Assert.AreEqual(1, field.AliveCount);
        }

        [Test]
        public void MovingAUnitFreesItsOldHex()
        {
            var field = new UnitField();
            var from = new Hex(4, 10);
            var to = new Hex(3, 10);
            var u = field.Spawn(0, Ground(), from);

            u.PlaceAt(to, field.Layout);

            Assert.IsTrue(field.HasCapacity(from, false), "the vacated hex is free again");
            Assert.IsFalse(field.HasCapacity(to, false));
            Assert.AreEqual(field.Layout.Center(to).X, u.Position.X, 1e-4f,
                "PlaceAt snaps the position to the new centre");
        }

        /// <summary>
        /// <c>EnterHex</c> advances the cell WITHOUT snapping the position — the browser
        /// game relies on that gap, which is why radius effects read position, not hex.
        /// </summary>
        [Test]
        public void EnterHexAdvancesTheCellButLeavesThePositionBehind()
        {
            var field = new UnitField();
            var u = field.Spawn(0, Ground(), new Hex(4, 10));
            var before = u.Position;

            u.EnterHex(new Hex(3, 10));

            Assert.AreEqual(new Hex(3, 10), u.Hex);
            Assert.AreEqual(before.X, u.Position.X, 1e-4f, "position must not jump");
            Assert.AreEqual(before.Y, u.Position.Y, 1e-4f);
        }

        [Test]
        public void EnemiesWithinFindsTheNearestFirst()
        {
            var field = new UnitField();
            var origin = new Hex(4, 6);

            field.Spawn(1, Ground(), new Hex(4, 5));   // 1 away
            field.Spawn(1, Ground(), new Hex(4, 3));   // further
            field.Spawn(0, Ground(), new Hex(4, 7));   // friendly, must not appear

            var found = field.EnemiesWithin(origin, 0, 5).ToList();

            Assert.AreEqual(2, found.Count, "only the two enemies");
            Assert.AreEqual(1, Hex.Distance(found[0].Hex, origin), "nearest first");
            Assert.IsEmpty(field.EnemiesWithin(origin, 0, 0).ToList(), "range 0 finds nothing here");
        }

        // ---- emergency placement -----------------------------------------------------

        [Test]
        public void AClearExitHexIsUsedAsIs()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var exit = OpenHex(buildings, terrain);

            var at = SpawnPlacement.Resolve(exit, 0, buildings.Of(0, BuildingType.City),
                                            false, field, buildings);

            Assert.AreEqual(exit, at, "an empty doorway needs no fallback");
        }

        /// <summary>
        /// The rule the step exists for: a blocked doorway must not stall training. The
        /// unit emerges ON its source building instead.
        /// </summary>
        [Test]
        public void ABlockedExitPlacesTheUnitOnItsSourceBuilding()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var city = buildings.Of(0, BuildingType.City);
            var exit = OpenHex(buildings, terrain);

            field.Spawn(0, Ground(), exit);   // doorway now full

            var at = SpawnPlacement.Resolve(exit, 0, city, false, field, buildings);

            Assert.IsNotNull(at, "placement must not fail while a building stands");
            CollectionAssert.Contains(city.Hexes().ToList(), at.Value,
                "the unit should emerge on one of its source building's hexes");
        }

        [Test]
        public void TheChosenBuildingHexIsTheOneNearestTheIntendedExit()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var city = buildings.Of(0, BuildingType.City);

            // Block a hex adjacent to one end of the city, then check we emerge at that end.
            foreach (var end in new[] { city.HexA, city.HexB })
            {
                var near = end.Neighbors().First(h => buildings.AnyAt(h) == null);
                var f = new UnitField();
                f.Spawn(0, Ground(), near);

                var at = SpawnPlacement.Resolve(near, 0, city, false, f, buildings);
                Assert.AreEqual(end, at.Value,
                    $"should emerge on the city hex closest to {near}");
            }
        }

        [Test]
        public void ADestroyedSourceFallsBackToAnotherStandingBuilding()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var city = buildings.Of(0, BuildingType.City);
            var exit = OpenHex(buildings, terrain);

            field.Spawn(0, Ground(), exit);
            city.TakeDamage(city.MaxHp);

            var at = SpawnPlacement.Resolve(exit, 0, city, false, field, buildings);

            Assert.IsNotNull(at, "a dead source must not stall training");
            var host = buildings.AnyAt(at.Value);
            Assert.IsNotNull(host, "the fallback hex belongs to a building");
            Assert.IsFalse(host.Dead, "and that building is standing");
            Assert.AreEqual(0, host.Side, "and it belongs to the unit's owner");
        }

        [Test]
        public void WithNoStandingBuildingPlacementFailsSoTheCallerCanRetry()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var exit = OpenHex(buildings, terrain);

            field.Spawn(0, Ground(), exit);
            foreach (var b in buildings.Of(0).ToList()) b.TakeDamage(b.MaxHp);

            var at = SpawnPlacement.Resolve(exit, 0, null, false, field, buildings);

            Assert.IsNull(at, "with nothing standing, the caller retries rather than dropping the unit");
            Assert.IsFalse(SpawnPlacement.CanPlace(exit, 0, false, field, buildings));
        }

        /// <summary>
        /// Emergency placement deliberately ignores capacity on the building hex — stacking
        /// there is the escape valve, and refusing it would reintroduce the stall.
        /// </summary>
        [Test]
        public void EmergencyPlacementMayStackOnABuildingHex()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var city = buildings.Of(0, BuildingType.City);
            var exit = OpenHex(buildings, terrain);

            field.Spawn(0, Ground(), exit);
            var first = SpawnPlacement.Resolve(exit, 0, city, false, field, buildings);
            field.Spawn(0, Ground(), first.Value);

            var second = SpawnPlacement.Resolve(exit, 0, city, false, field, buildings);

            Assert.IsNotNull(second, "still must not stall");
            Assert.AreEqual(first.Value, second.Value,
                "the same building hex is offered again even though it is occupied");
        }

        /// <summary>Layers are independent here too: a ground jam does not block a flyer.</summary>
        [Test]
        public void AGroundJamDoesNotDivertAFlyingSpawn()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var exit = OpenHex(buildings, terrain);

            field.Spawn(0, Ground(), exit);

            var at = SpawnPlacement.Resolve(exit, 0, buildings.Of(0, BuildingType.City),
                                            true, field, buildings);

            Assert.AreEqual(exit, at.Value, "the flying layer at that hex is still empty");
        }

        [Test]
        public void CanPlaceIsTrueWhileAnyBuildingStands()
        {
            var terrain = new TerrainMap();
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            var exit = OpenHex(buildings, terrain);
            field.Spawn(0, Ground(), exit);

            Assert.IsTrue(SpawnPlacement.CanPlace(exit, 0, false, field, buildings));

            var standing = buildings.Of(0).Where(b => !b.Dead).ToList();
            for (int i = 0; i < standing.Count - 1; i++) standing[i].TakeDamage(standing[i].MaxHp);
            Assert.IsTrue(SpawnPlacement.CanPlace(exit, 0, false, field, buildings),
                "one standing building is enough");

            standing[standing.Count - 1].TakeDamage(standing[standing.Count - 1].MaxHp);
            Assert.IsFalse(SpawnPlacement.CanPlace(exit, 0, false, field, buildings));
        }

        [Test]
        public void OffBoardPlacementIsRejected()
        {
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var field = new UnitField();
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => SpawnPlacement.Resolve(new Hex(-1, 0), 0, null, false, field, buildings));
        }
    }
}
