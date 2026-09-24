using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;

namespace Godsbound.Core.Decks
{
    /// <summary>One building of a deck, in the owner's HALF-LOCAL rows (0 is the row nearest no-man's land).</summary>
    [System.Serializable]
    public sealed class DeckBuilding
    {
        public string type;
        public int c0, r0, c1, r1;
        public Hex A => new Hex(c0, r0);
        public Hex B => new Hex(c1, r1);
        public DeckBuilding() { }
        public DeckBuilding(string type, Hex a, Hex b)
        { this.type = type; c0 = a.C; r0 = a.R; c1 = b.C; r1 = b.R; }
        public DeckBuilding Copy() => new DeckBuilding { type = type, c0 = c0, r0 = r0, c1 = c1, r1 = r1 };
        public override string ToString() => $"{type} {A}-{B}";
    }

    /// <summary>
    /// A complete player setup: faction, the four humans in hand, two heroes, four gods, the painted
    /// half and where its three buildings stand. Ports the browser's deck preset, field for field.
    /// </summary>
    /// <remarks>
    /// <para>Plain data with no behaviour, so it serializes straight to JSON (the browser keeps the
    /// same shape in <c>localStorage</c>) and nothing here depends on a scene. Every rule about what
    /// makes a preset legal lives in <see cref="DeckRules"/>, in one place, because the browser has
    /// exactly one gate — <c>normalizeDeckPreset</c> — and both the editor and the loader pass
    /// through it.</para>
    /// <para>Rows are HALF-LOCAL, as the browser stores them: row 0 is the half's first row, and the
    /// match composes the two halves into absolute board rows at start. Storing absolute rows here
    /// would make a deck unusable by the side it was not saved for.</para>
    /// </remarks>
    [System.Serializable]
    public sealed class DeckPreset
    {
        public string faction;
        public string[] loadout = System.Array.Empty<string>();
        public string[] heroes = System.Array.Empty<string>();
        public string[] gods = System.Array.Empty<string>();

        /// <summary>One string per half-local row, each of <see cref="Board.Cols"/> terrain codes.</summary>
        public string[] terrain = System.Array.Empty<string>();
        public DeckBuilding[] buildings = System.Array.Empty<DeckBuilding>();

        public DeckPreset Copy() => new DeckPreset
        {
            faction = faction,
            loadout = (string[])loadout.Clone(),
            heroes = (string[])heroes.Clone(),
            gods = (string[])gods.Clone(),
            terrain = (string[])terrain.Clone(),
            buildings = buildings.Select(b => b.Copy()).ToArray()
        };

        /// <summary>The terrain of one half-local cell.</summary>
        public TerrainType TerrainAt(int c, int r) => TerrainTable.FromCode(terrain[r][c]);

        public DeckBuilding Building(string type) => buildings.FirstOrDefault(b => b.type == type);

        public override string ToString() =>
            $"{faction} deck: {string.Join(",", loadout)} / {string.Join(",", heroes)} / {string.Join(",", gods)}";
    }

    /// <summary>
    /// The one gate a deck passes through, ported from <c>normalizeDeckPreset</c>.
    /// </summary>
    /// <remarks>
    /// The browser returns null for anything illegal and the caller decides what to do; here the
    /// reason comes back with it, because a rejected deck in Unity is shown to the player rather than
    /// silently replaced. Nothing is repaired: the browser repairs nothing either, and a deck that
    /// half-survived a gate would be worse than one that was refused.
    /// </remarks>
    public static class DeckRules
    {
        public const int HandSize = 4, HeroCount = 2, GodCount = 4, BuildingCount = 3;
        private static readonly string[] BuildingTypes = { "temple", "city", "fortress" };
        private static readonly char[] TerrainCodes = { 'P', 'D', 'F', 'M', 'W' };

        /// <summary>A faction's shipped deck, straight from the export.</summary>
        public static DeckPreset Default(GameDatabase db, string faction = "egypt")
        {
            var data = db.DefaultDeck(faction) ?? db.DefaultDeck("egypt");
            if (data == null) return null;
            return new DeckPreset
            {
                faction = data.faction,
                loadout = data.loadout.ToArray(),
                heroes = data.heroes.ToArray(),
                gods = data.gods.ToArray(),
                terrain = data.terrain.ToArray(),
                buildings = data.buildings.Select(b => new DeckBuilding
                { type = b.type, c0 = b.c0, r0 = b.r0, c1 = b.c1, r1 = b.r1 }).ToArray()
            };
        }

        /// <summary><c>validPick</c>: exactly this many, all different, all from the allowed set.</summary>
        private static bool ValidPick(string[] list, int count, IEnumerable<string> allowed) =>
            list != null && list.Length == count && list.Distinct().Count() == count &&
            list.All(allowed.Contains);

        /// <summary>
        /// Normalize a raw preset, or explain why it cannot stand. A legal preset comes back as a
        /// COPY, so the caller's object can never be aliased into a match.
        /// </summary>
        public static bool TryNormalize(GameDatabase db, DeckPreset raw, out DeckPreset preset, out string problem)
        {
            preset = null;
            problem = null;
            if (db == null) { problem = "no game database"; return false; }
            if (raw == null) { problem = "no deck"; return false; }
            var faction = db.Faction(raw.faction ?? "");
            if (faction == null) { problem = $"unknown faction '{raw.faction}'"; return false; }

            var humans = db.UnitsOf(faction.id, "human").Select(u => u.key).ToList();
            var heroes = db.UnitsOf(faction.id, "hero").Select(u => u.key).ToList();
            var gods = db.Roster(faction.id).Select(g => g.key).ToList();
            if (!ValidPick(raw.loadout, HandSize, humans))
            { problem = $"a hand is exactly {HandSize} different {faction.id} humans"; return false; }
            if (!ValidPick(raw.heroes, HeroCount, heroes))
            { problem = $"a deck fields exactly {HeroCount} different {faction.id} heroes"; return false; }
            if (!ValidPick(raw.gods, GodCount, gods))
            { problem = $"a deck fields exactly {GodCount} different {faction.id} gods"; return false; }

            int rows = db.PlayerRows;
            if (raw.terrain == null || raw.terrain.Length != rows)
            { problem = $"a half is {rows} rows of terrain"; return false; }
            foreach (var row in raw.terrain)
            {
                if (row == null || row.Length != Board.Cols)
                { problem = $"every terrain row is {Board.Cols} cells"; return false; }
                foreach (var code in row)
                    if (System.Array.IndexOf(TerrainCodes, code) < 0)
                    { problem = $"'{code}' is not a terrain"; return false; }
            }
            foreach (var budget in db.TerrainBudgets)
            {
                char code = budget.code[0];
                int painted = raw.terrain.Sum(row => row.Count(ch => ch == code));
                if (painted > budget.max)
                { problem = $"{painted} of '{code}' is over the budget of {budget.max}"; return false; }
            }

            if (raw.buildings == null || raw.buildings.Length != BuildingCount)
            { problem = $"a half holds exactly {BuildingCount} buildings"; return false; }
            var placed = new HashSet<string>();
            var taken = new HashSet<Hex>();
            foreach (var b in raw.buildings)
            {
                if (b == null || System.Array.IndexOf(BuildingTypes, b.type) < 0)
                { problem = $"'{b?.type}' is not a building"; return false; }
                if (!placed.Add(b.type)) { problem = $"two {b.type}s"; return false; }
                // The browser's order, so a preset that breaks two rules is refused for the same
                // reason in both builds: bounds, then adjacency, then what each hex sits on.
                foreach (var h in new[] { b.A, b.B })
                    if (h.C < 0 || h.C >= Board.Cols || h.R < 0 || h.R >= rows)
                    { problem = $"the {b.type} stands off the half at {h}"; return false; }
                if (Hex.Distance(b.A, b.B) != 1)
                { problem = $"the {b.type}'s two hexes are not adjacent"; return false; }
                foreach (var h in new[] { b.A, b.B })
                {
                    if (!taken.Add(h)) { problem = $"the {b.type} overlaps another building at {h}"; return false; }
                    var terrain = TerrainTable.FromCode(raw.terrain[h.R][h.C]);
                    if (terrain == TerrainType.Mountain || terrain == TerrainType.Water)
                    { problem = $"the {b.type} stands on {terrain} at {h}"; return false; }
                }
            }
            preset = raw.Copy();
            preset.faction = faction.id;
            return true;
        }

        /// <summary>The normalized preset, or null with the reason discarded — the browser's own shape.</summary>
        public static DeckPreset Normalize(GameDatabase db, DeckPreset raw) =>
            TryNormalize(db, raw, out var preset, out _) ? preset : null;
    }
}
