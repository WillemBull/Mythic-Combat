using Godsbound.Core.Data;

namespace Godsbound.Core.Objectives
{
    /// <summary>
    /// The match timer.
    /// </summary>
    /// <remarks>
    /// <para>Counts UP from zero, where the browser game's <c>S.t</c> counts DOWN from 180. That
    /// is a deliberate departure, not an accident of porting: every timing comparison in the
    /// browser has to convert through <c>elapsed()</c> (<c>= 180 - S.t</c>) to be correct, which
    /// is why that project carries a standing rule that <c>S.t</c> must never be compared
    /// directly. Counting up removes the trap rather than importing it.</para>
    /// <para>The reported clock is unchanged. The browser's own match reporting already prints
    /// <c>elapsed()</c>, so this counter IS the reported number, and <see cref="Remaining"/>
    /// reproduces <c>S.t</c> for anything that wants the countdown for display.</para>
    /// <para>The duration comes from <see cref="GameDatabase.MatchSeconds"/>, exported from the
    /// browser game — never a literal here.</para>
    /// </remarks>
    public sealed class MatchClock
    {
        /// <summary>Match length in seconds, from the export.</summary>
        public float Duration { get; }

        /// <summary>Seconds since the match began, clamped to <see cref="Duration"/>.</summary>
        public float Elapsed { get; private set; }

        /// <summary>Seconds left — the browser's <c>S.t</c>, derived rather than stored.</summary>
        public float Remaining
        {
            get
            {
                float left = Duration - Elapsed;
                return left < 0f ? 0f : left;
            }
        }

        /// <summary>True once the full duration has run.</summary>
        public bool Expired => Elapsed >= Duration;

        public MatchClock(float duration)
        {
            if (duration <= 0f)
                throw new System.ArgumentOutOfRangeException(
                    nameof(duration), duration, "a match needs a positive duration");
            Duration = duration;
        }

        /// <summary>Build from the exported match length.</summary>
        public static MatchClock From(GameDatabase database)
        {
            if (database == null) throw new System.ArgumentNullException(nameof(database));
            return new MatchClock(database.MatchSeconds);
        }

        /// <summary>
        /// Advance the clock. Returns the time actually applied, which is less than
        /// <paramref name="dt"/> on the tick that runs out the match — so a caller stepping a
        /// fixed timestep cannot accumulate past the end.
        /// </summary>
        public float Advance(float dt)
        {
            if (dt < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(dt), dt, "time cannot run backwards");
            float room = Duration - Elapsed;
            float applied = dt > room ? room : dt;
            Elapsed += applied;
            return applied;
        }

        /// <summary>Back to the start of a match.</summary>
        public void Reset() => Elapsed = 0f;

        public override string ToString() => $"{Elapsed:0.0}s / {Duration:0}s";
    }
}
