using System.Linq;
using NUnit.Framework;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>U41: every god the game can field has a face, and a missing one degrades to text.</summary>
    public class GodPortraitTests
    {
        [Test] public void EveryGodInTheDatabaseHasAPortraitThatLoads()
        {
            var db = GameDataLoader.Load();
            using (var catalog = new UnitArtCatalog(PresentationData.Load()))
            {
                var missing = db.AllGods.Where(g => catalog.Portrait(g.key) == null).Select(g => g.key).ToList();
                Assert.That(missing, Is.Empty, "gods with no portrait: " + string.Join(", ", missing));
            }
        }

        [Test] public void ThePortraitTableCoversTheBrowsersFortyTwoAndNothingInvented()
        {
            var data = PresentationData.Load();
            Assert.That(data.godArt.Length, Is.EqualTo(42), "GOD_PORTRAITS has 42 entries");
            var db = GameDataLoader.Load();
            foreach (var entry in data.godArt)
            {
                Assert.That(entry.resource, Is.EqualTo("GodArt/" + entry.key));
                Assert.That(db.AllGods.Any(g => g.key == entry.key), Is.True,
                    entry.key + " is exported but is not a god in the database");
            }
        }

        [Test] public void AnUnknownGodAsksForAFaceAndGetsNullRatherThanAnException()
        {
            using (var catalog = new UnitArtCatalog(PresentationData.Load()))
                Assert.That(catalog.Portrait("nosuchgod"), Is.Null);
        }
    }
}
