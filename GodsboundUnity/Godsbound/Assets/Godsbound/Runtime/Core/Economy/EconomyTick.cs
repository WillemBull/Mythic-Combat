using Godsbound.Core.Data;

namespace Godsbound.Core.Economy
{
    /// <summary>Which side's purse and rate table a tick applies to.</summary>
    public enum EconomySide
    {
        Player = 0,
        Ai = 1
    }

    /// <summary>A side's held resources.</summary>
    public readonly struct Purse
    {
        public readonly float Food;
        public readonly float Favor;

        public Purse(float food, float favor)
        {
            Food = food;
            Favor = favor;
        }

        public static readonly Purse Empty = new Purse(0f, 0f);

        public override string ToString() => $"food {Food:0.##}, favor {Favor:0.##}";
    }

    /// <summary>
    /// Everything outside the economy that changes what a side earns this tick.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than looked up, which is what keeps the tick pure and
    /// testable without a match, a scene or a god registry. Passive detection, building
    /// state and terrain counting all belong to layers that do not exist yet.
    /// </remarks>
    public readonly struct EconomyConditions
    {
        /// <summary>City standing. Food falls to the fallback rate without it.</summary>
        public readonly bool CityStanding;

        /// <summary>Temple standing. Favor falls to the floor without it.</summary>
        public readonly bool TempleStanding;

        /// <summary>
        /// Whether this faction's Temple raises favor at all — Aztec sets
        /// <c>templeBonus:false</c>, so its Temple never lifts favor above the floor.
        /// </summary>
        public readonly bool TempleBonus;

        /// <summary>Bastet's Home's Protection: City food +20%.</summary>
        public readonly bool CityFoodBoost;

        /// <summary>Quetzalcoatl's Morning Star: food accelerated.</summary>
        public readonly bool MorningStar;

        /// <summary>Demeter's Bounty: a standing City also yields favor.</summary>
        public readonly bool Bounty;

        /// <summary>Demeter's Blight on this side: nothing grows.</summary>
        public readonly bool Blighted;

        /// <summary>Jade Emperor's Mandate of Heaven.</summary>
        public readonly bool MandateOfHeaven;

        /// <summary>Buildings of this side still standing, which scales the Mandate.</summary>
        public readonly int StandingBuildings;

        /// <summary>Long Wang's Lord of the Four Seas, already resolved to favor/second.</summary>
        public readonly float WaterFavorPerSecond;

        public EconomyConditions(
            bool cityStanding = true,
            bool templeStanding = true,
            bool templeBonus = true,
            bool cityFoodBoost = false,
            bool morningStar = false,
            bool bounty = false,
            bool blighted = false,
            bool mandateOfHeaven = false,
            int standingBuildings = 3,
            float waterFavorPerSecond = 0f)
        {
            CityStanding = cityStanding;
            TempleStanding = templeStanding;
            TempleBonus = templeBonus;
            CityFoodBoost = cityFoodBoost;
            MorningStar = morningStar;
            Bounty = bounty;
            Blighted = blighted;
            MandateOfHeaven = mandateOfHeaven;
            StandingBuildings = standingBuildings;
            WaterFavorPerSecond = waterFavorPerSecond;
        }
    }

    /// <summary>
    /// The resource tick, ported from <c>tickEconomy(dt)</c>.
    /// </summary>
    /// <remarks>
    /// <para>Pure: no scene, no singletons, no time source. The caller owns the purse and
    /// passes <c>dt</c>, so a test can advance a whole match in a loop and the simulation
    /// can be stepped deterministically.</para>
    /// <para>Every rate comes from <see cref="EconomyRates"/>, which the exporter MEASURED
    /// off the real <c>tickEconomy</c>. There are deliberately no numeric literals in this
    /// file — balance lives in the browser game's tables.</para>
    /// </remarks>
    public static class EconomyTick
    {
        /// <summary>
        /// The Mandate of Heaven multiplier for a side, from how many of its buildings
        /// still stand. Full at three, reduced at two, gone below that.
        /// </summary>
        public static float MandateMultiplier(EconomyConditions c, EconomyRates r)
        {
            if (!c.MandateOfHeaven) return 1f;
            if (c.StandingBuildings >= 3) return r.mandateThreeBuildingsMultiplier;
            if (c.StandingBuildings == 2) return r.mandateTwoBuildingsMultiplier;
            return 1f;
        }

        /// <summary>Food per second for a side under these conditions.</summary>
        public static float FoodPerSecond(EconomyConditions c, EconomyRates r)
        {
            if (c.Blighted && r.blightStopsFood) return 0f;

            float bas = c.CityStanding
                ? (c.CityFoodBoost ? r.cityFoodBaseBoosted : r.cityFoodBase)
                : r.noCityFoodBase;

            float morning = c.MorningStar ? 1f + r.morningStarBonus : 1f;

            return r.foodRateMult * bas * morning * MandateMultiplier(c, r);
        }

        /// <summary>
        /// Favor per second for a side under these conditions.
        /// </summary>
        /// <remarks>
        /// The browser game uses ONE favor formula for both sides since 2026-09-16
        /// (<c>favorRate</c>). The side still selects its exported rate pair, which the
        /// exporter measures separately, and Bounty applies to whichever side holds it
        /// whenever the export says it does, so any future asymmetry reaches Unity as data.
        /// </remarks>
        public static float FavorPerSecond(EconomySide side, EconomyConditions c, EconomyRates r)
        {
            if (c.Blighted && r.blightStopsFavor) return 0f;

            bool templeLifts = c.TempleStanding && c.TempleBonus;

            float bas = side == EconomySide.Player
                ? (templeLifts ? r.playerFavorWithTemple : r.playerFavorWithoutTemple)
                : (templeLifts ? r.aiFavorWithTemple : r.aiFavorWithoutTemple);

            bool bountyPays = side == EconomySide.Player || r.bountyAppliesToAi;
            float cityFavor = (bountyPays && c.Bounty && c.CityStanding)
                ? r.bountyCityFavorPerSecond
                : 0f;

            return bas * MandateMultiplier(c, r) + c.WaterFavorPerSecond + cityFavor;
        }

        /// <summary>
        /// Advance a purse by <paramref name="dt"/> seconds, clamped to the caps.
        /// </summary>
        /// <remarks>
        /// Clamping happens per resource, as in the browser game: hitting the food cap does
        /// not stall favor.
        /// </remarks>
        public static Purse Advance(Purse purse, EconomySide side, EconomyConditions c,
                                    EconomyRates r, float dt)
        {
            if (dt < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(dt), dt, "dt cannot be negative");

            float food = Clamp(purse.Food + dt * FoodPerSecond(c, r), r.foodCap);
            float favor = Clamp(purse.Favor + dt * FavorPerSecond(side, c, r), r.favorCap);
            return new Purse(food, favor);
        }

        private static float Clamp(float value, float cap) => value > cap ? cap : value;
    }
}
