using System;
using System.Linq;

namespace Godsbound.Core.AI
{
    /// <summary>One entry of a profile's <c>godSched</c>: the earliest elapsed time the AI may buy this god.</summary>
    [Serializable] public sealed class GodScheduleEntry
    {
        public string key;
        public float at;
    }
    [Serializable] public sealed class AiProfile
    {
        public string style, faction;
        public float trainMin, trainRand, heroAt, mythChance;
        public bool rushCity;
        public string[] pool;
        public GodScheduleEntry[] godSched = Array.Empty<GodScheduleEntry>();

        /// <summary>When this profile may buy a god, or null when it never schedules it (the browser's
        /// <c>elapsed()>=undefined</c> is always false).</summary>
        public float? GodDueAt(string key)
        {
            if (godSched == null) return null;
            foreach (var e in godSched) if (e.key == key) return e.at;
            return null;
        }
    }
    /// <summary>One entry of the browser's <c>AI_POWER_CLASS</c>: which trigger rule a god obeys.</summary>
    [Serializable] public sealed class AiPowerClass
    {
        public string key;

        /// <summary>"aoe", "control", "support", "buff" or "situational" — the browser's own words.</summary>
        public string cls;
    }

    [Serializable] public sealed class AiRules
    {
        public float saveMax, initialTrainDelay, initialHeroAt, heroRepeat, heroCounterChance, rangedRatio, mythReserve, waveTimeout;
        public int counterMyths, rangedMinimum, waveMinimum, waveRandom, waypointRow, defenseDistance, defenseMinimum, defenseMaximum, rallySettleDistance;
        public int[] lanes;

        /// <summary>U29 power gates: the shared cast guard, the support window and each class's threshold.</summary>
        public float castGap, supportWindow, castNever;
        public int aoeHits, controlHits, buffUnits, aoeRadius, controlRadius, buffRadius, soilRadius;
        public AiPowerClass[] powerClasses = Array.Empty<AiPowerClass>();

        /// <summary>The trigger class of a god, defaulting as the browser's <c>||"situational"</c> does.</summary>
        public string PowerClass(string key)
        {
            if (powerClasses != null)
                foreach (var entry in powerClasses) if (entry.key == key) return entry.cls;
            return "situational";
        }
    }
    /// <summary>A two-hex footprint, flat because JsonUtility cannot read an array of arrays.</summary>
    [Serializable] public sealed class HexPairData
    {
        public int c0, r0, c1, r1;
        public Hex A => new Hex(c0, r0);
        public Hex B => new Hex(c1, r1);
    }

    [Serializable] public sealed class HexData { public int c, r; public Hex Hex => new Hex(c, r); }

    /// <summary>One of the browser's <c>AI_LAYOUTS</c>: where that arrangement puts each building.</summary>
    [Serializable] public sealed class AiLayoutData
    {
        public HexPairData city, temple, fortress;
        public HexPairData Of(string type) =>
            type == "city" ? city : type == "temple" ? temple : type == "fortress" ? fortress : null;
    }

    [Serializable] public sealed class LaneData { public int[] cols; }
    [Serializable] public sealed class TerrainPatchData { public string code; public HexData[] hexes; }

    /// <summary>
    /// <c>generateAiTerrain</c>'s own weights, measured from its source. Nothing here is a judgement
    /// call: every number is what the browser scores its candidate hexes with.
    /// </summary>
    [Serializable] public sealed class AiTerrainRates
    {
        public LaneData[] lanes;
        public TerrainPatchData[] fallback;
        public int attempts, mountainRowMin, mountainRowMax, mountainLaneBonus, mountainMidRow, mountainMidBonus,
                   mountainJitter, nileStartRowMin, nileStartRowMax, nileStartLaneBonus, nileStartJitter,
                   waterRowMin, waterDeepRow, waterDeepBonus, waterLaneBonus, waterJitter, forestRowMin,
                   forestNearBuildingRange, forestNearBuildingBonus, forestNearMountainBonus,
                   forestFunnelBonus, forestJitter, gateCol;

        public HexData[] Fallback(string code)
        {
            foreach (var patch in fallback) if (patch.code == code) return patch.hexes;
            return Array.Empty<HexData>();
        }
    }

    [Serializable] public sealed class AiData
    {
        public string source;
        public AiProfile[] profiles;
        public AiRules rules;

        /// <summary>The AI's three shipped building arrangements (U32).</summary>
        public AiLayoutData[] layouts = Array.Empty<AiLayoutData>();

        /// <summary>Its terrain generator's lanes, weights and hand-authored fallback (U32).</summary>
        public AiTerrainRates terrain = new AiTerrainRates();
        public AiProfile Profile(string faction, string style) => profiles.Single(p => p.faction == faction && p.style == style);
    }
}
