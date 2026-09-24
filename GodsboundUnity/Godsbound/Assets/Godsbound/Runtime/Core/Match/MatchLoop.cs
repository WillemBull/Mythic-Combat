using System;
using Godsbound.Core.Economy;
using Godsbound.Core.Objectives;
using Godsbound.Core.Units;

namespace Godsbound.Core.Match
{
    /// <summary>
    /// What happens to one unit each frame. UnitUpdate supplies the default implementation — target
    /// selection and attack scheduling, the half of the browser's <c>updateUnit</c> that U12
    /// deliberately left out.
    /// </summary>
    public interface IUnitUpdate
    {
        void Update(MatchState state, Unit unit, float dt, float elapsed);
    }

    /// <summary>
    /// U14's movement-only adapter, retained for callers explicitly isolating movement.
    /// </summary>
    /// <remarks>
    /// Units march and collide but never acquire a target or throw a punch, so a U14-era match
    /// runs to time without a single shot fired. That is the honest state of the port, not a
    /// placeholder pretending to be combat.
    /// </remarks>
    public sealed class MovementOnlyUnitUpdate : IUnitUpdate
    {
        public void Update(MatchState state, Unit unit, float dt, float elapsed) =>
            state.Movement.Tick(unit, dt, elapsed);
    }

    /// <summary>
    /// One frame of a match, in the browser's own order.
    /// </summary>
    /// <remarks>
    /// <para>Ported from <c>loop()</c> in <c>godsbound_beta.html</c>. Order is not arbitrary —
    /// training must land before units update so a unit spawned this frame can move on it, and
    /// objectives must be evaluated after damage so a building destroyed this frame ends the
    /// match this frame rather than a frame late.</para>
    /// <para>AI god progression ticks inside <c>AiController.Tick</c> (U24); <c>tickGodEffects</c>
    /// runs as <see cref="Gods.GodEffects"/> after training (U26, with U27's floods and warning and
    /// U28's hero sweep), and <c>tickFormChains</c> after the unit pass (U28).
    /// <b>Not ticked yet, each because its own system is unported:</b> <c>tickPowerFx</c>
    /// (presentation, and driven by MatchController instead), ward expiry and <c>campaign</c>.
    /// These are chosen omissions, not oversights.</para>
    /// <para><c>retainUnitAfterUpdate</c> is deliberately NOT ported. It holds a dead spearman in
    /// the unit list purely to finish a death animation — presentation, not simulation. Core drops
    /// dead units immediately; a corpse list belongs with the sprites.</para>
    /// </remarks>
    public sealed class MatchLoop
    {
        /// <summary>
        /// The longest frame the simulation will accept, from <c>loop()</c>'s own
        /// <c>Math.min(0.05, …)</c>.
        /// </summary>
        /// <remarks>
        /// Load-bearing, not cosmetic. A long frame — a breakpoint, a stalled tab, a slow first
        /// frame after a scene load — would otherwise advance a unit far enough in one step to
        /// cross a blocked hex without ever being tested against it.
        /// </remarks>
        public const float MaxStep = 0.05f;

        private readonly IUnitUpdate _unitUpdate;

        public MatchLoop(IUnitUpdate unitUpdate = null)
        {
            _unitUpdate = unitUpdate ?? new UnitUpdate();
        }

        /// <summary>
        /// Advance one frame. Returns the result on the frame the match settles and on every
        /// frame after, or null while play continues.
        /// </summary>
        public MatchResult? Tick(MatchState state, float dt)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "a frame needs a finite, non-negative length");

            // Setup phase or an already-settled match: no simulation at all, exactly as the
            // browser skips its whole tick block on `!S.over && S.phase==="battle"`.
            if (!state.Live) return state.Objectives.Result;

            float step = dt > MaxStep ? MaxStep : dt;

            state.Objectives.Clock.Advance(step);
            float elapsed = state.Objectives.Clock.Elapsed;

            for (int side = 0; side < 2; side++)
                state.Resources[side] = EconomyTick.Advance(
                    state.Resources[side], (EconomySide)side,
                    state.ConditionsFor(side), state.Database.Economy, step);

            // TrainingQueue.Tick spawns THROUGH UnitField.Spawn, which already adds to the field.
            // Its return value is the list of what appeared this frame, for callers that want to
            // react to it — re-adding it here would double every trained unit.
            var spawned = state.Training.Tick(elapsed, state.Units, state.Buildings);
            state.GodEffects.Tick(step, elapsed); // browser order: after training, before fortresses
            state.Ai?.RegisterSpawns(spawned);
            state.Fortresses.Tick(step, elapsed);
            state.Ai?.Tick(step);

            // Reverse iteration over the live list, not a copy: a unit update may append (a spawn
            // on death, later) and reverse order keeps an append beyond the current index. Nothing
            // is removed mid-pass — dead units are swept below, after every unit has acted.
            var acting = state.Units.All;
            for (int i = acting.Count - 1; i >= 0; i--)
            {
                var u = acting[i];
                if (!u.Dead) _unitUpdate.Update(state, u, step, elapsed);
            }

            // Form chains resolve AFTER every unit has acted and BEFORE the dead are swept: a body
            // that fell this frame must still be here for its successor to be spawned from.
            state.FormChains.Tick(elapsed);

            state.Units.RemoveDead();

            return state.Objectives.Evaluate();
        }

        /// <summary>
        /// The most sub-steps <see cref="Advance"/> will run for one real frame. Beyond
        /// <c>DefaultMaxSubsteps * MaxStep</c> (0.2s) of wall time the remainder is dropped, so a
        /// multi-second stall (a breakpoint, the app returning from the background) cannot start a
        /// spiral where every frame has more simulation to catch up on than the last.
        /// </summary>
        public const int DefaultMaxSubsteps = 4;

        /// <summary>
        /// Advance by one real frame of <paramref name="dt"/>, as several <see cref="Tick"/>s of at
        /// most <see cref="MaxStep"/> each.
        /// </summary>
        /// <remarks>
        /// <para>A deliberate departure from the browser, which clamps a long frame to 0.05s and
        /// simply runs slow below 20fps. Sub-stepping keeps match time equal to wall time on a
        /// phone that drops frames, without changing what any single tick does — every tick is still
        /// at most <see cref="MaxStep"/>, so the per-tick fixtures and tests hold unchanged.</para>
        /// <para>Returns the result on the frame the match settles and on every frame after, or
        /// null while play continues. Stops early on the sub-step that settles the match.</para>
        /// </remarks>
        public MatchResult? Advance(MatchState state, float dt, int maxSubsteps = DefaultMaxSubsteps)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "a frame needs a finite, non-negative length");
            if (maxSubsteps < 1)
                throw new ArgumentOutOfRangeException(nameof(maxSubsteps), maxSubsteps, "at least one sub-step is needed");

            MatchResult? result = state.Objectives.Result;
            for (int i = 0; i < maxSubsteps && dt > 0f; i++)
            {
                float step = dt > MaxStep ? MaxStep : dt;
                result = Tick(state, step);
                dt -= step;
                if (result.HasValue) break;
            }
            return result;
        }

        /// <summary>
        /// Run frames of <paramref name="step"/> until the match settles or
        /// <paramref name="maxFrames"/> is reached. For headless simulation and tests.
        /// </summary>
        public MatchResult? Run(MatchState state, float step = MaxStep, int maxFrames = 100000)
        {
            if (step <= 0f) throw new ArgumentOutOfRangeException(nameof(step), step, "a frame needs a positive length");
            for (int i = 0; i < maxFrames; i++)
            {
                var result = Tick(state, step);
                if (result.HasValue) return result;
            }
            return state.Objectives.Result;
        }
    }
}
