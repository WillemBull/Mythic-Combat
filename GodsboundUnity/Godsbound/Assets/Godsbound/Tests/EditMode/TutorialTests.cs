using NUnit.Framework;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>U36: the timed hint queue from roadmap 7.3, carrying 7.4's five hints.</summary>
    public class TutorialTests
    {
        [Test] public void HintsFireAtTheirTimesInOrder()
        {
            var hints = new TutorialHints();
            Assert.That(hints.Current, Is.Null, "nothing before the first tick");
            hints.Tick(0f);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[0].Text));
            // Still the first one a moment later: a hint is not replaced mid-read.
            hints.Tick(5f);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[0].Text));
            for (int i = 1; i < TutorialHints.Script.Length; i++)
            {
                hints.Tick(TutorialHints.Script[i].At);
                Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[i].Text),
                    "hint " + i + " is due at " + TutorialHints.Script[i].At + "s");
            }
            Assert.That(hints.Pending, Is.Zero);
            hints.Tick(999f);
            Assert.That(hints.Current, Is.Null, "the script runs out rather than repeating");
        }

        [Test] public void ACalloutClearsItselfAfterEightSeconds()
        {
            var hints = new TutorialHints();
            hints.Tick(0f);
            hints.Tick(TutorialHints.ShowSeconds - 0.01f);
            Assert.That(hints.Current, Is.Not.Null);
            // At exactly 8s the first expires and the second is due: one goes, one arrives.
            hints.Tick(TutorialHints.ShowSeconds);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[1].Text));
            hints.Tick(TutorialHints.ShowSeconds * 2f);
            Assert.That(hints.Current, Is.Null, "nothing is due yet, and the second has expired");
        }

        [Test] public void ATapDismissesAndTheQueueCarriesOn()
        {
            var hints = new TutorialHints();
            hints.Tick(0f);
            hints.Dismiss();
            Assert.That(hints.Current, Is.Null);
            hints.Dismiss();
            Assert.That(hints.Current, Is.Null, "dismissing nothing is harmless");
            hints.Tick(8f);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[1].Text));
        }

        [Test] public void AHintDueWhileAnotherShowsWaitsItsTurn()
        {
            var hints = new TutorialHints();
            hints.Tick(24f);                    // the first hint, late: it is still the one owed
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[0].Text));
            hints.Tick(25f);                    // hints 2 and 3 are both due now
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[0].Text),
                "the one on screen is not shoved aside");
            hints.Dismiss(); hints.Tick(25f);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[1].Text),
                "the queue advances one at a time, in order, nothing skipped");
        }

        [Test] public void NothingShowsWhenTheToggleIsOff()
        {
            var hints = new TutorialHints(enabled: false);
            for (float t = 0f; t <= 80f; t += 1f) hints.Tick(t);
            Assert.That(hints.Current, Is.Null);
            Assert.That(hints.Pending, Is.EqualTo(TutorialHints.Script.Length), "the script was not consumed");
            // Turning it on mid-match starts from the top rather than dumping the backlog.
            hints.Enabled = true;
            hints.Tick(81f);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[0].Text));
        }

        [Test] public void ResetPutsTheScriptBackForANewMatch()
        {
            var hints = new TutorialHints();
            hints.Tick(0f); hints.Tick(8f); hints.Tick(25f);
            Assert.That(hints.Pending, Is.LessThan(TutorialHints.Script.Length));
            hints.Reset();
            Assert.That(hints.Current, Is.Null);
            Assert.That(hints.Pending, Is.EqualTo(TutorialHints.Script.Length));
            hints.Tick(0f);
            Assert.That(hints.Current.Value.Text, Is.EqualTo(TutorialHints.Script[0].Text));
        }

        [Test] public void TheFiveHintsAreTheOnesTheRoadmapAsksFor()
        {
            Assert.That(TutorialHints.Script.Length, Is.EqualTo(5));
            var times = new float[] { 0f, 8f, 25f, 45f, 70f };
            for (int i = 0; i < 5; i++) Assert.That(TutorialHints.Script[i].At, Is.EqualTo(times[i]));
            Assert.That(TutorialHints.Script[0].Text, Is.EqualTo("Drag a unit card through one of your buildings"));
            Assert.That(TutorialHints.Script[4].Text, Is.EqualTo("Tap your own unit in your half to make it hold"));
        }
    }
}
