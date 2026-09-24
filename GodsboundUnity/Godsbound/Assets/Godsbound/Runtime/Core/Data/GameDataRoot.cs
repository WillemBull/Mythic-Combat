using System;
using System.Collections.Generic;

namespace Godsbound.Core.Data
{
    /// <summary>One value from a table's long tail of optional fields.</summary>
    /// <remarks>
    /// Units carry 74 distinct fields across the four factions and gods carry 16. Giving
    /// every one a typed C# property would be a large hand-transcription — exactly what U5
    /// exists to avoid — and most are ability flags only one or two units use. So the core
    /// stats are typed and the rest arrive here as key/kind/value. The Unity tests assert
    /// that every field in the export's inventory is either typed or present in extras, so
    /// a new field in the HTML fails a test rather than disappearing.
    /// </remarks>
    [Serializable]
    public class ExtraValue
    {
        public string key;
        /// <summary>"number", "bool" or "string".</summary>
        public string kind;
        /// <summary>Always the stringified value; read it through the typed accessors.</summary>
        public string value;
    }

    /// <summary>A unit, hero, myth or transient form, from one faction's table.</summary>
    [Serializable]
    public class UnitData
    {
        public string faction;
        public string key;
        public string name;
        public string emoji;

        /// <summary>Food component of the cost. 0 when the unit costs no food.</summary>
        public int costFood;

        /// <summary>Favor component of the cost. 0 when the unit costs no favor.</summary>
        public int costFavor;

        /// <summary>"human", "hero", "myth", or "form" — see the category triangle in HANDOFF.</summary>
        public string cat;

        public string armor;
        public string dtype;
        public float hp;
        public float dmg;

        /// <summary>Attacks per second — the browser table's <c>as</c>, renamed because
        /// <c>as</c> is a C# keyword.</summary>
        public float attackSpeed;

        public int range;
        public float speed;
        public int size;
        public int train;

        public bool hasPerSideScale;
        public float spriteScaleUniform;
        public float spriteScaleSide0;
        public float spriteScaleSide1;

        public List<ExtraValue> extras = new List<ExtraValue>();

        public bool IsHero => cat == "hero" || GetBool("hero");
        public bool IsFlying => GetBool("flying");
        public bool IsSiege => GetBool("siege");

        /// <summary>True when the table defines this optional field at all.</summary>
        public bool Has(string field) => Find(field) != null;

        public float GetFloat(string field, float fallback = 0f)
        {
            var e = Find(field);
            return e != null && float.TryParse(e.value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        public bool GetBool(string field, bool fallback = false)
        {
            var e = Find(field);
            return e != null ? e.value == "true" : fallback;
        }

        public string GetString(string field, string fallback = "")
        {
            var e = Find(field);
            return e != null ? e.value : fallback;
        }

        private ExtraValue Find(string field)
        {
            if (extras == null) return null;
            for (int i = 0; i < extras.Count; i++)
                if (extras[i].key == field) return extras[i];
            return null;
        }

        public override string ToString() => $"{faction}/{key} ({name})";
    }

    /// <summary>A playable pantheon and its default deck.</summary>
    [Serializable]
    public class FactionData
    {
        public string id;
        public string name;
        public string label;
        public string aiTitle;

        public List<string> unitKeys = new List<string>();
        public List<string> allGodKeys = new List<string>();

        /// <summary>The four default human units.</summary>
        public List<string> defaultLoadout = new List<string>();

        /// <summary>The two default heroes.</summary>
        public List<string> defaultHeroes = new List<string>();

        /// <summary>The four default gods.</summary>
        public List<string> defaultGodPick = new List<string>();

        /// <summary>Per-faction economy overrides, e.g. Aztec's favorPerDeath.</summary>
        public List<ExtraValue> economy = new List<ExtraValue>();

        public override string ToString() => $"{id} ({name})";
    }

    /// <summary>One paintable terrain and how much of it a deck may hold.</summary>
    [System.Serializable] public sealed class TerrainBudgetData { public string code; public int max; }

    /// <summary>One building of a deck preset, in the owner's HALF-LOCAL rows.</summary>
    [System.Serializable] public sealed class DeckBuildingData { public string type; public int c0, r0, c1, r1; }

    /// <summary>A whole deck: faction, hand, heroes, gods, painted half and building layout.</summary>
    [System.Serializable] public sealed class DeckPresetData
    {
        public string faction;
        public List<string> loadout = new List<string>();
        public List<string> heroes = new List<string>();
        public List<string> gods = new List<string>();

        /// <summary>One string per half-local row, each of <see cref="Board.Cols"/> terrain codes.</summary>
        public List<string> terrain = new List<string>();
        public List<DeckBuildingData> buildings = new List<DeckBuildingData>();
    }

    /// <summary>
    /// The whole export, exactly as <c>tools/export_unity_data.js</c> writes it.
    /// </summary>
    /// <remarks>
    /// Deliberately plain: public fields, <see cref="SerializableAttribute"/>, no
    /// UnityEngine types. That keeps <see cref="Godsbound.Core"/> engine-free while still
    /// being populatable by <c>JsonUtility</c> from the loader, which does reference
    /// UnityEngine.
    /// </remarks>
    [Serializable]
    public class GameDataRoot
    {
        public string generated;
        public string source;

        public int foodCap;
        public int favorCap;
        public int matchSeconds;

        /// <summary>Income rates and caps, measured from the browser game.</summary>
        public EconomyRates economy = new EconomyRates();

        /// <summary>findPath's cost-function constants.</summary>
        public PathfindingRates pathfinding = new PathfindingRates();

        /// <summary>Price and queue modifiers.</summary>
        public TrainingRates training = new TrainingRates();

        public MovementRates movement = new MovementRates();

        public CombatRates combat = new CombatRates();
        public FortressRates fortresses = new FortressRates();

        public List<FactionData> factions = new List<FactionData>();
        public List<UnitData> units = new List<UnitData>();

        /// <summary>Per-type building stats. Three entries.</summary>
        public List<BuildingTypeData> buildingTypes = new List<BuildingTypeData>();

        /// <summary>The player's default layout, in absolute rows.</summary>
        public List<BuildingLayoutData> defaultPlayerBuildings = new List<BuildingLayoutData>();
        public List<GodData> gods = new List<GodData>();

        /// <summary>God-related constants measured from the browser (U23).</summary>
        public GodRates godRates = new GodRates();

        /// <summary>God-power coefficients (U26).</summary>
        public PowerRates powers = new PowerRates();

        /// <summary>How deep a side's own half is, in rows — the browser's <c>PLAYER_ROWS</c>.</summary>
        public int playerRows;

        /// <summary>How much of each paintable terrain a deck may hold (<c>TERRAIN_BUDGET</c>).</summary>
        public List<TerrainBudgetData> terrainBudget = new List<TerrainBudgetData>();

        /// <summary>Each faction's shipped starting deck (<c>defaultDeckPreset</c>).</summary>
        public List<DeckPresetData> defaultDecks = new List<DeckPresetData>();

        /// <summary>Every unit field name seen anywhere in the browser tables.</summary>
        public List<string> unitFieldInventory = new List<string>();

        /// <summary>The subset given typed C# properties.</summary>
        public List<string> typedUnitFields = new List<string>();

        public List<string> godFieldInventory = new List<string>();
        public List<string> typedGodFields = new List<string>();
    }
}
