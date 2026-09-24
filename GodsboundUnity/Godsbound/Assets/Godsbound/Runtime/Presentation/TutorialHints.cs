using System;

namespace Godsbound.Presentation
{
    /// <summary>
    /// The timed callout queue from roadmap 7.3/7.4: one hint on screen at a time, dismissed by a tap
    /// or after <see cref="ShowSeconds"/>, and only while the tutorial toggle is on.
    /// </summary>
    /// <remarks>
    /// <para>Nothing to port: 7.3 and 7.4 are open steps in the browser, so the roadmap's own wording
    /// is the specification — the five texts, their times and the 8s auto-dismiss are quoted from it,
    /// not measured from a running game.</para>
    /// <para>Pure and engine-free, so the timing can be tested without a scene. It reads a clock it is
    /// given rather than keeping its own, which means a paused match pauses the hints for free.</para>
    /// </remarks>
    public sealed class TutorialHints
    {
        /// <summary>Where a callout points: the card and god bar, or the board itself.</summary>
        public enum Anchor { Bar, Arena }

        public readonly struct Callout
        {
            public readonly float At;
            public readonly string Text;
            public readonly Anchor Where;
            public Callout(float at, string text, Anchor where) { At = at; Text = text; Where = where; }
        }

        /// <summary>A callout clears itself after this long unattended (7.3).</summary>
        public const float ShowSeconds = 8f;

        /// <summary>Roadmap 7.4, verbatim and in order.</summary>
        public static readonly Callout[] Script =
        {
            new Callout(0f,  "Drag a unit card through one of your buildings", Anchor.Bar),
            new Callout(8f,  "Draw a path — your unit will follow it",         Anchor.Arena),
            new Callout(25f, "Tap a god when you can afford them",             Anchor.Bar),
            new Callout(45f, "Tap an unlocked god, then tap the battlefield to cast", Anchor.Arena),
            new Callout(70f, "Tap your own unit in your half to make it hold", Anchor.Arena),
        };

        private int next;
        private int showing = -1;
        private float shownAt;

        /// <summary>Off means nothing is ever shown — 7.3's "none in non-tutorial mode".</summary>
        public bool Enabled { get; set; }

        public TutorialHints(bool enabled = true) { Enabled = enabled; }

        /// <summary>The callout to draw, or null.</summary>
        public Callout? Current => Enabled && showing >= 0 ? Script[showing] : (Callout?)null;

        /// <summary>Hints whose time has not come yet. Zero once the script has run out.</summary>
        public int Pending => Script.Length - next;

        /// <summary>
        /// Advance to the match clock. A hint that came due while another was showing is not lost and
        /// not stacked: it waits its turn, which is why 7.3 calls this a queue.
        /// </summary>
        public void Tick(float elapsed)
        {
            if (!Enabled) return;
            if (showing >= 0 && elapsed - shownAt >= ShowSeconds) showing = -1;
            if (showing < 0 && next < Script.Length && elapsed >= Script[next].At)
            {
                showing = next++;
                shownAt = elapsed;
            }
        }

        /// <summary>The tap. Does nothing when no callout is up.</summary>
        public void Dismiss() => showing = -1;

        /// <summary>Back to the top of the script, for a new match.</summary>
        public void Reset() { next = 0; showing = -1; shownAt = 0f; }
    }
}
