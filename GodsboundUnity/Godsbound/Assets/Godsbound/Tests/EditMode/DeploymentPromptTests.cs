using NUnit.Framework;
using UnityEngine;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>
    /// U40: the words a player is given while dragging a card are the browser's own, and the board
    /// lights up while a drag is looking for a building.
    /// </summary>
    public class DeploymentPromptTests
    {
        [Test] public void EveryPromptMatchesTheBrowserVerbatim()
        {
            // Quoted from godsbound_beta.html's pointerdown/pointermove/pointerup handlers.
            Assert.That(DeploymentPrompt.PickedUp("Spearman"),
                Is.EqualTo("Drag Spearman through your City, Fortress or Temple, then draw a route."));
            Assert.That(DeploymentPrompt.RouteNext, Is.EqualTo("Now draw the route. Release to deploy."));
            Assert.That(DeploymentPrompt.NoBuilding,
                Is.EqualTo("Route cancelled — start by dragging through one of your buildings."));
            Assert.That(DeploymentPrompt.NoRoute, Is.EqualTo("Draw at least one hex of route."));
            Assert.That(DeploymentPrompt.TooPoor, Is.EqualTo("Not enough resources."));
            Assert.That(DeploymentPrompt.Training("Spearman"), Is.EqualTo("Spearman training…"));
        }

        [Test] public void FailuresAreReportedInTheOrderTheyHappen()
        {
            // No building beats every other complaint: you never got started.
            Assert.That(DeploymentPrompt.ForFailure(false, false, false, false), Is.EqualTo(DeploymentPrompt.NoBuilding));
            Assert.That(DeploymentPrompt.ForFailure(true, false, false, false), Is.EqualTo(DeploymentPrompt.NoRoute));
            Assert.That(DeploymentPrompt.ForFailure(true, true, false, false), Is.EqualTo(DeploymentPrompt.BadHex));
            Assert.That(DeploymentPrompt.ForFailure(true, true, true, false), Is.EqualTo(DeploymentPrompt.TooPoor));
            Assert.That(DeploymentPrompt.ForFailure(true, true, true, true), Is.Null, "nothing went wrong");
        }

        [Test] public void TheDeployPulseStaysVisibleAndNeverGoesDark()
        {
            for (float t = 0f; t < 4f; t += 0.05f)
            {
                float p = BuildingsView.Pulse(t);
                Assert.That(p, Is.InRange(0.29f, 1.01f), "a hint, not a strobe");
            }
            // It does move, or it is not a pulse.
            Assert.That(BuildingsView.Pulse(0f), Is.Not.EqualTo(BuildingsView.Pulse(0.25f)).Within(0.05f));
        }

        [Test] public void NothingIsHighlightedUntilADragIsLookingForABuilding()
        {
            var root = new GameObject("DeployHighlightTest");
            try
            {
                var view = root.AddComponent<BuildingsView>();
                Assert.That(view.DeployHighlight, Is.EqualTo(-1), "no drag, no lights");
                var hud = root.AddComponent<BattleHud>();
                Assert.That(hud.WantsDeployTargets, Is.False, "no draft, nothing wanted");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
