using NUnit.Framework;
using Godsbound.Core.Setup;
using Godsbound.Data;
using Godsbound.Presentation;
using Godsbound.Presentation.Setup;

namespace Godsbound.Tests
{
    /// <summary>U42: the menus are screens with art, and only the terrain step shows the board.</summary>
    public class SetupScreenTests
    {
        [Test] public void OnlyTheTerrainStepKeepsTheBoard()
        {
            Assert.That(SetupHud.ShowsBoard(SetupStep.Terrain), Is.True, "building your half needs the board");
            foreach (SetupStep step in System.Enum.GetValues(typeof(SetupStep)))
                if (step != SetupStep.Terrain)
                    Assert.That(SetupHud.ShowsBoard(step), Is.False, step + " is a menu, not an arena overlay");
        }

        [Test] public void AMenuStepUsesTheWholeScreenNotTheBottomBand()
        {
            const float w = BoardViewport.DesignWidthPx, h = BoardViewport.DesignHeightPx;
            var sheet = SetupHud.Sheet(w, h);
            var band = SetupHud.Controls(w, h);
            Assert.That(sheet.height, Is.GreaterThan(band.height * 3f), "a menu is a page, not a strip");
            Assert.That(sheet.yMin, Is.GreaterThanOrEqualTo(SetupHud.Title(w, h).yMax), "it starts under the title");
            Assert.That(sheet.yMax, Is.LessThanOrEqualTo(h), "and ends on the screen");
        }

        [Test] public void EveryPantheonHasAFaceAndTheMenuHasItsBackdrop()
        {
            var db = GameDataLoader.Load();
            using (var catalog = new UnitArtCatalog(PresentationData.Load()))
            {
                Assert.That(catalog.Screen("menu"), Is.Not.Null, "the menu backdrop is exported");
                foreach (var faction in new[] { "egypt", "china", "greek", "aztec" })
                {
                    var key = SetupHud.FaceOf(db, faction);
                    Assert.That(key, Is.Not.Null, faction + " has no gods to take a face from");
                    Assert.That(catalog.Portrait(key), Is.Not.Null, faction + "'s face (" + key + ") has no portrait");
                }
                // Greece has no arena painting in the browser and must not pretend otherwise.
                Assert.That(catalog.Screen("arena_greek"), Is.Null);
                foreach (var f in new[] { "egypt", "china", "aztec" })
                    Assert.That(catalog.Screen("arena_" + f), Is.Not.Null, f + " has an arena backdrop");
            }
        }

        [Test] public void TheStepControlsAreThumbSized()
        {
            Assert.That(SetupHud.RowHeight, Is.GreaterThanOrEqualTo(BattleHud.MinTouch),
                "the setup buttons are tapped on a phone too (U37's rule)");
        }
    }
}
