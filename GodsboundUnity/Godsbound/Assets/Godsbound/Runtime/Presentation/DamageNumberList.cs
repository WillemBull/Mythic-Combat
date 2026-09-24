using System;
using System.Collections.Generic;

namespace Godsbound.Presentation
{
    /// <summary>
    /// The live floating damage numbers, in board space. Pure: no engine types, so the cap and the
    /// fade are testable without a scene. Ports the browser's <c>"text"</c> effect (roadmap 6.5).
    /// </summary>
    public sealed class DamageNumberList
    {
        /// <summary>Willem's 2026-07-29 ruling: every hit, no damage floor, but never more than 20
        /// on screen at once. At the cap the NEW number is dropped; an existing one is never evicted,
        /// so a number never vanishes mid-rise.</summary>
        public const int Cap = 20;

        /// <summary>From the browser's <c>fx()</c>: a "text" effect lives 1.2s.</summary>
        public const float Life = 1.2f;

        /// <summary>
        /// The browser lifts the text by <c>p*20</c> screen pixels against a board hex of 24px, and
        /// draws it at <c>HEX*0.5</c>. Both are expressed here in hex radii so they hold at any zoom.
        /// </summary>
        public const float Rise = 20f / 24f;
        public const float Size = 0.5f;

        public struct Entry
        {
            public float X, Y;
            public float Amount;
            /// <summary>The side that TOOK the damage, so the number can carry its colour.</summary>
            public int Side;
            public float At;
        }

        private readonly List<Entry> live = new List<Entry>();
        public IReadOnlyList<Entry> Live => live;
        public int Count => live.Count;

        /// <summary>Returns false when the cap dropped this number.</summary>
        public bool Add(float x, float y, float amount, int side, float at)
        {
            if (live.Count >= Cap) return false;
            live.Add(new Entry { X = x, Y = y, Amount = amount, Side = side, At = at });
            return true;
        }

        /// <summary>Drops numbers whose life has run out. Cheap to call every frame.</summary>
        public void Expire(float elapsed)
        {
            for (int i = live.Count - 1; i >= 0; i--)
                if (elapsed - live[i].At >= Life) live.RemoveAt(i);
        }

        public void Clear() => live.Clear();

        /// <summary>0 at the hit, 1 when the number is gone.</summary>
        public static float Progress(Entry entry, float elapsed) =>
            Math.Min(1f, Math.Max(0f, (elapsed - entry.At) / Life));

        /// <summary>
        /// The browser's <c>"-"+Math.round(dmg)</c>. A hit small enough to round to zero still shows
        /// "-1": the roadmap's no-threshold rule means every hit must be visible, and "-0" reads as
        /// a miss.
        /// </summary>
        public static string Text(float amount) =>
            "-" + Math.Max(1, (int)Math.Round(Math.Abs(amount), MidpointRounding.AwayFromZero));
    }
}
