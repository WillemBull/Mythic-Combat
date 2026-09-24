using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core.Data;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// The exported unit, god and faction tables, checked against the browser game.
    /// </summary>
    /// <remarks>
    /// Regenerate with <c>node tools/export_unity_data.js</c> from the repository root.
    /// The point of U5 is that these tables are never hand-transcribed, so these tests are
    /// about the export being COMPLETE and SOUND rather than about specific balance numbers,
    /// which are Willem's to change in the HTML.
    /// </remarks>
    public class GameDataTests
    {
        private static GameDatabase _db;

        [SetUp]
        public void LoadDatabase()
        {
            if (_db != null) return;
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        [Test]
        public void ExportLoadsAndIsNotEmpty()
        {
            Assert.IsNotNull(_db.Raw.generated, "the export should stamp how it was generated");
            Assert.AreEqual("godsbound_beta.html", _db.Raw.source);
            Assert.Greater(_db.AllUnits.Count, 0);
            Assert.Greater(_db.AllGods.Count, 0);
        }

        [Test]
        public void AllFourPantheonsArePresent()
        {
            var ids = _db.Factions.Select(f => f.id).ToList();
            CollectionAssert.AreEquivalent(new[] { "egypt", "china", "aztec", "greek" }, ids);
        }

        /// <summary>
        /// Referential integrity — the check that earns its keep. A typo in a default
        /// loadout is invisible until a match tries to deal a card that does not exist.
        /// </summary>
        [Test]
        public void ExportIsInternallyConsistent()
        {
            var problems = _db.Validate();
            Assert.IsEmpty(problems, "export inconsistencies:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// NOTHING may be silently dropped. Every unit field the browser tables define must
        /// be either a typed C# property or present in extras. A new field in the HTML fails
        /// here instead of vanishing into the gap between the two builds — which is the whole
        /// reason U5 exports rather than transcribes.
        /// </summary>
        [Test]
        public void EveryUnitFieldIsEitherTypedOrCarriedInExtras()
        {
            var typed = _db.Raw.typedUnitFields.ToHashSet();
            var carried = _db.AllUnits
                .SelectMany(u => u.extras ?? new System.Collections.Generic.List<ExtraValue>())
                .Select(e => e.key)
                .ToHashSet();

            var lost = _db.Raw.unitFieldInventory
                .Where(f => !typed.Contains(f) && !carried.Contains(f))
                .ToList();

            Assert.IsEmpty(lost,
                "unit fields present in the browser tables but reaching neither a typed " +
                "property nor extras: " + string.Join(", ", lost));
        }

        [Test]
        public void EveryGodFieldIsEitherTypedOrCarriedInExtras()
        {
            var typed = _db.Raw.typedGodFields.ToHashSet();
            var carried = _db.AllGods
                .SelectMany(g => g.extras ?? new System.Collections.Generic.List<ExtraValue>())
                .Select(e => e.key)
                .ToHashSet();

            var lost = _db.Raw.godFieldInventory
                .Where(f => !typed.Contains(f) && !carried.Contains(f))
                .ToList();

            Assert.IsEmpty(lost, "god fields lost in translation: " + string.Join(", ", lost));
        }

        /// <summary>
        /// One unit checked value by value against the browser table, as an anchor. If the
        /// export's field mapping ever shifts — cost flattening, the <c>as</c> rename — this
        /// catches it even when the counts still look right.
        /// </summary>
        [Test]
        public void EgyptianSpearmenMatchTheBrowserTableExactly()
        {
            var u = _db.Unit("egypt", "spear");
            Assert.IsNotNull(u, "egypt/spear should exist");
            Assert.AreEqual("Spearmen", u.name);
            Assert.AreEqual(20, u.costFood);
            Assert.AreEqual(0, u.costFavor);
            Assert.AreEqual("human", u.cat);
            Assert.AreEqual("shielded", u.armor);
            Assert.AreEqual("melee", u.dtype);
            Assert.AreEqual(120f, u.hp, 1e-4f);
            Assert.AreEqual(12f, u.dmg, 1e-4f);
            Assert.AreEqual(1f, u.attackSpeed, 1e-4f, "the browser table's 'as', renamed");
            Assert.AreEqual(1, u.range);
            Assert.AreEqual(0.516375f, u.speed, 1e-6f);
            Assert.AreEqual(1, u.size);
            Assert.AreEqual(1, u.train);
            Assert.IsTrue(u.hasPerSideScale, "spearmen use per-side sprite scales");
            Assert.AreEqual(0.8083f, u.spriteScaleSide0, 1e-4f);
            Assert.AreEqual(0.8683f, u.spriteScaleSide1, 1e-4f);
        }

        /// <summary>Costs come in three shapes: food only, favor only, and both.</summary>
        [Test]
        public void AllThreeCostShapesSurviveTheFlattening()
        {
            Assert.IsTrue(_db.AllUnits.Any(u => u.costFood > 0 && u.costFavor == 0), "food-only");
            Assert.IsTrue(_db.AllUnits.Any(u => u.costFood > 0 && u.costFavor > 0), "food and favor");
            Assert.IsTrue(_db.AllUnits.Any(u => u.costFood == 0 && u.costFavor > 0), "favor-only");
        }

        [Test]
        public void ResourceCapsAndMatchLengthMatchTheBrowserGame()
        {
            Assert.AreEqual(120, _db.FoodCap, "S.FOOD_CAP");
            Assert.AreEqual(250, _db.FavorCap, "S.FAVOR_CAP");
            Assert.AreEqual(180, _db.MatchSeconds, "a match is 180 seconds");
        }

        /// <summary>Each faction's deck slots are exactly 4 units, 2 heroes, 4 gods.</summary>
        [Test]
        public void EveryFactionHasACompleteDefaultDeck()
        {
            foreach (var f in _db.Factions)
            {
                Assert.AreEqual(4, f.defaultLoadout.Count, $"{f.id} default human units");
                Assert.AreEqual(2, f.defaultHeroes.Count, $"{f.id} default heroes");
                Assert.AreEqual(4, f.defaultGodPick.Count, $"{f.id} default gods");
                Assert.IsNotEmpty(f.unitKeys, $"{f.id} should have units");
                Assert.IsNotEmpty(f.allGodKeys, $"{f.id} should have gods");
            }
        }

        [Test]
        public void EveryGodsMythIsARealUnitOfThatFaction()
        {
            foreach (var g in _db.AllGods.Where(g => !string.IsNullOrEmpty(g.myth)))
                Assert.IsNotNull(_db.Unit(g.faction, g.myth),
                    $"{g.faction}/{g.key} names myth '{g.myth}'");
        }

        [Test]
        public void UnitKeysAreUniqueWithinAFaction()
        {
            foreach (var f in _db.Factions)
            {
                var keys = _db.UnitsOf(f.id).Select(u => u.key).ToList();
                CollectionAssert.AllItemsAreUnique(keys, $"{f.id} unit keys");
                Assert.AreEqual(f.unitKeys.Count, keys.Count, $"{f.id} unit count");
            }
        }

        /// <summary>The extras accessors must read the right type back out.</summary>
        [Test]
        public void ExtrasReadBackAsTheCorrectTypes()
        {
            // Bata's tree is inert and only its own side may target it — both booleans.
            var tree = _db.Unit("egypt", "bata_tree");
            if (tree != null)
            {
                Assert.IsTrue(tree.GetBool("inert"), "bata_tree should be inert");
                Assert.IsTrue(tree.GetBool("allyTargetOnly"));
                Assert.AreEqual("bata_reborn", tree.GetString("becomesOnDeath"));
            }

            var withSplash = _db.AllUnits.FirstOrDefault(u => u.Has("splash"));
            Assert.IsNotNull(withSplash, "at least one unit should define splash");
            Assert.Greater(withSplash.GetFloat("splash"), 0f, "splash should parse as a number");

            var missing = _db.Unit("egypt", "spear");
            Assert.IsFalse(missing.Has("nonexistentField"));
            Assert.AreEqual(0f, missing.GetFloat("nonexistentField"), 1e-6f);
            Assert.IsFalse(missing.GetBool("nonexistentField"));
            Assert.AreEqual("fallback", missing.GetString("nonexistentField", "fallback"));
        }

        /// <summary>
        /// Floats must parse the same regardless of the machine's locale. A comma-decimal
        /// locale would otherwise read 0.75 as 75.
        /// </summary>
        [Test]
        public void ExtrasParseUnderACommaDecimalLocale()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");

                var u = _db.AllUnits.First(x => x.Has("splash"));
                Assert.Less(u.GetFloat("splash"), 10f,
                    "a decimal must not be misread as a large integer under de-DE");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void MissingExportFailsLoudlyRatherThanReturningNull()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => GameDataLoader.Parse("{}"),
                "an export with no units must be rejected");
        }

        [Test]
        public void UnknownLookupsReturnNullRatherThanThrowing()
        {
            Assert.IsNull(_db.Faction("atlantis"));
            Assert.IsNull(_db.Unit("egypt", "nosuchunit"));
            Assert.IsNull(_db.God("egypt", "nosuchgod"));
        }

        /// <summary>Keys collide across factions, so lookups must be faction-scoped.</summary>
        [Test]
        public void SameKeyInTwoFactionsResolvesSeparately()
        {
            var shared = _db.AllUnits.GroupBy(u => u.key)
                                     .Where(g => g.Select(u => u.faction).Distinct().Count() > 1)
                                     .Select(g => g.Key)
                                     .FirstOrDefault();
            if (shared == null) Assert.Pass("no unit key is shared between factions");

            var factions = _db.AllUnits.Where(u => u.key == shared)
                                       .Select(u => u.faction).Distinct().ToList();
            var a = _db.Unit(factions[0], shared);
            var b = _db.Unit(factions[1], shared);
            Assert.AreEqual(factions[0], a.faction);
            Assert.AreEqual(factions[1], b.faction);
        }
    }
}
