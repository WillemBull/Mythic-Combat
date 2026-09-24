using Godsbound.Core.Buildings;

namespace Godsbound.Presentation
{
    /// <summary>How beaten-up a building looks. Roadmap 6.6's two thresholds.</summary>
    public enum BuildingWear { Whole, Cracked, Burning }

    /// <summary>
    /// Roadmap 6.6: cracks under half health, smoke under a quarter. The two fractions are the
    /// step's own, not a measurement — the browser never drew damage states, so there is nothing
    /// to export. Everything here is deterministic in the building's own hex, so a wall's cracks
    /// stay put frame to frame instead of crawling.
    /// </summary>
    public static class BuildingDamageState
    {
        public const float CrackBelow = 0.5f;
        public const float SmokeBelow = 0.25f;

        /// <summary>Cracks drawn at Cracked and above; 6.6 asks for "2-3".</summary>
        public const int Cracks = 3;
        public const int Smokes = 3;

        public static BuildingWear For(float hp, float maxHp)
        {
            if (maxHp <= 0f) return BuildingWear.Whole;
            float ratio = hp / maxHp;
            if (ratio < SmokeBelow) return BuildingWear.Burning;
            if (ratio < CrackBelow) return BuildingWear.Cracked;
            return BuildingWear.Whole;
        }

        public static BuildingWear For(Building building) =>
            building == null || building.Dead ? BuildingWear.Whole : For(building.Hp, building.MaxHp);

        /// <summary>
        /// A stable pseudo-random in [0,1) from the building's anchor hex and a slot index, so two
        /// walls crack differently but the same wall cracks the same way every frame.
        /// </summary>
        public static float Jitter(Building building, int slot, int channel)
        {
            int seed = building == null ? 0 : building.HexA.C * 73856093 ^ building.HexA.R * 19349663;
            seed ^= (slot + 1) * 83492791 ^ (channel + 1) * 19349669;
            uint h = (uint)seed;
            h ^= h >> 16; h *= 2246822507u; h ^= h >> 13; h *= 3266489909u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
