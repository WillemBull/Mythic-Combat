using NUnit.Framework;
using UnityEngine;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>
    /// U37: every control a thumb has to hit is at least <see cref="BattleHud.MinTouch"/> in both
    /// directions at the phone frame, the bars clear the notch and the home indicator, and the app
    /// is locked to portrait.
    /// </summary>
    public class TouchLayoutTests
    {
        private const float W = BoardViewport.DesignWidthPx, H = BoardViewport.DesignHeightPx;

        [Test] public void EveryBarControlIsBigEnoughForAThumb()
        {
            var bottom = BattleHud.BottomBand(W, H);
            var layout = BattleHud.Layout(bottom);
            var bar = BattleHud.TopBar(BattleHud.TopBand(W, H, default));

            // Six cards share the card row; four god tiles share the god row.
            float cardWidth = layout.Cards.width / 6f, tileWidth = layout.Gods.width / 4f;
            Assert.That(cardWidth, Is.GreaterThanOrEqualTo(BattleHud.MinTouch), "card width");
            Assert.That(layout.Cards.height, Is.GreaterThanOrEqualTo(BattleHud.MinTouch), "card height");
            Assert.That(tileWidth, Is.GreaterThanOrEqualTo(BattleHud.MinTouch), "god tile width");
            Assert.That(layout.Gods.height, Is.GreaterThanOrEqualTo(BattleHud.MinTouch), "god tile height");
            foreach (var (name, r) in new[] { ("action", bar.Action), ("restart", bar.Restart) })
            {
                Assert.That(r.height, Is.GreaterThanOrEqualTo(BattleHud.MinTouch), name + " height");
                Assert.That(r.width, Is.GreaterThanOrEqualTo(BattleHud.MinTouch), name + " width");
            }
        }

        [Test] public void TheBarStillFitsInsideItsFixedBandAndKeepsTheBrowsersCardHeight()
        {
            var bottom = BattleHud.BottomBand(W, H);
            var layout = BattleHud.Layout(bottom);
            // The taller god row is paid for out of the hint and card rows, not by growing the band.
            Assert.That(layout.Gods.yMax, Is.LessThanOrEqualTo(bottom.yMax + 0.001f));
            Assert.That(layout.Cards.height, Is.GreaterThanOrEqualTo(68f), "browser .card{min-height:68px}");
            Assert.That(layout.Hint.yMax, Is.LessThanOrEqualTo(layout.Cards.yMin));
            Assert.That(layout.Cards.yMax, Is.LessThanOrEqualTo(layout.Gods.yMin));
        }

        [Test] public void TopBarButtonsSitBelowTheTextAndInsideTheBand()
        {
            var band = BattleHud.TopBand(W, H, default);
            var bar = BattleHud.TopBar(band);
            Assert.That(bar.Title.yMax, Is.LessThanOrEqualTo(bar.Action.yMin));
            Assert.That(bar.Action.yMax, Is.LessThanOrEqualTo(band.yMax + 0.001f));
            Assert.That(bar.Restart.xMax, Is.LessThanOrEqualTo(band.xMax + 0.001f));
            Assert.That(bar.Action.xMax, Is.LessThanOrEqualTo(bar.Status.xMin), "buttons and status do not overlap");
            Assert.That(bar.Status.xMax, Is.LessThanOrEqualTo(bar.Restart.xMin));
        }

        [Test] public void SafeAreaInsetsFlipFromBottomLeftPixelsToTopLeftDesignUnits()
        {
            // A 393x852 phone at 3x with a 47pt notch and a 34pt home indicator.
            const float scale = 3f;
            var safeArea = new Rect(0f, 34f * scale, 393f * scale, (852f - 47f - 34f) * scale);
            var insets = BattleHud.SafeInsets.From(safeArea, 393f * scale, 852f * scale, scale);
            Assert.That(insets.Top, Is.EqualTo(47f).Within(0.001), "the notch is at the TOP in GUI space");
            Assert.That(insets.Bottom, Is.EqualTo(34f).Within(0.001));
            Assert.That(insets.Left, Is.EqualTo(0f));
            Assert.That(insets.Right, Is.EqualTo(0f));

            // A device with nothing to avoid, and a device that reports nonsense, both get nothing.
            var full = BattleHud.SafeInsets.From(new Rect(0, 0, 1080, 1920), 1080, 1920, 1f);
            Assert.That(full.Top + full.Bottom + full.Left + full.Right, Is.EqualTo(0f));
            var broken = BattleHud.SafeInsets.From(new Rect(0, 0, 0, 0), 1080, 1920, 1f);
            Assert.That(broken.Top + broken.Bottom + broken.Left + broken.Right, Is.EqualTo(0f));
        }

        [Test] public void BarsMoveClearOfTheNotchWhileTheirBackdropsStillFillTheGlass()
        {
            var safe = new BattleHud.SafeInsets(47f, 34f, 0f, 0f);
            var top = BattleHud.TopBand(W, H, safe);
            var bottom = BattleHud.BottomBand(W, H, safe);
            var backdrop = BattleHud.BottomBackdrop(W, H, safe);
            Assert.That(top.y, Is.EqualTo(47f), "content starts below the notch");
            Assert.That(bottom.yMax, Is.EqualTo(H - 34f).Within(0.001), "content stops above the indicator");
            Assert.That(backdrop.yMax, Is.EqualTo(H).Within(0.001), "paint still reaches the bottom edge");
            Assert.That(backdrop.y, Is.EqualTo(bottom.y).Within(0.001));
            // The controls stay inside the moved band.
            var layout = BattleHud.Layout(bottom);
            Assert.That(layout.Gods.yMax, Is.LessThanOrEqualTo(bottom.yMax + 0.001f));
            // With no insets nothing moves, so the old two-argument call still means what it meant.
            Assert.That(BattleHud.BottomBand(W, H), Is.EqualTo(BattleHud.BottomBand(W, H, default)));
        }

        [Test] public void ThePlayerSettingsAreLockedToPortrait()
        {
            Assert.That(UnityEditor.PlayerSettings.defaultInterfaceOrientation,
                Is.EqualTo(UnityEditor.UIOrientation.Portrait), "a strategy board that rotates is a bug, not a feature");
            Assert.That(UnityEditor.PlayerSettings.allowedAutorotateToLandscapeLeft, Is.False);
            Assert.That(UnityEditor.PlayerSettings.allowedAutorotateToLandscapeRight, Is.False);
        }
    }
}
