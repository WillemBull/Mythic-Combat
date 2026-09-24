using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// U31: the deck model and its two local slots, gate for gate against the browser's
    /// <c>normalizeDeckPreset</c> (<c>node tools/export_unity_decks.js</c>).
    /// </summary>
    public class DeckTests
    {
        [Serializable] public class CaseBuilding { public string type; public int c0, r0, c1, r1; }
        [Serializable] public class CasePreset
        {
            public string faction; public string[] loadout, heroes, gods, terrain; public CaseBuilding[] buildings;
        }
        [Serializable] public class DeckCase { public string name; public CasePreset preset, normalized; public bool ok; }
        [Serializable] public class BudgetRow { public string code; public int max; }
        [Serializable] public class Fixture { public int playerRows, cols; public BudgetRow[] budget; public DeckCase[] cases; }

        private GameDatabase db;
        private Fixture fx;
        private string scratch;

        [OneTimeSetUp] public void Load()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
            fx = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/deck_reference.json")));
        }

        [SetUp] public void Scratch()
        {
            scratch = Path.Combine(Path.GetTempPath(), "godsbound-decks-" + Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown] public void Sweep()
        {
            try { if (File.Exists(scratch)) File.Delete(scratch); } catch (IOException) { }
        }

        private static DeckPreset From(CasePreset p) => new DeckPreset
        {
            faction = p.faction,
            loadout = p.loadout ?? Array.Empty<string>(),
            heroes = p.heroes ?? Array.Empty<string>(),
            gods = p.gods ?? Array.Empty<string>(),
            terrain = p.terrain ?? Array.Empty<string>(),
            buildings = (p.buildings ?? Array.Empty<CaseBuilding>())
                .Select(b => new DeckBuilding { type = b.type, c0 = b.c0, r0 = b.r0, c1 = b.c1, r1 = b.r1 }).ToArray()
        };

        [Test] public void TheExportCarriesTheHalfDepthAndTheTerrainBudget()
        {
            Assert.That(db.PlayerRows, Is.EqualTo(fx.playerRows));
            Assert.That(db.PlayerRows, Is.EqualTo(Board.PlayerRows), "the board and the export agree");
            Assert.That(fx.cols, Is.EqualTo(Board.Cols));
            Assert.That(db.TerrainBudgets.Count, Is.EqualTo(fx.budget.Length));
            foreach (var row in fx.budget)
                Assert.That(db.TerrainBudget(row.code[0]), Is.EqualTo(row.max), row.code);
            Assert.That(db.TerrainBudget('P'), Is.EqualTo(0), "plains are unlimited, so they have no budget");
        }

        [Test] public void EveryFactionShipsALegalDefaultDeck()
        {
            foreach (var f in db.Factions)
            {
                var preset = DeckRules.Default(db, f.id);
                Assert.That(preset, Is.Not.Null, f.id);
                Assert.That(preset.faction, Is.EqualTo(f.id));
                Assert.That(DeckRules.TryNormalize(db, preset, out _, out var problem), Is.True, f.id + ": " + problem);
            }
            Assert.That(DeckRules.Default(db, "atlantis").faction, Is.EqualTo("egypt"), "an unknown faction falls back");
        }

        [Test] public void EveryBrowserGateAnswersTheSameWay()
        {
            Assert.That(fx.cases.Length, Is.GreaterThan(20));
            foreach (var c in fx.cases)
            {
                bool ok = DeckRules.TryNormalize(db, From(c.preset), out var preset, out var problem);
                Assert.That(ok, Is.EqualTo(c.ok), c.ok ? $"{c.name} was refused: {problem}" : $"{c.name} was accepted");
                if (!c.ok) { Assert.That(problem, Is.Not.Null.And.Not.Empty, c.name + " needs a reason"); continue; }
                Assert.That(problem, Is.Null, c.name);
                Assert.That(preset.faction, Is.EqualTo(c.normalized.faction), c.name);
                Assert.That(preset.loadout, Is.EqualTo(c.normalized.loadout), c.name + " hand");
                Assert.That(preset.heroes, Is.EqualTo(c.normalized.heroes), c.name + " heroes");
                Assert.That(preset.gods, Is.EqualTo(c.normalized.gods), c.name + " gods");
                Assert.That(preset.terrain, Is.EqualTo(c.normalized.terrain), c.name + " terrain");
                Assert.That(preset.buildings.Select(b => $"{b.type} {b.c0},{b.r0} {b.c1},{b.r1}").ToArray(),
                    Is.EqualTo(c.normalized.buildings.Select(b => $"{b.type} {b.c0},{b.r0} {b.c1},{b.r1}").ToArray()),
                    c.name + " buildings");
            }
        }

        [Test] public void ANormalizedPresetIsACopyTheCallerCannotReachInto()
        {
            var raw = DeckRules.Default(db, "china");
            Assert.That(DeckRules.TryNormalize(db, raw, out var preset, out _), Is.True);
            raw.loadout[0] = "changed";
            raw.buildings[0].c0 = 8;
            Assert.That(preset.loadout[0], Is.Not.EqualTo("changed"));
            Assert.That(preset.buildings[0].c0, Is.Not.EqualTo(8));
        }

        [Test] public void SlotsRoundTripThroughTheFileAndSurviveAMissingOne()
        {
            var store = new DeckStore(db, scratch);
            store.Load();
            Assert.That(store.Loaded, Is.False, "nothing on disk yet");
            Assert.That(store.Slot(0), Is.Null);
            Assert.That(store.Active(useDefault: true).faction, Is.EqualTo("egypt"), "a default still plays");
            Assert.That(store.Active(), Is.Null, "without one, an empty slot is empty");

            Assert.That(store.Store(0, DeckRules.Default(db, "greek")), Is.True);
            Assert.That(store.Store(1, DeckRules.Default(db, "aztec")), Is.True);
            Assert.That(store.ActiveSlot, Is.EqualTo(1));
            Assert.That(store.Save(), Is.True);

            var reopened = new DeckStore(db, scratch);
            reopened.Load();
            Assert.That(reopened.Loaded, Is.True);
            Assert.That(reopened.ActiveSlot, Is.EqualTo(1));
            Assert.That(reopened.Slot(0).faction, Is.EqualTo("greek"));
            Assert.That(reopened.Slot(1).faction, Is.EqualTo("aztec"));
            Assert.That(reopened.Slot(0).terrain, Is.EqualTo(DeckRules.Default(db, "greek").terrain));
            Assert.That(reopened.Slot(2), Is.Null, "there are two slots");
        }

        [Test] public void AnIllegalDeckIsRefusedAndNeverOverwritesAGoodOne()
        {
            var store = new DeckStore(db, scratch);
            Assert.That(store.Store(0, DeckRules.Default(db, "china")), Is.True);
            var broken = DeckRules.Default(db, "china");
            broken.gods = broken.gods.Take(3).ToArray();
            Assert.That(store.Store(0, broken, out var problem), Is.False);
            Assert.That(problem, Does.Contain("4"));
            Assert.That(store.Slot(0).gods.Length, Is.EqualTo(DeckRules.GodCount), "the good deck is still there");
            Assert.That(store.Store(5, DeckRules.Default(db, "china"), out var slotProblem), Is.False);
            Assert.That(slotProblem, Is.Not.Empty);
        }

        [Test] public void ACorruptOrOutdatedFileReadsBackEmptyRatherThanIllegal()
        {
            File.WriteAllText(scratch, "{ this is not json");
            var store = new DeckStore(db, scratch);
            store.Load();
            Assert.That(store.Slot(0), Is.Null);
            Assert.That(store.Slot(1), Is.Null);

            // A slot saved by a build whose rules were looser: it is dropped, not played.
            var good = new DeckStore(db, scratch);
            good.Store(0, DeckRules.Default(db, "egypt"));
            good.Save();
            var text = File.ReadAllText(scratch).Replace("\"faction\": \"egypt\"", "\"faction\": \"atlantis\"");
            File.WriteAllText(scratch, text);
            var after = new DeckStore(db, scratch);
            after.Load();
            Assert.That(after.Loaded, Is.True, "the file itself parsed");
            Assert.That(after.Slot(0), Is.Null, "but the deck in it did not survive the gate");
        }
    }
}
