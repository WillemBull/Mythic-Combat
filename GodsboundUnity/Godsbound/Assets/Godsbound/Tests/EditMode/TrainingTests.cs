using System.Linq;
using NUnit.Framework;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Training;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>Paying for units, the training queue, and the spawn.</summary>
    public class TrainingTests
    {
        private static GameDatabase _db;
        private static TrainingRates R => _db.Training;

        [SetUp]
        public void Load()
        {
            if (_db != null) return;
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        private static UnitData Human() => _db.Unit("egypt", "spear");
        private static UnitData FavorUnit() =>
            _db.AllUnits.First(u => u.costFavor > 0 && u.costFood > 0);

        private static Hex OpenHex(BuildingMap b, TerrainMap t, int skip = 0) =>
            Board.AllCells()
                 .Where(h => b.AnyAt(h) == null && t.RouteOk(h, false, b.RouteBlocker))
                 .Skip(skip).First();

        private static Purse Rich() => new Purse(1000f, 1000f);

        // ---- price ---------------------------------------------------------------------

        [Test]
        public void RatesWereExtractedFromTheBrowserGame()
        {
            Assert.Less(R.thothDiscount, 1f, "Thoth's ledger should reduce cost");
            Assert.Less(R.nuWaHumanFoodDiscount, 1f, "Nu Wa should reduce human food cost");
            Assert.Less(R.messengerTrainMultiplier, 1f, "Hermes should shorten training");
            Assert.Greater(R.blockedExitRetrySeconds, 0f);
        }

        [Test]
        public void UndiscountedPriceIsTheExportedCost()
        {
            var def = Human();
            var p = UnitPrice.For(def, TrainingModifiers.None, R);
            Assert.AreEqual(def.costFood, p.Food, 1e-3f);
            Assert.AreEqual(def.costFavor, p.Favor, 1e-3f);
        }

        [Test]
        public void ThothDiscountsBothCurrencies()
        {
            var def = FavorUnit();
            var p = UnitPrice.For(def, new TrainingModifiers(scribesLedger: true), R);
            Assert.AreEqual(def.costFood * R.thothDiscount, p.Food, 1e-3f);
            Assert.AreEqual(def.costFavor * R.thothDiscount, p.Favor, 1e-3f,
                "Thoth's ledger applies to favor too");
        }

        /// <summary>Nu Wa is narrower than Thoth: food only, humans only.</summary>
        [Test]
        public void NuWaDiscountsHumanFoodOnly()
        {
            var human = Human();
            var p = UnitPrice.For(human, new TrainingModifiers(motherOfHumanity: true), R);
            Assert.AreEqual(human.costFood * R.nuWaHumanFoodDiscount, p.Food, 1e-3f);

            var nonHuman = _db.AllUnits.First(u => u.cat != "human" && u.costFood > 0);
            var q = UnitPrice.For(nonHuman, new TrainingModifiers(motherOfHumanity: true), R);
            Assert.AreEqual(nonHuman.costFood, q.Food, 1e-3f, "non-humans pay full food");
        }

        [Test]
        public void NuWaDoesNotTouchFavor()
        {
            // No shipped human currently costs favor. Exercise the human-only branch
            // with an exported two-currency price so a future such unit stays correct.
            var priced = FavorUnit();
            var def = new UnitData { cat = "human", costFood = priced.costFood,
                                     costFavor = priced.costFavor };
            var p = UnitPrice.For(def, new TrainingModifiers(motherOfHumanity: true), R);
            Assert.AreEqual(def.costFavor, p.Favor, 1e-3f);
        }

        [Test]
        public void TheTwoDiscountsStack()
        {
            var def = Human();
            var both = UnitPrice.For(def,
                new TrainingModifiers(scribesLedger: true, motherOfHumanity: true), R);
            Assert.AreEqual(def.costFood * R.thothDiscount * R.nuWaHumanFoodDiscount,
                both.Food, 1e-3f);
        }

        /// <summary>
        /// The browser game records that <c>canAfford</c> and <c>pay</c> once disagreed —
        /// one ignored a discount the other applied, so a side was blocked at full price and
        /// then charged less. Both must read the same price.
        /// </summary>
        [Test]
        public void CanAffordAndPayAgreeOnPrice()
        {
            var def = Human();
            var mods = new TrainingModifiers(scribesLedger: true, motherOfHumanity: true);
            var price = UnitPrice.For(def, mods, R);

            var exact = new Purse(price.Food, price.Favor);
            Assert.IsTrue(UnitPrice.CanAfford(exact, def, mods, R),
                "the exact price must be affordable");

            var after = UnitPrice.Pay(exact, def, mods, R);
            Assert.AreEqual(0f, after.Food, 1e-3f, "paying the exact price empties the purse");
            Assert.AreEqual(0f, after.Favor, 1e-3f);

            var short_ = new Purse(price.Food - 0.01f, price.Favor);
            Assert.IsFalse(UnitPrice.CanAfford(short_, def, mods, R),
                "a penny short must be refused");
        }

        [Test]
        public void PayingWhatYouCannotAffordThrowsRatherThanGoingNegative()
        {
            var def = Human();
            Assert.Throws<System.InvalidOperationException>(
                () => UnitPrice.Pay(Purse.Empty, def, TrainingModifiers.None, R));
        }

        [Test]
        public void BothCurrenciesMustSuffice()
        {
            var def = FavorUnit();
            var price = UnitPrice.For(def, TrainingModifiers.None, R);

            Assert.IsFalse(UnitPrice.CanAfford(new Purse(price.Food, 0f), def, TrainingModifiers.None, R),
                "enough food but no favor is not enough");
            Assert.IsFalse(UnitPrice.CanAfford(new Purse(0f, price.Favor), def, TrainingModifiers.None, R),
                "enough favor but no food is not enough");
        }

        [Test]
        public void MessengerShortensTraining()
        {
            var def = Human();
            Assert.AreEqual(def.train, UnitPrice.TrainSeconds(def, TrainingModifiers.None, R), 1e-3f);
            Assert.AreEqual(def.train * R.messengerTrainMultiplier,
                UnitPrice.TrainSeconds(def, new TrainingModifiers(messenger: true), R), 1e-3f);
        }

        // ---- queue ---------------------------------------------------------------------

        [Test]
        public void QueueingChargesThePurseAndSchedulesTheUnit()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var def = Human();
            var price = UnitPrice.For(def, TrainingModifiers.None, R);

            bool ok = q.TryEnqueue(Rich(), 0, def, OpenHex(buildings, terrain),
                                   buildings.Of(0, BuildingType.City), TrainingModifiers.None,
                                   elapsed: 0f, out var left, out var order);

            Assert.IsTrue(ok);
            Assert.AreEqual(1000f - price.Food, left.Food, 1e-3f, "the purse is charged");
            Assert.AreEqual(def.train, order.ReadyAt, 1e-3f, "due after its training time");
            Assert.AreEqual(1, q.PendingCount);
        }

        [Test]
        public void AUnitCannotBeQueuedWithoutTheResources()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();

            bool ok = q.TryEnqueue(Purse.Empty, 0, Human(), OpenHex(buildings, terrain),
                                   null, TrainingModifiers.None, 0f, out var left, out var order);

            Assert.IsFalse(ok);
            Assert.IsNull(order);
            Assert.AreEqual(0f, left.Food, 1e-3f, "a refused order must not charge anything");
            Assert.AreEqual(0, q.PendingCount);
        }

        [Test]
        public void NothingSpawnsBeforeItsReadyTime()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var def = Human();

            q.TryEnqueue(Rich(), 0, def, OpenHex(buildings, terrain),
                         buildings.Of(0, BuildingType.City), TrainingModifiers.None, 0f,
                         out _, out var order);

            Assert.IsEmpty(q.Tick(order.ReadyAt - 0.01f, units, buildings), "not yet due");
            Assert.AreEqual(0, units.AliveCount);

            var spawned = q.Tick(order.ReadyAt, units, buildings);
            Assert.AreEqual(1, spawned.Count, "due now");
            Assert.AreEqual(0, q.PendingCount, "and removed from the queue");
        }

        [Test]
        public void ACompletedUnitAppearsAtItsExitHexWithFullHealth()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var def = Human();
            var exit = OpenHex(buildings, terrain);

            q.TryEnqueue(Rich(), 0, def, exit, buildings.Of(0, BuildingType.City),
                         TrainingModifiers.None, 0f, out _, out var order);

            var u = q.Tick(order.ReadyAt, units, buildings).Single();

            Assert.AreEqual(exit, u.Hex);
            Assert.AreEqual(0, u.Side);
            Assert.AreEqual(def.hp, u.Hp, 1e-3f);
            Assert.AreEqual(order.ReadyAt, u.BornAt, 1e-3f);
            Assert.IsTrue(u.Free, "a trained unit stands free until given a route");
        }

        [Test]
        public void OrdersDueTogetherMatchBrowserCreationOrder()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var city = buildings.Of(0, BuildingType.City);
            var purse = Rich();

            for (int i = 0; i < 3; i++)
                q.TryEnqueue(purse, 0, Human(), new Hex(i, Board.AiRows), city,
                             TrainingModifiers.None, 0f, out purse, out _);

            var spawned = q.Tick(100f, units, buildings);

            Assert.AreEqual(3, spawned.Count);
            CollectionAssert.AreEqual(R.simultaneousCompletionOrder,
                spawned.Select(s => s.Hex.C).ToArray(), "measured by the browser's tickTraining");
            var births = spawned.Select(s => s.Id).ToList();
            CollectionAssert.AreEqual(births.OrderBy(x => x).ToList(), births,
                "returned units must be in the same order they entered the field");
        }

        // ---- the no-stall rule ---------------------------------------------------------

        /// <summary>
        /// The rule this step exists to protect. A blocked doorway must divert the unit onto
        /// a standing building, not hold up the queue.
        /// </summary>
        [Test]
        public void ABlockedExitDivertsOntoABuildingWithoutStalling()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var city = buildings.Of(0, BuildingType.City);
            var exit = OpenHex(buildings, terrain);

            units.Spawn(0, Human(), exit);   // doorway jammed

            q.TryEnqueue(Rich(), 0, Human(), exit, city, TrainingModifiers.None, 0f,
                         out _, out var order);

            var spawned = q.Tick(order.ReadyAt, units, buildings);

            Assert.AreEqual(1, spawned.Count, "the unit must still appear");
            CollectionAssert.Contains(city.Hexes().ToList(), spawned[0].Hex,
                "it emerges on its source building");
            Assert.AreEqual(0, q.PendingCount, "and the queue is not held up");
            Assert.AreEqual(0, order.Deferrals, "no deferral was needed");
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void FullFriendlyBuildingsStillSpawnImmediately(int side, bool destroySource)
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var units = new UnitField();
            var city = buildings.Of(side, BuildingType.City);
            var exit = OpenHex(buildings, new TerrainMap());
            units.Spawn(side, Human(), exit);
            foreach (var building in buildings.Of(side))
                foreach (var hex in building.Hexes()) units.Spawn(side, Human(), hex);

            q.TryEnqueue(Rich(), side, Human(), exit, city, TrainingModifiers.None, 12f,
                         out var paidPurse, out var order);
            if (destroySource) city.TakeDamage(city.MaxHp);

            float due = order.ReadyAt;
            var spawned = q.Tick(due, units, buildings).Single();
            var host = buildings.StandingAt(spawned.Hex);
            Assert.IsNotNull(host, "the fallback must be a standing building");
            Assert.AreEqual(side, host.Side, "the fallback must belong to the unit");
            if (!destroySource) Assert.AreSame(city, host, "prefer the source while it stands");
            Assert.AreEqual(2, units.HexLoad(spawned.Hex, false),
                "emergency placement intentionally ignores the building hex's capacity");
            Assert.AreEqual(due, spawned.BornAt, "no extra wait at a blocked exit");
            Assert.AreEqual(due, order.ReadyAt);
            Assert.AreEqual(0, order.Deferrals);
            Assert.AreEqual(0, q.PendingCount);
            Assert.IsEmpty(q.Tick(due + 1f, units, buildings), "a paid order spawns only once");
            Assert.AreEqual(Rich().Food - Human().costFood, paidPurse.Food, 1e-3f);
        }

        /// <summary>
        /// Only with nothing standing does the order wait — and it waits, it is never lost.
        /// </summary>
        [Test]
        public void WithNoBuildingsTheOrderIsDeferredNotDropped()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var exit = OpenHex(buildings, terrain);

            units.Spawn(0, Human(), exit);
            q.TryEnqueue(Rich(), 0, Human(), exit, null, TrainingModifiers.None, 0f,
                         out _, out var order);
            foreach (var b in buildings.Of(0).ToList()) b.TakeDamage(b.MaxHp);

            float due = order.ReadyAt;
            Assert.IsEmpty(q.Tick(due, units, buildings), "nowhere to put it");
            Assert.AreEqual(1, q.PendingCount, "still queued, still paid for");
            Assert.AreEqual(1, order.Deferrals);
            Assert.AreEqual(due + R.blockedExitRetrySeconds, order.ReadyAt, 1e-3f,
                "the retry is pushed out by the exported delay");

            // It keeps waiting rather than vanishing...
            q.Tick(order.ReadyAt, units, buildings);
            Assert.AreEqual(1, q.PendingCount);

            // ...and lands as soon as the doorway clears.
            units.All.Single(u => u.Hex == exit).Kill();
            var spawned = q.Tick(order.ReadyAt, units, buildings);
            Assert.AreEqual(1, spawned.Count, "it completes once there is room");
            Assert.AreEqual(exit, spawned[0].Hex);
        }

        [Test]
        public void ADestroyedSourceStillCompletesViaAnotherBuilding()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var city = buildings.Of(0, BuildingType.City);
            var exit = OpenHex(buildings, terrain);

            units.Spawn(0, Human(), exit);
            q.TryEnqueue(Rich(), 0, Human(), exit, city, TrainingModifiers.None, 0f,
                         out _, out var order);
            city.TakeDamage(city.MaxHp);

            var spawned = q.Tick(order.ReadyAt, units, buildings);

            Assert.AreEqual(1, spawned.Count, "losing the barracks mid-train must not lose the unit");
            var host = buildings.AnyAt(spawned[0].Hex);
            Assert.IsNotNull(host);
            Assert.IsFalse(host.Dead);
        }

        /// <summary>
        /// A long jam must not accumulate unbounded deferrals or lose the order — the
        /// property that makes "never stalls" mean something over time.
        /// </summary>
        [Test]
        public void ASustainedJamKeepsTheOrderAliveAndEventuallyDelivers()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var exit = OpenHex(buildings, terrain);

            units.Spawn(0, Human(), exit);
            q.TryEnqueue(Rich(), 0, Human(), exit, null, TrainingModifiers.None, 0f,
                         out _, out var order);
            foreach (var b in buildings.Of(0).ToList()) b.TakeDamage(b.MaxHp);

            // Capture the bound FIRST. order.ReadyAt is pushed out by every deferral, so
            // reading it in the loop condition makes the finish line retreat at exactly the
            // rate t advances (0.4s per deferral, one deferral per 4 ticks = 0.1s/tick) and
            // the loop never ends. That hung the editor once; do not inline it again.
            float jamStart = order.ReadyAt;
            for (float t = jamStart; t < jamStart + 20f; t += 0.1f)
                q.Tick(t, units, buildings);

            Assert.AreEqual(1, q.PendingCount, "still there after twenty seconds of jam");
            Assert.AreEqual(0, units.AliveCount - 1, "and nothing spurious spawned");

            buildings.ResetAll();
            var spawned = q.Tick(order.ReadyAt + 30f, units, buildings);
            Assert.AreEqual(1, spawned.Count, "rebuilding lets it through");
        }

        [Test]
        public void FlyingUnitsUseTheirOwnLayerWhenChoosingToDivert()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();
            var units = new UnitField();
            var flyer = _db.AllUnits.First(u => u.IsFlying);
            var exit = OpenHex(buildings, terrain);

            units.Spawn(0, Human(), exit);   // ground jam only

            q.TryEnqueue(Rich(), 0, flyer, exit, buildings.Of(0, BuildingType.City),
                         TrainingModifiers.None, 0f, out _, out var order);
            var spawned = q.Tick(order.ReadyAt, units, buildings);

            Assert.AreEqual(exit, spawned.Single().Hex,
                "the flying layer at the doorway is clear, so no diversion");
        }

        [Test]
        public void ClearDropsPendingOrders()
        {
            var q = new TrainingQueue(R);
            var buildings = BuildingMap.FromExport(_db.Buildings);
            var terrain = new TerrainMap();

            q.TryEnqueue(Rich(), 0, Human(), OpenHex(buildings, terrain), null,
                         TrainingModifiers.None, 0f, out _, out _);
            q.Clear();
            Assert.AreEqual(0, q.PendingCount);
        }

        [Test]
        public void OffBoardExitHexesAreRejected()
        {
            var q = new TrainingQueue(R);
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => q.TryEnqueue(Rich(), 0, Human(), new Hex(-1, 0), null,
                                   TrainingModifiers.None, 0f, out _, out _));
        }
    }
}
