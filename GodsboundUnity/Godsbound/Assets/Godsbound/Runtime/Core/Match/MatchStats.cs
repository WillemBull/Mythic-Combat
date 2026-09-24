namespace Godsbound.Core.Match
{
    /// <summary>
    /// What each side spent and lost, counted by the systems that own the events.
    /// </summary>
    /// <remarks>
    /// <para>Roadmap 7.1's four counters, indexed by side. They are incremented where the thing
    /// actually happens — a body appearing, a body dying, a power paid for, a unit paid for — rather
    /// than inferred afterwards, because anything inferred from the board misses what has already
    /// died or despawned.</para>
    /// <para>Counted for BOTH sides, so the end screen can put them next to each other.</para>
    /// </remarks>
    public sealed class MatchStats
    {
        /// <summary>Bodies that walked out of a building.</summary>
        public readonly int[] Trained = new int[2];

        /// <summary>Bodies that died. A conjured thing quietly vanishing is not a death.</summary>
        public readonly int[] Lost = new int[2];

        /// <summary>Favor spent on casting.</summary>
        public readonly float[] FavorOnPowers = new float[2];

        /// <summary>Favor spent on training — food is not counted; only the scarce currency is.</summary>
        public readonly float[] FavorOnUnits = new float[2];

        public void Reset()
        {
            for (int side = 0; side < 2; side++)
            {
                Trained[side] = Lost[side] = 0;
                FavorOnPowers[side] = FavorOnUnits[side] = 0f;
            }
        }

        public override string ToString() =>
            $"trained {Trained[0]}/{Trained[1]}, lost {Lost[0]}/{Lost[1]}, " +
            $"favor on powers {FavorOnPowers[0]:0}/{FavorOnPowers[1]:0}, on units {FavorOnUnits[0]:0}/{FavorOnUnits[1]:0}";
    }
}
