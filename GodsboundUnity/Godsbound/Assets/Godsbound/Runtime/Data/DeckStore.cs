using System;
using System.IO;
using UnityEngine;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;

namespace Godsbound.Data
{
    /// <summary>
    /// The player's two device-local deck slots, on disk instead of in <c>localStorage</c>.
    /// </summary>
    /// <remarks>
    /// <para>Ports the browser's two-slot flow: both slots plus which one is active, saved together,
    /// and every slot passing through <see cref="DeckRules.TryNormalize"/> on the way IN and on the
    /// way OUT. A slot that fails the gate reads back as EMPTY rather than as a half-legal deck —
    /// the browser's <c>initDeckPresets</c> does exactly that, and it is what keeps a deck saved by
    /// an older build from starting a match it cannot legally play.</para>
    /// <para>Storage is guarded the same way the browser guards <c>localStorage</c>: a missing file,
    /// unreadable path or corrupt JSON leaves both slots empty and the flow still works for this
    /// session. Nothing here throws at the caller for a bad file.</para>
    /// </remarks>
    public sealed class DeckStore
    {
        public const int SlotCount = 2;

        [Serializable] private sealed class SavedSlot { public bool filled; public DeckPreset preset; }
        [Serializable] private sealed class SavedDecks { public int activeSlot; public SavedSlot[] slots; }

        private readonly DeckPreset[] slots = new DeckPreset[SlotCount];
        private readonly GameDatabase db;

        /// <summary>Where the slots live. One file, beside the rest of the player's own data.</summary>
        public string Path { get; }

        /// <summary>The slot the player last played or edited.</summary>
        public int ActiveSlot { get; private set; }

        /// <summary>Whether the last <see cref="Load"/> found a usable file at all. Diagnostic.</summary>
        public bool Loaded { get; private set; }

        public static string DefaultPath =>
            System.IO.Path.Combine(Application.persistentDataPath, "decks.json");

        public DeckStore(GameDatabase database, string path = null)
        {
            db = database ?? throw new ArgumentNullException(nameof(database));
            Path = string.IsNullOrEmpty(path) ? DefaultPath : path;
        }

        /// <summary>The deck in a slot, or null when that slot is empty.</summary>
        public DeckPreset Slot(int slot) => InRange(slot) ? slots[slot] : null;

        private static bool InRange(int slot) => slot >= 0 && slot < SlotCount;

        /// <summary>
        /// Put a deck in a slot, if it is legal. Returns false with the reason and leaves the slot
        /// untouched — a bad save must never destroy a good deck.
        /// </summary>
        public bool Store(int slot, DeckPreset preset, out string problem)
        {
            problem = null;
            if (!InRange(slot)) { problem = $"there are {SlotCount} deck slots"; return false; }
            if (!DeckRules.TryNormalize(db, preset, out var clean, out problem)) return false;
            slots[slot] = clean;
            ActiveSlot = slot;
            return true;
        }

        public bool Store(int slot, DeckPreset preset) => Store(slot, preset, out _);

        /// <summary>Empty a slot. The file still holds the other one.</summary>
        public void Clear(int slot) { if (InRange(slot)) slots[slot] = null; }

        /// <summary>
        /// The active deck, or the faction default when that slot is empty and a default is asked
        /// for — the browser's <c>applyDeckPreset(slot, useDefault)</c>.
        /// </summary>
        public DeckPreset Active(bool useDefault = false, string fallbackFaction = "egypt") =>
            slots[ActiveSlot] ?? (useDefault ? DeckRules.Default(db, fallbackFaction) : null);

        public bool SelectSlot(int slot)
        {
            if (!InRange(slot)) return false;
            ActiveSlot = slot;
            return true;
        }

        /// <summary>Read the file. A missing, unreadable or corrupt one leaves both slots empty.</summary>
        public void Load()
        {
            slots[0] = slots[1] = null;
            ActiveSlot = 0;
            Loaded = false;
            string text;
            try
            {
                if (!File.Exists(Path)) return;
                text = File.ReadAllText(Path);
            }
            catch (Exception) { return; }
            SavedDecks saved;
            try { saved = JsonUtility.FromJson<SavedDecks>(text); }
            catch (Exception) { return; }
            if (saved == null) return;
            Loaded = true;
            ActiveSlot = saved.activeSlot == 1 ? 1 : 0;
            if (saved.slots == null) return;
            for (int i = 0; i < SlotCount && i < saved.slots.Length; i++)
            {
                var row = saved.slots[i];
                if (row == null || !row.filled) continue;
                // Every slot passes the gate on the way out: a deck from an older build, or one
                // hand-edited on disk, reads back EMPTY rather than starting an illegal match.
                slots[i] = DeckRules.Normalize(db, row.preset);
            }
        }

        /// <summary>Write both slots. A failure is reported, never thrown — saving a deck is not
        /// worth losing a session over.</summary>
        public bool Save()
        {
            var saved = new SavedDecks { activeSlot = ActiveSlot, slots = new SavedSlot[SlotCount] };
            for (int i = 0; i < SlotCount; i++)
                saved.slots[i] = new SavedSlot { filled = slots[i] != null, preset = slots[i] ?? new DeckPreset() };
            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(Path, JsonUtility.ToJson(saved, true));
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
