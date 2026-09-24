using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;

namespace Godsbound.Core.Gods
{
    /// <summary>One selected god's live state for one side. Mirrors an entry of the browser's
    /// <c>S.playerGods</c> / <c>S.aiGods</c>.</summary>
    public sealed class GodSlot
    {
        public GodData Def { get; }
        public string Key => Def.key;
        public bool Unlocked { get; internal set; }

        /// <summary>Elapsed time at which the power is ready again (<c>cdUntil</c>). Set by U26.</summary>
        public float CooldownUntil { get; set; }

        /// <summary>Isis's Secret Name lock on this god's power (<c>lockedUntil</c>). Set by U26.</summary>
        public float LockedUntil { get; set; }

        internal GodSlot(GodData def) { Def = def ?? throw new ArgumentNullException(nameof(def)); }

        internal void Reset() { Unlocked = false; CooldownUntil = 0f; LockedUntil = 0f; }

        public override string ToString() => $"{Key}{(Unlocked ? " (unlocked)" : "")}";
    }

    /// <summary>
    /// A side's gods for one match: the four selected, which are unlocked, their cooldown
    /// clocks, and the myths unlocking has added to the side's trainable pool.
    /// </summary>
    /// <remarks>
    /// <para>Ported from the browser's <c>activeGods</c>/<c>activeAiGods</c> plus
    /// <c>S.playerGods</c>/<c>S.aiGods</c>. <see cref="Selected"/> is in ROSTER order
    /// (<c>FACTIONS[f].allGods</c> filtered by the pick), not pick order, because that is the
    /// order the browser iterates — it decides which god the AI buys first when two fall due
    /// on the same tick.</para>
    /// <para>No UnityEngine here. Unlocking is a transaction on <see cref="GodRules"/>; this
    /// class only holds state.</para>
    /// </remarks>
    public sealed class GodState
    {
        /// <summary>A deck always fields exactly this many gods.</summary>
        public const int SelectionSize = 4;

        private readonly List<GodSlot> _selected;
        private readonly List<string> _mythDeck = new List<string>();

        public int Side { get; }

        /// <summary>The pantheon, or empty for a side with no gods (isolated simulation tests).</summary>
        public string Faction { get; }

        public IReadOnlyList<GodSlot> Selected => _selected;

        /// <summary>
        /// Xipe Totec's Flaying: every passive of this side reads false while elapsed time is
        /// before this. The power that sets it is Batch 2; the gate lives in
        /// <see cref="GodRules.HasPassive"/> now so the port of <c>hasPassive</c> is whole.
        /// </summary>
        public float PassivesStrippedUntil { get; set; }

        /// <summary>Myth unit keys in the order their gods were unlocked — the browser's
        /// <c>DECK.push(g.myth)</c>.</summary>
        public IReadOnlyList<string> MythDeck => _mythDeck;

        /// <summary>Raised after a god is unlocked and paid for.</summary>
        public event Action<GodSlot> GodUnlocked;

        private GodState(int side, string faction, List<GodSlot> selected)
        {
            if (side != 0 && side != 1) throw new ArgumentOutOfRangeException(nameof(side), side, "side is 0 or 1");
            Side = side;
            Faction = faction ?? "";
            _selected = selected;
        }

        /// <summary>A side that fields no gods. Every passive reads false and nothing unlocks.</summary>
        public static GodState None(int side) => new GodState(side, "", new List<GodSlot>());

        /// <summary>
        /// Build a side's gods from a faction and a pick of four. Throws with the reason when
        /// the pick breaks the selected-four constraint; nothing is repaired silently.
        /// </summary>
        public static GodState Create(int side, GameDatabase database, string faction, IEnumerable<string> pick)
        {
            var problem = Validate(database, faction, pick);
            if (problem != null) throw new ArgumentException(problem, nameof(pick));
            var keys = pick.ToList();
            var roster = database.Faction(faction).allGodKeys;
            var selected = roster.Where(keys.Contains).Select(k => new GodSlot(database.God(faction, k))).ToList();
            return new GodState(side, faction, selected);
        }

        /// <summary>The faction's default four, as the browser's <c>defaultGodPick</c>.</summary>
        public static GodState Default(int side, GameDatabase database, string faction)
        {
            var f = database?.Faction(faction) ?? throw new ArgumentException($"unknown faction '{faction}'", nameof(faction));
            return Create(side, database, faction, f.defaultGodPick);
        }

        /// <summary>Why a pick is illegal, or null when it is legal.</summary>
        public static string Validate(GameDatabase database, string faction, IEnumerable<string> pick)
        {
            if (database == null) return "no game database";
            var f = database.Faction(faction);
            if (f == null) return $"unknown faction '{faction}'";
            if (pick == null) return "no gods picked";
            var keys = pick.ToList();
            if (keys.Count != SelectionSize) return $"a deck fields exactly {SelectionSize} gods, not {keys.Count}";
            if (keys.Distinct().Count() != keys.Count) return "the same god is picked twice";
            foreach (var k in keys)
                if (!f.allGodKeys.Contains(k)) return $"'{k}' is not a god of {faction}";
            return null;
        }

        public GodSlot Slot(string key) => _selected.FirstOrDefault(s => s.Key == key);

        public bool IsSelected(string key) => Slot(key) != null;

        public bool IsUnlocked(string key) => Slot(key)?.Unlocked == true;

        public IEnumerable<GodSlot> UnlockedSlots => _selected.Where(s => s.Unlocked);

        /// <summary>Unlocked gods' myths in roster order — what the browser's AI reads
        /// (<c>activeAiGods.filter(unlocked).map(myth)</c>).</summary>
        public IEnumerable<string> UnlockedMythsInRosterOrder() =>
            _selected.Where(s => s.Unlocked && s.Def.HasMyth).Select(s => s.Def.myth);

        /// <summary>
        /// Unlock without payment or side effects — for scripted scenarios such as tests that
        /// need a given set of passives. Matches setting <c>S.aiGods[k].unlocked=true</c>.
        /// </summary>
        public bool Grant(string key)
        {
            var slot = Slot(key);
            if (slot == null || slot.Unlocked) return false;
            MarkUnlocked(slot);
            return true;
        }

        internal void MarkUnlocked(GodSlot slot)
        {
            slot.Unlocked = true;
            if (slot.Def.HasMyth) _mythDeck.Add(slot.Def.myth);
            GodUnlocked?.Invoke(slot);
        }

        /// <summary>Back to the start of a match: all locked, clocks cleared, no myths.</summary>
        public void Reset()
        {
            foreach (var s in _selected) s.Reset();
            _mythDeck.Clear();
            PassivesStrippedUntil = 0f;
        }

        public override string ToString() =>
            $"side {Side} {Faction}: " + string.Join(", ", _selected.Select(s => s.ToString()));
    }
}
