using System;
using System.Linq;
using System.Collections.Generic;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;

namespace Godsbound.Core.Match
{
    /// <summary>Visibility inputs; power systems supply these without owning target selection.</summary>
    public sealed class UnitUpdateContext
    {
        public Func<int, string> FactionForSide = side => "";
        public Func<int, string, bool> HasPassive = (side, key) => false;
        public Func<Unit, bool> InGrove = unit => false;
        /// <summary>Artemis's <c>isQuarry(u)</c>: exposure that outranks every concealment route.</summary>
        public Func<Unit, float, bool> IsQuarry = (unit, elapsed) => false;
        public float VeilUntil { get; set; }
        // Like the browser, indexed by the side prevented from hiding, not the observer.
        public float[] RevealedUntil { get; } = new float[2];
        public void ResetTimers() { VeilUntil = 0f; Array.Clear(RevealedUntil, 0, RevealedUntil.Length); }
    }

    /// <summary>U15's ordinary targeting and attack scheduler; movement remains in RouteMovement.</summary>
    public sealed class UnitUpdate : IUnitUpdate
    {
        private readonly Random _random;
        // Enemies currently engaged by the scanning unit's allies, rebuilt once per scan. Reused so
        // the scan allocates nothing; the loop is single-threaded and never re-enters.
        private readonly HashSet<Unit> _engagedByAllies = new HashSet<Unit>();
        public UnitUpdate(Random random = null) { _random = random ?? new Random(); }

        public bool LineOfSightBlocked(MatchState state, Hex from, Hex to)
        {
            if (Hex.Distance(from, to) <= 1) return false;
            var middles = from.Neighbors().Where(h => Hex.Distance(h, to) == 1).ToArray();
            return middles.Length > 0 && middles.All(h => TerrainTable.Info(state.Terrain[h]).Block);
        }

        public bool HiddenFrom(MatchState state, Unit target, Unit observer, float elapsed)
            => HiddenFrom(state, target, observer.Side, observer, elapsed);

        public bool HiddenFromSide(MatchState state, Unit target, int observerSide, float elapsed)
            => HiddenFrom(state, target, observerSide, null, elapsed);

        private bool HiddenFrom(MatchState state, Unit target, int observerSide, Unit observer, float elapsed)
        {
            if (target.Def.GetBool("allyTargetOnly") && target.Side != observerSide) return true;
            var context = state.UnitContext;
            if (context.HasPassive(observerSide, "trueSight") || elapsed < context.RevealedUntil[target.Side]) return false;
            // Artemis: being marked as prey is EXPOSURE, so it outranks the veil, invisibility and
            // bamboo alike — a quarry standing in cover must still be visible to its own hounds.
            if (context.IsQuarry(target, elapsed)) return false;
            if (elapsed < context.VeilUntil && Board.SideForRow(target.Hex.R) == BoardSide.Neutral) return true;
            if (target.Combat.Invisible) return true;
            bool bamboo = context.InGrove(target) ||
                (context.FactionForSide(target.Side) == "china" && state.Terrain[target.Hex] == TerrainType.Forest &&
                 (int)Board.SideForRow(target.Hex.R) == target.Side);
            if (!bamboo) return false;
            return observer == null || target.Engagement?.Unit != observer;
        }

        public Unit ScanEncounter(MatchState state, Unit unit, float elapsed)
        {
            if (unit.Def.GetBool("targetsBuildings")) return null;
            int range = Math.Max(unit.Def.range, 1);
            // Sekhmet's Eye Unleashed: a frenzy flattens the side distinction and drops the
            // ally/counter bonuses, so a frenzied unit simply takes the nearest body in reach.
            bool frenzied = elapsed < unit.Combat.FrenzyUntil;
            var context = state.UnitContext;
            float quarryBonus = state.Database.Powers.Value("quarryScore");
            Unit best = null, ally = null;
            float bestScore = float.NegativeInfinity, allyScore = float.NegativeInfinity;
            var rates = state.Database.Combat;
            float scanDistance = rates.Value("scanDistance"), scanBlocker = rates.Value("scanBlocker"),
                  scanAlly = rates.Value("scanAlly"), scanCounter = rates.Value("scanCounter");
            // One pass over the allies instead of one per candidate: the ally bonus asks "is some
            // other friendly unit already fighting this enemy?", which is a set membership test.
            _engagedByAllies.Clear();
            foreach (var friend in state.Units.All)
                if (!friend.Dead && friend != unit && friend.Side == unit.Side && friend.Engagement?.Unit != null)
                    _engagedByAllies.Add(friend.Engagement.Unit);
            foreach (var enemy in state.Units.All)
            {
                if (enemy.Dead || enemy == unit) continue;
                bool sameSide = enemy.Side == unit.Side;
                bool quarry = context.IsQuarry(enemy, elapsed);
                if (sameSide && !frenzied && !enemy.Def.GetBool("allyTargetOnly") && !quarry) continue;
                if (HiddenFrom(state, enemy, unit, elapsed)) continue;
                int distance = Hex.Distance(unit.Hex, enemy.Hex);
                if (distance > range || (unit.Def.dtype == "ranged" && LineOfSightBlocked(state, unit.Hex, enemy.Hex))) continue;
                float score = -distance * scanDistance;
                if (unit.HasRoute && Hex.Distance(enemy.Hex, unit.Route[unit.RouteIndex]) <= 1) score += scanBlocker;
                if (!frenzied && _engagedByAllies.Contains(enemy)) score += scanAlly;
                if (!frenzied && CombatRates.Matches(rates.categoryBeats, unit.Def.cat, enemy.Def.cat)) score += scanCounter;
                // Big enough to dominate every other term: everything that CAN reach the quarry takes it.
                if (quarry) score += quarryBonus;
                // The ally bucket only wins when no real enemy qualifies at all. A quarry belongs in the
                // REAL bucket — otherwise Actaeon's hounds would keep fighting you while it stood there.
                if (sameSide && !frenzied && !quarry) { if (score > allyScore) { allyScore = score; ally = enemy; } }
                else if (score > bestScore) { bestScore = score; best = enemy; }
            }
            return best ?? ally;
        }

        /// <summary>
        /// The closest enemy UNIT anywhere on the board, for Hou Yi's Sun-Shooter. Deliberately
        /// unlike <see cref="ScanEncounter"/>: no range limit and no line-of-sight check — a
        /// sun-arrow crossing the whole board is not stopped by a mountain — but concealment is
        /// still respected. Ties resolve to list order, so the pick is deterministic.
        /// </summary>
        public Unit SnipeTarget(MatchState state, Unit unit, float elapsed)
        {
            Unit best = null;
            int nearest = int.MaxValue;
            foreach (var e in state.Units.All)
            {
                if (e.Dead || e.Side == unit.Side || HiddenFrom(state, e, unit, elapsed)) continue;
                int d = Hex.Distance(unit.Hex, e.Hex);
                if (d < nearest) { nearest = d; best = e; }
            }
            return best;
        }

        /// <summary>Sekhmet's Plague Bearer: one swing lands on up to N enemies in range at once.</summary>
        private void FanOut(MatchState state, Unit unit, int multi, float elapsed)
        {
            int range = Math.Max(unit.Def.range, 1);
            var targets = state.Units.All
                .Where(e => !e.Dead && e.Side != unit.Side && !HiddenFrom(state, e, unit, elapsed) &&
                            Hex.Distance(unit.Hex, e.Hex) <= range &&
                            !(unit.Def.dtype == "ranged" && LineOfSightBlocked(state, unit.Hex, e.Hex)))
                .OrderBy(e => Hex.Distance(unit.Hex, e.Hex))
                .Select(e => new MovementContact(e)).ToList();
            var primary = unit.Engagement;
            // The unit it was already fighting is always struck, even a building it was besieging.
            if (primary.IsBuilding || targets.All(x => x.Unit != primary.Unit)) targets.Insert(0, primary);
            foreach (var x in targets.Take(multi)) state.Combat.DealDamage(unit, x, elapsed);
        }

        private void March(MatchState state, Unit unit)
        {
            var previous = unit.MarchGoal;
            if (unit.LockedGoal != null && unit.LockedGoal.Dead) unit.LockedGoal = null;
            Building destination = unit.LockedGoal ?? previous;
            if (destination != null && (destination.Dead || destination.Side == unit.Side)) destination = null;
            List<Hex> path = null;
            if (destination == null)
            {
                int length = int.MaxValue;
                // Browser marchRouteFrom: shortest actual route, stable building order for ties.
                foreach (var b in state.Buildings.All)
                {
                    if (b.Dead || b.Side == unit.Side) continue;
                    var candidate = state.Paths.FindPath(unit.Hex,
                        state.Buildings.AdjacentRoutableHexes(b, unit.Flying, state.Terrain), unit.Flying);
                    if (candidate != null && candidate.Count < length)
                    { destination = b; path = candidate; length = candidate.Count; }
                }
                // Preserve the browser's unreachable-building fallback and commit once.
                if (destination == null)
                    destination = state.Buildings.All.Where(b => !b.Dead && b.Side != unit.Side)
                        .OrderBy(b => b.Hexes().Min(h => Hex.Distance(unit.Hex, h))).FirstOrDefault();
            }
            if (destination != previous)
            {
                unit.MarchGoal = destination;
                if (destination != null && path == null)
                    path = state.Paths.FindPath(unit.Hex,
                        state.Buildings.AdjacentRoutableHexes(destination, unit.Flying, state.Terrain), unit.Flying);
                unit.SetRoute(path != null && path.Count > 1 ? path.Skip(1) : null);
                unit.Free = true; // automatic march, rather than the player's drawn-route lock
            }
            // Willem 2026-09-11: never rebuild a missing/exhausted route to the SAME destination.
            if (unit.MarchGoal != null) unit.Engagement = new MovementContact(unit.MarchGoal);
        }

        public void Update(MatchState state, Unit unit, float dt, float elapsed)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt)) throw new ArgumentOutOfRangeException(nameof(dt));
            if (unit.Dead || dt == 0f) return;
            // Bata's persea tree and Nezha's lotus: an inert body never moves, targets or attacks.
            // It only stands and can be chopped; its lifecycle belongs to FormChains.
            if (unit.Def.GetBool("inert")) return;
            if (float.IsNaN(unit.AttackTimer)) unit.AttackTimer = (float)_random.NextDouble() * state.Database.Combat.Value("initialAttackSpread");
            unit.AttackTimer -= dt;
            // Theseus's Clew: a retreating unit walks home and does nothing else. Without the early
            // return it would stop at the first enemy, never finish the route, and fight on while
            // untouchable — the ward is topped up (in GodEffects) only for as long as it is walking.
            if (unit.Combat.Retreating)
            {
                unit.Engagement = null;
                state.Movement.FollowPath(unit, dt, elapsed);
                return;
            }
            bool stunned = elapsed < unit.StunUntil;
            // Hou Yi's Sun-Shooter: every few seconds he stops dead and shoots the nearest enemy
            // anywhere on the board. The windup is the whole cost, and it is never interrupted once
            // begun. Suppressed while something is IN RANGE, so he is never duelling and sniping at
            // once — deliberately not a bare "has no target", since a marching unit always has one.
            float snipeEvery = unit.Def.GetFloat("snipeEvery");
            if (snipeEvery != 0f && !stunned)
            {
                if (unit.Sniping)
                {
                    if (elapsed >= unit.SnipeFireAt)
                    {
                        var shot = SnipeTarget(state, unit, elapsed);
                        if (shot != null) state.Combat.DealDamage(unit, shot, elapsed);
                        unit.Sniping = false;
                        unit.SnipeFireAt = 0f;
                        unit.SnipeTimer = snipeEvery;
                    }
                    return; // full stillness: no movement and no ordinary attack, for the whole windup
                }
                unit.SnipeTimer -= dt;
                bool engaged = unit.Engagement != null &&
                               unit.Engagement.Distance(unit.Hex) <= Math.Max(unit.Def.range, 1);
                if (unit.SnipeTimer <= 0f && !engaged && SnipeTarget(state, unit, elapsed) != null)
                {
                    unit.Sniping = true;
                    unit.SnipeFireAt = elapsed + unit.Def.GetFloat("snipeWindup");
                    return;
                }
            }
            state.Ai?.PrepareUnit(unit);
            var target = unit.Engagement;
            if (target != null)
            {
                if (target.Dead) unit.Engagement = null;
                else if (!target.IsBuilding)
                {
                    int range = Math.Max(unit.Def.range, 1), distance = target.Distance(unit.Hex);
                    bool blocker = unit.HasRoute && target.Distance(unit.Route[unit.RouteIndex]) <= 1;
                    if (distance > range + 1 || (distance > range && !blocker && !unit.Free) ||
                        (unit.Def.dtype == "ranged" && LineOfSightBlocked(state, unit.Hex, target.Unit.Hex)) ||
                        HiddenFrom(state, target.Unit, unit, elapsed)) unit.Engagement = null;
                }
            }
            if (!stunned && (unit.Engagement == null || unit.Engagement.IsBuilding))
            {
                var enemy = ScanEncounter(state, unit, elapsed);
                if (enemy != null) unit.Engagement = new MovementContact(enemy);
                else if (unit.Engagement == null && unit.Free && !unit.Hold) March(state, unit);
            }
            // A healer with nothing to fight mends the most hurt ally beside it, on its own cadence.
            // A MARCHING healer is excluded by the same condition the browser uses: its goal building
            // is its target, so only a held or blocked healer ever reaches this.
            float heal = unit.Def.GetFloat("heal");
            if (heal != 0f && unit.Engagement == null)
            {
                unit.HealTimer -= dt;
                if (unit.HealTimer <= 0f)
                {
                    Unit hurt = null;
                    float worst = float.PositiveInfinity;
                    foreach (var friend in state.Units.All)
                    {
                        if (friend.Dead || friend == unit || friend.Side != unit.Side) continue;
                        if (friend.Hp >= friend.MaxHp || Hex.Distance(unit.Hex, friend.Hex) > 1) continue;
                        float ratio = friend.Hp / friend.MaxHp;
                        if (ratio < worst) { worst = ratio; hurt = friend; }
                    }
                    if (hurt != null)
                    {
                        hurt.Heal(heal);
                        unit.HealTimer = state.Database.Powers.Value("healInterval");
                    }
                }
            }
            bool ready = state.Movement.Tick(unit, dt, elapsed);
            if (ready && unit.Engagement != null && !stunned && unit.AttackTimer <= 0f)
            {
                unit.AttackTimer = 1f / unit.Def.attackSpeed;
                int multi = (int)unit.Def.GetFloat("multiTarget");
                if (multi > 0) FanOut(state, unit, multi, elapsed);
                else state.Combat.DealDamage(unit, unit.Engagement, elapsed);
            }
        }
    }
}
