using System;

namespace Godsbound.Core.Data
{
    /// <summary>
    /// Income rates, caps and passive multipliers, MEASURED from the browser game's
    /// <c>tickEconomy</c> rather than transcribed from it.
    /// </summary>
    /// <remarks>
    /// <para>Every number here was produced by booting a match in
    /// <c>tools/export_unity_data.js</c>, driving the real <c>tickEconomy</c> with
    /// <c>dt = 1</c> so each delta IS a per-second rate, and temporarily overriding
    /// <c>hasPassive</c> to switch individual passives on. Nothing is hand-typed, so a
    /// balance change in the HTML reaches Unity by re-running the exporter.</para>
    /// <para>Balance belongs in the browser game's tables. Do not edit these values here.</para>
    /// </remarks>
    [Serializable]
    public class EconomyRates
    {
        public int foodCap;
        public int favorCap;

        /// <summary>Global food scalar (<c>FOOD_RATE_MULT</c>).</summary>
        public float foodRateMult;

        /// <summary>Quetzalcoatl's Morning Star food bonus, as a fraction.</summary>
        public float morningStarBonus;

        // Per-second rates with no passives, for direct assertion.
        public float foodWithCity;
        public float foodWithoutCity;
        public float playerFavorWithTemple;
        public float playerFavorWithoutTemple;
        public float aiFavorWithTemple;
        public float aiFavorWithoutTemple;

        // Pre-multiplier food bases, recovered from the measurements.
        public float cityFoodBase;
        public float cityFoodBaseBoosted;
        public float noCityFoodBase;

        public float morningStarFoodMultiplier;
        public float mandateThreeBuildingsMultiplier;
        public float mandateTwoBuildingsMultiplier;

        /// <summary>Demeter's Bounty: a standing City also yields this much favor.</summary>
        public float bountyCityFavorPerSecond;

        /// <summary>Demeter's Blight halts food.</summary>
        public bool blightStopsFood;

        /// <summary>
        /// Blight does NOT halt favor. Measured, not assumed — the browser source's
        /// else-guard wraps only the food line, so favor keeps flowing under blight.
        /// </summary>
        public bool blightStopsFavor;

        public bool capsClamp;

        /// <summary>
        /// True when the player and AI earn favor at different base rates. They did until
        /// 2026-09-16 (3.4/2.2 against 3.0/2.0); Willem then ruled both sides onto one
        /// formula, so the export now measures them equal.
        /// </summary>
        public bool favorIsSideAsymmetric;

        /// <summary>
        /// Demeter's Bounty pays the AI side exactly as it pays the player — measured by
        /// the exporter on both sides. False before the 2026-09-16 unification.
        /// </summary>
        public bool bountyAppliesToAi;
    }
}
