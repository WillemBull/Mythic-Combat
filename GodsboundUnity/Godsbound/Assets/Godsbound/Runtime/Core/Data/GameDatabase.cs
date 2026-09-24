using System.Collections.Generic;
using System.Linq;

namespace Godsbound.Core.Data
{
    /// <summary>
    /// Indexed, read-only access to the exported tables, plus a referential-integrity check.
    /// </summary>
    /// <remarks>
    /// Read-only on purpose. Balance lives in the browser game's data tables; the Unity side
    /// consumes them and must never edit them, or the two builds drift and the HTML stops
    /// being the source of truth. Re-run <c>tools/export_unity_data.js</c> to change anything
    /// here.
    /// </remarks>
    public sealed class GameDatabase
    {
        private readonly GameDataRoot _root;
        private readonly Dictionary<string, FactionData> _factions;
        private readonly Dictionary<string, UnitData> _units;
        private readonly Dictionary<string, GodData> _gods;

        /// <summary>Key for a unit or god, which is only unique WITHIN a faction.</summary>
        public static string Id(string faction, string key) => faction + "/" + key;

        public GameDatabase(GameDataRoot root)
        {
            _root = root ?? throw new System.ArgumentNullException(nameof(root));

            _factions = root.factions.ToDictionary(f => f.id);
            _units = root.units.ToDictionary(u => Id(u.faction, u.key));
            _gods = root.gods.ToDictionary(g => Id(g.faction, g.key));
        }

        public GameDataRoot Raw => _root;

        public int FoodCap => _root.foodCap;
        public int FavorCap => _root.favorCap;
        public int MatchSeconds => _root.matchSeconds;

        /// <summary>Income rates, caps and passive multipliers.</summary>
        public EconomyRates Economy => _root.economy;

        /// <summary>findPath's cost-function constants.</summary>
        public PathfindingRates Pathfinding => _root.pathfinding;

        /// <summary>Price and queue modifiers.</summary>
        public TrainingRates Training => _root.training;

        public MovementRates Movement => _root.movement;

        public CombatRates Combat => _root.combat;
        public FortressRates Fortresses => _root.fortresses;

        /// <summary>God constants measured from function bodies (U23).</summary>
        public GodRates GodRates => _root.godRates;

        /// <summary>God-power coefficients read from applyGodPower (U26).</summary>
        public PowerRates Powers => _root.powers;

        /// <summary>A unit for a side's faction, falling back to any faction — the browser's <c>unitDef</c> resolver.</summary>
        public UnitData ResolveUnit(string faction, string key) =>
            Unit(faction, key) ?? _root.units.FirstOrDefault(u => u.key == key);

        /// <summary>How deep one side's half is, as the browser counts it.</summary>
        public int PlayerRows => _root.playerRows;

        /// <summary>How much of a paintable terrain a deck may hold; 0 for one with no budget.</summary>
        public int TerrainBudget(char code)
        {
            foreach (var entry in _root.terrainBudget)
                if (entry.code.Length > 0 && entry.code[0] == code) return entry.max;
            return 0;
        }

        /// <summary>Every paintable terrain, in the browser's own order.</summary>
        public IReadOnlyList<TerrainBudgetData> TerrainBudgets => _root.terrainBudget;

        /// <summary>A faction's shipped starting deck, or null for an unknown faction.</summary>
        public DeckPresetData DefaultDeck(string faction) =>
            _root.defaultDecks.FirstOrDefault(d => d.faction == faction);

        /// <summary>A faction's units of one category, in table order — the browser's own filter.</summary>
        public IEnumerable<UnitData> UnitsOf(string faction, string category) =>
            _root.units.Where(u => u.faction == faction && u.cat == category);
        /// <summary>A faction's full god roster, in the browser's <c>allGods</c> order.</summary>
        public IReadOnlyList<GodData> Roster(string faction)
        {
            var f = Faction(faction);
            return f == null ? new List<GodData>() : f.allGodKeys.Select(k => God(faction, k)).Where(g => g != null).ToList();
        }

        public IReadOnlyCollection<FactionData> Factions => _root.factions;
        public IReadOnlyCollection<UnitData> AllUnits => _root.units;
        public IReadOnlyCollection<GodData> AllGods => _root.gods;

        /// <summary>Per-type building stats.</summary>
        public IReadOnlyCollection<BuildingTypeData> BuildingTypes => _root.buildingTypes;

        /// <summary>
        /// The six starting buildings, DERIVED rather than exported.
        /// </summary>
        /// <remarks>
        /// <para>There is no canonical layout to export. The player's comes from the deck
        /// preset; the AI's is randomized at match start. An earlier version snapshotted the
        /// live array and got a different layout on every export — one of which placed the
        /// AI city on (4,0) and silently broke a pathfinding test.</para>
        /// <para>So the player's three come from the exported preset, and the AI's are
        /// mirrored about the board's centre: same columns, same offset from its own edge.
        /// That reproduces the browser game's module-load layout exactly and is stable.
        /// Row mirroring is safe here only because every footprint is a same-row horizontal
        /// pair, which is adjacent at any column parity; <c>Building</c>'s constructor
        /// re-checks adjacency regardless.</para>
        /// <para>This is a DEFAULT for scenes and tests. Once setup and the AI's layout
        /// generator are ported, both sides' real layouts come from those instead.</para>
        /// </remarks>
        public IReadOnlyCollection<BuildingData> Buildings
        {
            get
            {
                if (_derivedBuildings != null) return _derivedBuildings;

                var byType = _root.buildingTypes.ToDictionary(t => t.type);
                var list = new List<BuildingData>();

                foreach (var lay in _root.defaultPlayerBuildings)
                {
                    if (!byType.TryGetValue(lay.type, out var t))
                        throw new System.InvalidOperationException(
                            $"default layout names '{lay.type}' but no such building type was exported");

                    list.Add(Make(0, t, lay.c0, lay.r0, lay.c1, lay.r1));
                    // Mirror into the AI's half: row r becomes (Rows-1-r).
                    list.Add(Make(1, t, lay.c0, Board.Rows - 1 - lay.r0,
                                        lay.c1, Board.Rows - 1 - lay.r1));
                }

                _derivedBuildings = list;
                return _derivedBuildings;
            }
        }

        private List<BuildingData> _derivedBuildings;

        private static BuildingData Make(int side, BuildingTypeData t,
                                         int c0, int r0, int c1, int r1) =>
            new BuildingData
            {
                side = side, type = t.type, name = t.name, emoji = t.emoji,
                c0 = c0, r0 = r0, c1 = c1, r1 = r1,
                hp = t.hp, maxHp = t.hp
            };

        public FactionData Faction(string id) =>
            _factions.TryGetValue(id, out var f) ? f : null;

        public UnitData Unit(string faction, string key) =>
            _units.TryGetValue(Id(faction, key), out var u) ? u : null;

        public GodData God(string faction, string key) =>
            _gods.TryGetValue(Id(faction, key), out var g) ? g : null;

        public IEnumerable<UnitData> UnitsOf(string faction) =>
            _root.units.Where(u => u.faction == faction);

        public IEnumerable<GodData> GodsOf(string faction) =>
            _root.gods.Where(g => g.faction == faction);

        /// <summary>
        /// Every way the export could be internally inconsistent, as a list of messages.
        /// Empty means sound. Run in a test rather than at load time — a shipped build should
        /// not pay for it.
        /// </summary>
        /// <remarks>
        /// Worth having because a typo in a default loadout is invisible until a match tries
        /// to deal a card that does not exist.
        /// </remarks>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();

            foreach (var f in _root.factions)
            {
                foreach (var k in f.unitKeys)
                    if (Unit(f.id, k) == null)
                        problems.Add($"{f.id}: unitKeys lists '{k}' but no such unit was exported");

                foreach (var k in f.allGodKeys)
                    if (God(f.id, k) == null)
                        problems.Add($"{f.id}: allGodKeys lists '{k}' but no such god was exported");

                CheckKeys(problems, f, f.defaultLoadout, "defaultLoadout", expectHero: false);
                CheckKeys(problems, f, f.defaultHeroes, "defaultHeroes", expectHero: true);

                foreach (var k in f.defaultGodPick)
                    if (God(f.id, k) == null)
                        problems.Add($"{f.id}: defaultGodPick '{k}' is not a god of this faction");

                if (f.defaultLoadout.Count != 4)
                    problems.Add($"{f.id}: defaultLoadout has {f.defaultLoadout.Count} units, expected 4");
                if (f.defaultHeroes.Count != 2)
                    problems.Add($"{f.id}: defaultHeroes has {f.defaultHeroes.Count}, expected 2");
                if (f.defaultGodPick.Count != 4)
                    problems.Add($"{f.id}: defaultGodPick has {f.defaultGodPick.Count}, expected 4");
            }

            // A god's myth must name a real unit of the same faction, or unlocking the god
            // makes an unbuyable card.
            foreach (var g in _root.gods)
                if (!string.IsNullOrEmpty(g.myth) && Unit(g.faction, g.myth) == null)
                    problems.Add($"{g.faction}/{g.key}: myth '{g.myth}' is not a unit of this faction");

            foreach (var u in _root.units)
            {
                if (u.costFood == 0 && u.costFavor == 0 && u.cat != "form")
                    problems.Add($"{u.faction}/{u.key}: costs nothing and is not a 'form'");
                if (u.hp <= 0 && !u.GetBool("inert"))
                    problems.Add($"{u.faction}/{u.key}: has no hp");
            }

            return problems;
        }

        private void CheckKeys(List<string> problems, FactionData f, IEnumerable<string> keys,
                               string what, bool expectHero)
        {
            foreach (var k in keys)
            {
                var u = Unit(f.id, k);
                if (u == null)
                {
                    problems.Add($"{f.id}: {what} '{k}' is not a unit of this faction");
                    continue;
                }
                if (expectHero && !u.IsHero)
                    problems.Add($"{f.id}: {what} '{k}' is not a hero (cat={u.cat})");
                if (!expectHero && u.cat != "human")
                    problems.Add($"{f.id}: {what} '{k}' should be a human unit (cat={u.cat})");
            }
        }
    }
}
