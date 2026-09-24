using System.Collections.Generic;
using Godsbound.Core.Buildings;

namespace Godsbound.Core.Objectives
{
    /// <summary>The match result from the player's (side 0) point of view.</summary>
    public enum MatchOutcome
    {
        Defeat = -1,
        Draw = 0,
        Victory = 1
    }

    /// <summary>
    /// A settled match. Carries the numbers the end screen reports, so presentation never
    /// recomputes the comparison and cannot disagree with the core about who won.
    /// </summary>
    public readonly struct MatchResult
    {
        public readonly MatchOutcome Outcome;

        /// <summary>Enemy buildings the player destroyed — the browser's <c>pKills</c>.</summary>
        public readonly int PlayerBuildingsDestroyed;

        /// <summary>The player's own buildings that fell — the browser's <c>aKills</c>.</summary>
        public readonly int EnemyBuildingsDestroyed;

        /// <summary>Building damage DEALT by each side, indexed by the attacking side.</summary>
        public readonly float PlayerBuildingDamage;
        public readonly float EnemyBuildingDamage;

        /// <summary>Seconds elapsed when the match settled.</summary>
        public readonly float AtSeconds;

        /// <summary>True when a side lost every building before the clock ran out.</summary>
        public readonly bool Instant;

        public MatchResult(MatchOutcome outcome, int playerKills, int enemyKills,
                           float playerDamage, float enemyDamage, float atSeconds, bool instant)
        {
            Outcome = outcome;
            PlayerBuildingsDestroyed = playerKills;
            EnemyBuildingsDestroyed = enemyKills;
            PlayerBuildingDamage = playerDamage;
            EnemyBuildingDamage = enemyDamage;
            AtSeconds = atSeconds;
            Instant = instant;
        }

        public override string ToString() =>
            $"{Outcome} at {AtSeconds:0}s — buildings {PlayerBuildingsDestroyed}-{EnemyBuildingsDestroyed}, " +
            $"damage {PlayerBuildingDamage:0}-{EnemyBuildingDamage:0}{(Instant ? " (instant)" : "")}";
    }

    /// <summary>
    /// Win conditions and the match clock.
    /// </summary>
    /// <remarks>
    /// <para>Two ways a match ends, and they share one comparison — exactly as the browser game
    /// does it, where <c>checkInstantWin</c> calls the same <c>endMatch</c> that the clock does:
    /// buildings destroyed decides first, total building damage dealt is the tiebreaker, and an
    /// even split is a draw.</para>
    /// <para>Resolution is latched. The browser guards re-entry with <c>S.over</c>; here the
    /// stored result serves the same purpose, so a caller that keeps ticking after the end gets
    /// the same result rather than a recomputed one.</para>
    /// <para>Building damage is read from an accumulator the caller owns — in practice
    /// <c>CombatSystem.BuildingDamage</c>, which sums the amount each hit actually absorbed, so
    /// overkill past zero HP never inflates the tiebreaker.</para>
    /// </remarks>
    public sealed class MatchObjectives
    {
        private readonly BuildingMap _buildings;
        private readonly IReadOnlyList<float> _damageDealt;
        private MatchResult? _result;

        public MatchClock Clock { get; }

        /// <summary>The settled result, or null while the match is live.</summary>
        public MatchResult? Result => _result;

        public bool Resolved => _result.HasValue;

        /// <param name="damageDealt">
        /// Building damage dealt, indexed by attacking side. Held by reference, not copied, so a
        /// live accumulator keeps working as the match runs.
        /// </param>
        public MatchObjectives(BuildingMap buildings, IReadOnlyList<float> damageDealt, MatchClock clock)
        {
            _buildings = buildings ?? throw new System.ArgumentNullException(nameof(buildings));
            _damageDealt = damageDealt ?? throw new System.ArgumentNullException(nameof(damageDealt));
            Clock = clock ?? throw new System.ArgumentNullException(nameof(clock));
            if (_damageDealt.Count < 2)
                throw new System.ArgumentException("damage must be indexed by side (two entries)", nameof(damageDealt));
        }

        /// <summary>
        /// Advance the clock and settle the match if it is over. Returns the result on the tick
        /// that settles it and on every tick after, or null while play continues.
        /// </summary>
        public MatchResult? Tick(float dt)
        {
            if (_result.HasValue) return _result;
            Clock.Advance(dt);
            return Evaluate();
        }

        /// <summary>
        /// Settle the match if a win condition is met, without touching the clock. Call after
        /// damage is applied so a building destroyed this frame ends the match this frame.
        /// </summary>
        public MatchResult? Evaluate()
        {
            if (_result.HasValue) return _result;
            bool wipe = _buildings.AllDestroyed(0) || _buildings.AllDestroyed(1);
            if (wipe) return Settle(true);
            if (Clock.Expired) return Settle(false);
            return null;
        }

        /// <summary>
        /// Settle now regardless of the clock — a forfeit or an abandoned match. Idempotent.
        /// </summary>
        public MatchResult Resolve() => _result ?? Settle(false);

        private MatchResult Settle(bool instant)
        {
            int playerKills = DeadCount(1); // enemy buildings the player destroyed
            int enemyKills = DeadCount(0);
            float playerDamage = _damageDealt[0];
            float enemyDamage = _damageDealt[1];

            MatchOutcome outcome;
            if (playerKills != enemyKills)
                outcome = playerKills > enemyKills ? MatchOutcome.Victory : MatchOutcome.Defeat;
            else if (playerDamage != enemyDamage)
                outcome = playerDamage > enemyDamage ? MatchOutcome.Victory : MatchOutcome.Defeat;
            else
                outcome = MatchOutcome.Draw;

            var settled = new MatchResult(outcome, playerKills, enemyKills,
                                          playerDamage, enemyDamage, Clock.Elapsed, instant);
            _result = settled;
            return settled;
        }

        private int DeadCount(int side)
        {
            int n = 0;
            foreach (var b in _buildings.All)
                if (b.Side == side && b.Dead) n++;
            return n;
        }

        /// <summary>Clear the result and the clock for a fresh match. Buildings reset separately.</summary>
        public void Reset()
        {
            _result = null;
            Clock.Reset();
        }
    }
}
