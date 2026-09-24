using NUnit.Framework;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// The resource tick, against rates measured off the browser game's <c>tickEconomy</c>.
    /// </summary>
    /// <remarks>
    /// The expected figures are not typed into these tests — they come from the exported
    /// <see cref="EconomyRates"/>, which the exporter produced by driving the real
    /// <c>tickEconomy</c> with dt = 1. So these assert that the C# formula reassembles the
    /// browser game's own measurements, which a hand-written expectation could not.
    /// </remarks>
    public class EconomyTests
    {
        private static GameDatabase _db;
        private static EconomyRates R => _db.Economy;

        [SetUp]
        public void Load()
        {
            if (_db != null) return;
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        private static EconomyConditions Base(bool city = true, bool temple = true) =>
            new EconomyConditions(cityStanding: city, templeStanding: temple);

        [Test]
        public void RatesWereExported()
        {
            Assert.AreEqual(120, R.foodCap, "S.FOOD_CAP");
            Assert.AreEqual(250, R.favorCap, "S.FAVOR_CAP");
            Assert.Greater(R.foodRateMult, 0f, "FOOD_RATE_MULT should have been measured");
            Assert.IsTrue(R.capsClamp, "the exporter should have verified the caps clamp");
        }

        // ---- food ------------------------------------------------------------------

        [Test]
        public void FoodWithACityMatchesTheMeasuredRate()
        {
            Assert.AreEqual(R.foodWithCity, EconomyTick.FoodPerSecond(Base(), R), 1e-4f);
        }

        /// <summary>The no-city fallback the step calls out explicitly.</summary>
        [Test]
        public void FoodFallsBackWhenTheCityIsDestroyed()
        {
            float withCity = EconomyTick.FoodPerSecond(Base(city: true), R);
            float without = EconomyTick.FoodPerSecond(Base(city: false), R);

            Assert.AreEqual(R.foodWithoutCity, without, 1e-4f);
            Assert.Less(without, withCity, "losing the City must reduce food income");
            Assert.Greater(without, 0f, "the fallback is reduced, not zero");
        }

        [Test]
        public void CityFoodBoostRaisesFoodByTheMeasuredMultiplier()
        {
            var plain = EconomyTick.FoodPerSecond(Base(), R);
            var boosted = EconomyTick.FoodPerSecond(
                new EconomyConditions(cityFoodBoost: true), R);

            Assert.AreEqual(R.foodRateMult * R.cityFoodBaseBoosted, boosted, 1e-4f);
            Assert.Greater(boosted, plain);
        }

        [Test]
        public void MorningStarRaisesFoodByTheMeasuredMultiplier()
        {
            var plain = EconomyTick.FoodPerSecond(Base(), R);
            var accelerated = EconomyTick.FoodPerSecond(
                new EconomyConditions(morningStar: true), R);

            Assert.AreEqual(plain * R.morningStarFoodMultiplier, accelerated, 1e-4f);
        }

        /// <summary>Bastet's boost applies to the City's rate, so it needs a City.</summary>
        [Test]
        public void CityFoodBoostDoesNothingWithoutACity()
        {
            var a = EconomyTick.FoodPerSecond(new EconomyConditions(cityStanding: false), R);
            var b = EconomyTick.FoodPerSecond(
                new EconomyConditions(cityStanding: false, cityFoodBoost: true), R);
            Assert.AreEqual(a, b, 1e-4f);
        }

        // ---- favor -----------------------------------------------------------------

        [Test]
        public void PlayerFavorMatchesTheMeasuredRates()
        {
            Assert.AreEqual(R.playerFavorWithTemple,
                EconomyTick.FavorPerSecond(EconomySide.Player, Base(temple: true), R), 1e-4f);
            Assert.AreEqual(R.playerFavorWithoutTemple,
                EconomyTick.FavorPerSecond(EconomySide.Player, Base(temple: false), R), 1e-4f);
        }

        [Test]
        public void AiFavorMatchesTheMeasuredRates()
        {
            Assert.AreEqual(R.aiFavorWithTemple,
                EconomyTick.FavorPerSecond(EconomySide.Ai, Base(temple: true), R), 1e-4f);
            Assert.AreEqual(R.aiFavorWithoutTemple,
                EconomyTick.FavorPerSecond(EconomySide.Ai, Base(temple: false), R), 1e-4f);
        }

        /// <summary>
        /// Rewritten 2026-09-16. This test used to pin the player earning favor faster than
        /// the AI (3.4/2.2 against 3.0/2.0), flagged to Willem as possible drift. He ruled
        /// it drift: both sides now use one formula in the browser, and the re-export
        /// measures them equal with and without a Temple.
        /// </summary>
        [Test]
        public void FavorIsSymmetricBetweenPlayerAndAi()
        {
            Assert.IsFalse(R.favorIsSideAsymmetric,
                "the export should measure the same favor rates for both sides");
            foreach (bool temple in new[] { true, false })
                Assert.AreEqual(
                    EconomyTick.FavorPerSecond(EconomySide.Player, Base(temple: temple), R),
                    EconomyTick.FavorPerSecond(EconomySide.Ai, Base(temple: temple), R), 1e-4f,
                    $"temple {temple}: both sides earn the same favor");
        }

        [Test]
        public void FavorFallsToTheFloorWithoutATemple()
        {
            foreach (var side in new[] { EconomySide.Player, EconomySide.Ai })
            {
                float with = EconomyTick.FavorPerSecond(side, Base(temple: true), R);
                float without = EconomyTick.FavorPerSecond(side, Base(temple: false), R);
                Assert.Less(without, with, $"{side}: losing the Temple must reduce favor");
                Assert.Greater(without, 0f, $"{side}: the floor is reduced, not zero");
            }
        }

        /// <summary>Aztec's Temple never lifts favor — <c>templeBonus:false</c>.</summary>
        [Test]
        public void AFactionWithoutTempleBonusStaysOnTheFloor()
        {
            var standing = new EconomyConditions(templeStanding: true, templeBonus: false);
            var destroyed = new EconomyConditions(templeStanding: false, templeBonus: false);

            Assert.AreEqual(
                EconomyTick.FavorPerSecond(EconomySide.Player, destroyed, R),
                EconomyTick.FavorPerSecond(EconomySide.Player, standing, R), 1e-4f,
                "with templeBonus off, a standing Temple should change nothing");
        }

        /// <summary>The Aztec faction really does ship with the bonus disabled.</summary>
        [Test]
        public void AztecIsTheFactionWithoutTempleBonus()
        {
            var aztec = _db.Faction("aztec");
            Assert.IsNotNull(aztec);
            var flag = aztec.economy.Find(e => e.key == "templeBonus");
            Assert.IsNotNull(flag, "aztec should define templeBonus");
            Assert.AreEqual("false", flag.value);
        }

        [Test]
        public void BountyAddsCityFavorOnlyWithAStandingCity()
        {
            var withCity = new EconomyConditions(bounty: true, cityStanding: true);
            var without = new EconomyConditions(bounty: true, cityStanding: false);

            Assert.AreEqual(
                EconomyTick.FavorPerSecond(EconomySide.Player, Base(), R) + R.bountyCityFavorPerSecond,
                EconomyTick.FavorPerSecond(EconomySide.Player, withCity, R), 1e-4f);

            Assert.AreEqual(
                EconomyTick.FavorPerSecond(EconomySide.Player, Base(city: false), R),
                EconomyTick.FavorPerSecond(EconomySide.Player, without, R), 1e-4f,
                "no City, no Bounty favor");
        }

        /// <summary>
        /// Rewritten 2026-09-16. Bounty used to appear only on the player's favor line, so
        /// this test pinned the AI getting nothing from it. Willem unified the sides, so the
        /// AI now receives exactly the player's Bounty, measured on both sides by the exporter.
        /// </summary>
        [Test]
        public void BountyAppliesToTheAiSide()
        {
            Assert.IsTrue(R.bountyAppliesToAi, "the export should measure Bounty paying the AI");
            Assert.AreEqual(
                EconomyTick.FavorPerSecond(EconomySide.Ai, Base(), R) + R.bountyCityFavorPerSecond,
                EconomyTick.FavorPerSecond(EconomySide.Ai, new EconomyConditions(bounty: true), R),
                1e-4f);
        }

        [Test]
        public void WaterTileFavorIsAddedFlatAndUnscaled()
        {
            var withWater = new EconomyConditions(waterFavorPerSecond: 0.25f);
            Assert.AreEqual(
                EconomyTick.FavorPerSecond(EconomySide.Player, Base(), R) + 0.25f,
                EconomyTick.FavorPerSecond(EconomySide.Player, withWater, R), 1e-4f);
        }

        // ---- mandate ---------------------------------------------------------------

        [Test]
        public void MandateScalesWithBuildingsStanding()
        {
            var three = new EconomyConditions(mandateOfHeaven: true, standingBuildings: 3);
            var two = new EconomyConditions(mandateOfHeaven: true, standingBuildings: 2);
            var one = new EconomyConditions(mandateOfHeaven: true, standingBuildings: 1);

            Assert.AreEqual(R.mandateThreeBuildingsMultiplier, EconomyTick.MandateMultiplier(three, R), 1e-4f);
            Assert.AreEqual(R.mandateTwoBuildingsMultiplier, EconomyTick.MandateMultiplier(two, R), 1e-4f);
            Assert.AreEqual(1f, EconomyTick.MandateMultiplier(one, R), 1e-4f, "below two, no bonus");
            Assert.AreEqual(1f, EconomyTick.MandateMultiplier(Base(), R), 1e-4f, "no passive, no bonus");

            Assert.Greater(R.mandateThreeBuildingsMultiplier, R.mandateTwoBuildingsMultiplier);
        }

        [Test]
        public void MandateScalesBothFoodAndFavor()
        {
            var m = new EconomyConditions(mandateOfHeaven: true, standingBuildings: 3);
            Assert.AreEqual(EconomyTick.FoodPerSecond(Base(), R) * R.mandateThreeBuildingsMultiplier,
                EconomyTick.FoodPerSecond(m, R), 1e-4f);
            Assert.AreEqual(EconomyTick.FavorPerSecond(EconomySide.Player, Base(), R) * R.mandateThreeBuildingsMultiplier,
                EconomyTick.FavorPerSecond(EconomySide.Player, m, R), 1e-4f);
        }

        // ---- blight ----------------------------------------------------------------

        /// <summary>
        /// Blight halts food but NOT favor. Measured from the browser source, where the
        /// else-guard wraps only the food line — worth pinning, because "nothing grows"
        /// reads as though it should stop both.
        /// </summary>
        [Test]
        public void BlightStopsFoodButNotFavor()
        {
            var blighted = new EconomyConditions(blighted: true);

            Assert.AreEqual(0f, EconomyTick.FoodPerSecond(blighted, R), 1e-4f, "food stops");
            Assert.AreEqual(
                EconomyTick.FavorPerSecond(EconomySide.Player, Base(), R),
                EconomyTick.FavorPerSecond(EconomySide.Player, blighted, R), 1e-4f,
                "favor keeps flowing under blight");

            Assert.IsTrue(R.blightStopsFood);
            Assert.IsFalse(R.blightStopsFavor);
        }

        // ---- accumulation and caps -------------------------------------------------

        [Test]
        public void AdvanceAccumulatesAtTheExpectedRate()
        {
            var p = EconomyTick.Advance(Purse.Empty, EconomySide.Player, Base(), R, 2f);
            Assert.AreEqual(2f * R.foodWithCity, p.Food, 1e-3f);
            Assert.AreEqual(2f * R.playerFavorWithTemple, p.Favor, 1e-3f);
        }

        [Test]
        public void AdvanceIsTheSameSteppedOrInOneGo()
        {
            var once = EconomyTick.Advance(Purse.Empty, EconomySide.Player, Base(), R, 1f);

            var stepped = Purse.Empty;
            for (int i = 0; i < 10; i++)
                stepped = EconomyTick.Advance(stepped, EconomySide.Player, Base(), R, 0.1f);

            Assert.AreEqual(once.Food, stepped.Food, 1e-3f, "food should not depend on step size");
            Assert.AreEqual(once.Favor, stepped.Favor, 1e-3f, "favor should not depend on step size");
        }

        [Test]
        public void ResourcesClampToTheirCaps()
        {
            var p = EconomyTick.Advance(Purse.Empty, EconomySide.Player, Base(), R, 100000f);
            Assert.AreEqual(R.foodCap, p.Food, 1e-3f);
            Assert.AreEqual(R.favorCap, p.Favor, 1e-3f);
        }

        /// <summary>Each resource clamps on its own; a full larder must not stall favor.</summary>
        [Test]
        public void HittingTheFoodCapDoesNotStallFavor()
        {
            var atFoodCap = new Purse(R.foodCap, 0f);
            var next = EconomyTick.Advance(atFoodCap, EconomySide.Player, Base(), R, 1f);
            Assert.AreEqual(R.foodCap, next.Food, 1e-3f, "food stays at the cap");
            Assert.Greater(next.Favor, 0f, "favor should still accrue");
        }

        [Test]
        public void ZeroDeltaChangesNothingAndNegativeIsRejected()
        {
            var p = new Purse(10f, 20f);
            var same = EconomyTick.Advance(p, EconomySide.Player, Base(), R, 0f);
            Assert.AreEqual(10f, same.Food, 1e-4f);
            Assert.AreEqual(20f, same.Favor, 1e-4f);

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => EconomyTick.Advance(p, EconomySide.Player, Base(), R, -1f));
        }

        /// <summary>
        /// A whole match at the standing-everything rate should bank well past both caps,
        /// which is what makes the caps the real constraint rather than income.
        /// </summary>
        [Test]
        public void AFullMatchReachesBothCaps()
        {
            var p = Purse.Empty;
            for (int s = 0; s < _db.MatchSeconds; s++)
                p = EconomyTick.Advance(p, EconomySide.Player, Base(), R, 1f);

            Assert.AreEqual(R.foodCap, p.Food, 1e-3f, "180s of city food should hit the cap");
            Assert.AreEqual(R.favorCap, p.Favor, 1e-3f, "180s of temple favor should hit the cap");
        }
    }
}
