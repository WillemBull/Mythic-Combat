using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;

namespace Godsbound.Core.Setup
{
    /// <summary>Where the player is in setup. The browser's <c>S.setupStep</c>, minus the steps this port has not taken.</summary>
    public enum SetupStep
    {
        Home,
        Decks,
        Faction,
        Gods,
        Heroes,
        Units,
        Terrain
    }

    /// <summary>What a building drag is currently proposing, and whether it would be allowed.</summary>
    public readonly struct BuildingDrag
    {
        public readonly string Type;
        public readonly Hex A, B;
        public readonly bool Legal;
        public BuildingDrag(string type, Hex a, Hex b, bool legal) { Type = type; A = a; B = b; Legal = legal; }
        public bool Active => !string.IsNullOrEmpty(Type);
    }

    /// <summary>
    /// One pass through setup: the faction, the picks, the painted half and where its buildings
    /// stand — everything the browser keeps across <c>S.playerFaction</c>, <c>S.godPick</c>,
    /// <c>S.heroPick</c>, <c>S.loadout</c>, <c>playerHalf</c> and <c>S.setupStep</c>.
    /// </summary>
    /// <remarks>
    /// <para>Engine-free on purpose: every rule the setup UI enforces is testable without a scene,
    /// and the scene's job is reduced to drawing what this says and forwarding taps. A control that
    /// writes to scene state instead of through here is a bug — the deck the player saves is built
    /// from THIS, not from what the buttons happen to look like.</para>
    /// <para>The picks are ordered lists with a cap, as the browser's are: tapping a chosen god
    /// removes it, tapping a new one appends while there is room, and a full set simply refuses.
    /// Nothing is swapped out from under the player.</para>
    /// </remarks>
    public sealed class SetupSession
    {
        private readonly GameDatabase db;
        private readonly List<string> gods = new List<string>();
        private readonly List<string> heroes = new List<string>();
        private readonly List<string> loadout = new List<string>();
        private string dragType;
        private Hex dragA, dragB;

        public SetupStep Step { get; private set; }
        public string Faction { get; private set; }
        public BoardHalf Half { get; private set; }

        /// <summary>The paint brush, or 0 for none. 'D' is the eraser, as in the browser.</summary>
        public char Brush { get; private set; }

        /// <summary>The last thing setup said to the player. Presentation shows it; nothing reads it back.</summary>
        public string Hint { get; private set; } = "";

        /// <summary>Which of the two deck slots this pass will save into.</summary>
        public int Slot { get; set; }

        public IReadOnlyList<string> Gods => gods;
        public IReadOnlyList<string> Heroes => heroes;
        public IReadOnlyList<string> Loadout => loadout;
        public BuildingDrag Drag => new BuildingDrag(dragType, dragA, dragB,
            dragType != null && BoardSetup.CanPlaceBuilding(Half, Half.Buildings.First(b => b.type == dragType), dragA, dragB));

        /// <summary>Raised whenever anything the UI draws has changed.</summary>
        public event Action Changed;

        public SetupSession(GameDatabase database, DeckPreset start = null, SetupStep step = SetupStep.Home)
        {
            db = database ?? throw new ArgumentNullException(nameof(database));
            Step = step;
            Load(start ?? DeckRules.Default(db));
        }

        /// <summary>Begin from a deck: its faction, its picks and its half.</summary>
        public void Load(DeckPreset preset)
        {
            var clean = DeckRules.Normalize(db, preset) ?? DeckRules.Default(db);
            Faction = clean.faction;
            gods.Clear(); gods.AddRange(clean.gods);
            heroes.Clear(); heroes.AddRange(clean.heroes);
            loadout.Clear(); loadout.AddRange(clean.loadout);
            Half = BoardHalf.FromPreset(clean);
            dragType = null;
            Brush = '\0';
            Changed?.Invoke();
        }

        public void Go(SetupStep step)
        {
            Step = step;
            dragType = null;
            Changed?.Invoke();
        }

        /// <summary>
        /// Choose the faction. A CHANGE empties every pick and starts a fresh half, because a deck's
        /// units and gods belong to its pantheon — the browser clears them the same way rather than
        /// carrying a Chinese hand into an Egyptian deck.
        /// </summary>
        public bool ChooseFaction(string faction)
        {
            if (db.Faction(faction) == null) { Say($"there is no {faction} pantheon"); return false; }
            if (faction == Faction) return true;
            Load(DeckRules.Default(db, faction));
            Say($"{faction} it is — now choose four gods");
            return true;
        }

        /// <summary>The gods this faction offers, in roster order.</summary>
        public IEnumerable<GodData> GodChoices() => db.Roster(Faction);

        /// <summary>Its heroes and its humans, in table order.</summary>
        public IEnumerable<UnitData> HeroChoices() => db.UnitsOf(Faction, "hero");
        public IEnumerable<UnitData> UnitChoices() => db.UnitsOf(Faction, "human");

        public bool ToggleGod(string key) => Toggle(gods, key, DeckRules.GodCount,
            GodChoices().Select(g => g.key), "gods");
        public bool ToggleHero(string key) => Toggle(heroes, key, DeckRules.HeroCount,
            HeroChoices().Select(u => u.key), "heroes");
        public bool ToggleUnit(string key) => Toggle(loadout, key, DeckRules.HandSize,
            UnitChoices().Select(u => u.key), "cards");

        private bool Toggle(List<string> picks, string key, int cap, IEnumerable<string> allowed, string what)
        {
            if (!allowed.Contains(key)) { Say($"{key} does not fight for {Faction}"); return false; }
            int at = picks.IndexOf(key);
            if (at >= 0) { picks.RemoveAt(at); Changed?.Invoke(); return true; }
            if (picks.Count >= cap) { Say($"{cap} {what} is the whole deck — drop one first"); return false; }
            picks.Add(key);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Pick up a brush, or the same one again to put it down.</summary>
        public void ChooseBrush(char code)
        {
            Brush = Brush == code ? '\0' : code;
            Changed?.Invoke();
        }

        /// <summary>How much of a terrain is left to paint.</summary>
        public int Remaining(char code) => db.TerrainBudget(code) - Half.PaintedCount(code);

        /// <summary>Paint (or erase) with the brush in hand. Nothing happens without one.</summary>
        public bool PaintAt(Hex h)
        {
            if (Brush == '\0') return false;
            if (!BoardSetup.TryPaint(Half, db, h, Brush, out var problem)) { Say(problem); return false; }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Take hold of one of your buildings. The browser starts its drag the same way.</summary>
        public bool BeginDrag(Hex h)
        {
            var b = Half.At(h);
            if (b == null) return false;
            dragType = b.type;
            dragA = b.A;
            dragB = b.B;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Propose a new footprint under the pointer. The browser anchors the pair at the pointed hex
        /// and lays the second beside it, so a building always keeps its horizontal footprint.
        /// </summary>
        public bool DragTo(Hex h)
        {
            if (dragType == null) return false;
            dragA = h;
            dragB = new Hex(h.C + 1, h.R);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Let go. The building moves only if the two hexes would take it.</summary>
        public bool DropDrag()
        {
            if (dragType == null) return false;
            var type = dragType;
            var to = (dragA, dragB);
            dragType = null;
            bool moved = BoardSetup.TryMoveBuilding(Half, type, to.Item1, to.Item2);
            if (!moved) Say($"the {type} does not fit there");
            Changed?.Invoke();
            return moved;
        }

        public void CancelDrag()
        {
            if (dragType == null) return;
            dragType = null;
            Changed?.Invoke();
        }

        /// <summary>The deck as it stands, legal or not — what the gate is asked about.</summary>
        public DeckPreset ToPreset() => new DeckPreset
        {
            faction = Faction,
            loadout = loadout.ToArray(),
            heroes = heroes.ToArray(),
            gods = gods.ToArray(),
            terrain = Half.RowStrings(),
            buildings = Half.Buildings.Select(b => b.Copy()).ToArray()
        };

        /// <summary>Is this deck ready to fight? The reason is the player's, not a log line.</summary>
        public bool CanStart(out DeckPreset preset, out string problem) =>
            DeckRules.TryNormalize(db, ToPreset(), out preset, out problem);

        public bool CanStart() => CanStart(out _, out _);

        private void Say(string hint)
        {
            Hint = hint ?? "";
            Changed?.Invoke();
        }
    }
}
