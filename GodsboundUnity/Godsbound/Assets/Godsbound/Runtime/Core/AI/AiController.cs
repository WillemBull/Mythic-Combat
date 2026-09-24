using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Gods;
using Godsbound.Core.Match;
using Godsbound.Core.Units;

namespace Godsbound.Core.AI
{
    public sealed class AiWave
    {
        public readonly List<Unit> Units = new List<Unit>();
        public int Lane { get; internal set; }
        public int Size { get; internal set; }
        public float StartedAt { get; internal set; }
    }
    /// <summary>tickAI: rally waves, god progression (U24) and training. Power casting is U29.</summary>
    public sealed class AiController
    {
        private readonly MatchState state;
        private readonly Func<double> random;
        public AiUnitChoice Choice { get; }
        public AiWave Wave { get; private set; }
        public int? LastLane { get; private set; }
        public float TrainRemaining { get; private set; }
        public int ReleasedWaves { get; private set; }

        /// <summary>Raised when the AI buys a god — the browser's "Enemy unlocked X!" banner.</summary>
        public event Action<string> GodUnlocked;
        private int targetRotation;
        private AiRules Rules => Choice.Rules;
        public AiController(MatchState state, AiUnitChoice choice, Func<double> random)
        {
            this.state = state; Choice = choice; this.random = random;
            // U23: unlocked myths come from the AI side's gods, in roster order as the browser reads them.
            Choice.UnlockedMyths = () => state.Gods[1].UnlockedMythsInRosterOrder();
            Reset();
        }
        public static AiController Create(MatchState state, AiData data, string faction, Random rng = null)
        {
            if (state.Gods[1].Faction != "" && state.Gods[1].Faction != faction)
                throw new ArgumentException($"the AI side fields {state.Gods[1].Faction} gods but plays {faction}", nameof(faction));
            rng = rng ?? new Random();
            var profiles = data.profiles.Where(p => p.faction == faction).ToArray();
            var profile = profiles[rng.Next(profiles.Length)];
            var heroes = state.Database.AllUnits.Where(u => u.faction == faction && u.cat == "hero").Select(u => u.key).ToList();
            // A shuffled pair, as in browser startMatch; avoid its engine-dependent random sort comparator.
            for (int i = heroes.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = heroes[i]; heroes[i] = heroes[j]; heroes[j] = t; }
            return new AiController(state, new AiUnitChoice(profile, data.rules, heroes.Take(2), rng.NextDouble), rng.NextDouble);
        }
        /// <summary>
        /// The browser's one global <c>aiLastCastAt</c>: every god shares it, so the AI cannot dump
        /// three powers in a single frame. Settable so a fixture replay can suppress casting exactly
        /// as the exporter does.
        /// </summary>
        public float LastCastAt { get; set; }

        public void Reset()
        {
            Choice.Reset(); TrainRemaining = Rules.initialTrainDelay; Wave = null;
            LastLane = null; targetRotation = ReleasedWaves = 0;
            LastCastAt = Rules.castNever;
        }

        /// <summary>
        /// Trigger-driven casting: every unlocked god is evaluated, and the first that has a target
        /// hex, clears its trigger and passes <c>PowerSystem</c>'s validation is cast. One cast per
        /// pass, then the shared guard closes for <c>castGap</c> seconds.
        /// </summary>
        /// <remarks>
        /// Candidates are shuffled so the AI does not always favour the same god when several are
        /// ready at once. The browser shuffles with <c>sort(() =&gt; Math.random() - 0.5)</c>, whose
        /// result depends on the JS engine's sort and cannot be reproduced here, so this is a plain
        /// Fisher-Yates over the same list. The fixtures therefore leave exactly one god ready per
        /// cast case, and never assert which of several simultaneously-ready gods went first.
        /// </remarks>
        public void TickPowers()
        {
            if (state.Elapsed - LastCastAt < Rules.castGap) return;
            var keys = state.Gods[1].Selected.Where(s => s.Unlocked).Select(s => s.Key).ToList();
            for (int i = keys.Count - 1; i > 0; i--)
            { int j = (int)(random() * (i + 1)); var t = keys[i]; keys[i] = keys[j]; keys[j] = t; }
            foreach (var key in keys)
            {
                var hex = AiPowers.PickHex(state, Rules, key);
                if (!hex.HasValue || !AiPowers.TriggerOk(state, Rules, key, hex.Value)) continue;
                if (state.Powers.TryCast(1, key, hex.Value).Paid) { LastCastAt = state.Elapsed; break; }
            }
        }
        private bool Legal(Hex h, bool flying) => state.Terrain.RouteOk(h, flying, state.Buildings.RouteBlocker);
        public List<Hex> RallyHexes(bool flying, int lane)
        {
            var anchor = new Hex(lane, Board.PlayerRow0);
            return Board.AllCells().Where(h => Legal(h, flying)).OrderBy(h => Hex.Distance(h, anchor))
                .ThenBy(h => Math.Abs(h.C - lane)).ToList();
        }
        public AiWave EnsureWave()
        {
            if (Wave != null) return Wave;
            var lanes = Rules.lanes.Where(l => l != LastLane).ToArray();
            int lane = lanes[(int)(random() * lanes.Length)]; LastLane = lane;
            Wave = new AiWave { Lane = lane, Size = Rules.waveMinimum + (int)(random() * Rules.waveRandom), StartedAt = state.Elapsed };
            return Wave;
        }
        public void RegisterSpawns(IEnumerable<Unit> spawned)
        {
            if (Wave == null) return;
            foreach (var u in spawned) if (u.Side == 1 && u.Duty == "attack") Wave.Units.Add(u);
        }
        public static List<Hex> LoopErasedPath(IEnumerable<Hex> path)
        {
            var result = new List<Hex>();
            foreach (var h in path)
            {
                int at = result.IndexOf(h);
                if (at >= 0) result.RemoveRange(at + 1, result.Count - at - 1);
                else result.Add(h);
            }
            return result;
        }
        private void RouteMember(Unit unit, Building target, int lane)
        {
            var goals = state.Buildings.AdjacentRoutableHexes(target, unit.Flying, state.Terrain);
            var waypoint = new Hex(lane, Rules.waypointRow);
            var first = state.Paths.FindPath(unit.Hex, new[] { waypoint }, unit.Flying);
            var second = first != null ? state.Paths.FindPath(waypoint, goals, unit.Flying) : null;
            var full = first != null && second != null ? LoopErasedPath(first.Concat(second.Skip(1))) :
                state.Paths.FindPath(unit.Hex, goals, unit.Flying);
            unit.SetRoute(full != null && full.Count > 1 ? full.Skip(1) : null);
        }
        public void TickWave()
        {
            if (Wave == null) return;
            Wave.Units.RemoveAll(u => u.Dead);
            if (Wave.Units.Count(u => u.Hold) < Wave.Size && state.Elapsed - Wave.StartedAt <= Rules.waveTimeout) return;
            var alive = state.Buildings.Of(0).Where(b => !b.Dead).ToArray();
            var city = state.Buildings.Of(0, BuildingType.City);
            var target = Choice.Profile.rushCity && city != null && !city.Dead ? city :
                alive.Length > 0 ? alive[targetRotation++ % alive.Length] : null;
            if (target != null)
            {
                foreach (var u in Wave.Units)
                {
                    u.Hold = false; u.Engagement = null;
                    // Wave release assigns a destination once. UnitUpdate never rebuilds it merely because it is exhausted.
                    if (u.MarchGoal != target) { u.MarchGoal = target; RouteMember(u, target, Wave.Lane); }
                }
                ReleasedWaves++;
            }
            Wave = null;
        }
        public void PrepareUnit(Unit unit)
        {
            if (unit.Side != 1 || unit.Duty != "attack" || Wave == null || !Wave.Units.Contains(unit)) return;
            if (unit.Arrived && unit.Engagement == null && unit.HasRoute &&
                Hex.Distance(unit.Hex, new Hex(Wave.Lane, Board.PlayerRow0)) <= Rules.rallySettleDistance &&
                Wave.Units.Any(o => o != unit && !o.Dead && o.Hold && o.Hex.Equals(unit.Route[unit.RouteIndex])))
            { unit.SetRoute(null); unit.Hold = true; }
            if (unit.Free && unit.Engagement == null) unit.Hold = true;
        }
        /// <summary>
        /// The god-unlock half of tickAI. Each selected god, in roster order, is bought as soon as
        /// its profile schedule has come due and the AI holds its (Ledger-discounted) price.
        /// </summary>
        /// <remarks>
        /// Runs BEFORE training every tick, exactly as in the browser, so a due god always wins
        /// favor over a myth purchase in the same tick. Siege saving only gates training and never
        /// blocks an unlock. Only gods in the AI side's selected four can ever be bought.
        /// </remarks>
        public void UnlockDueGods()
        {
            var gods = state.Gods[1];
            foreach (var slot in gods.Selected)
            {
                if (slot.Unlocked) continue;
                var due = Choice.Profile.GodDueAt(slot.Key);
                if (!due.HasValue || state.Elapsed < due.Value) continue;
                if (state.Resources[1].Favor < state.GodUnlockCost(1, slot.Def)) continue;
                if (state.TryUnlockGod(1, slot.Key) == UnlockOutcome.Unlocked) GodUnlocked?.Invoke(slot.Key);
            }
        }

        public void Tick(float dt)
        {
            if (!state.Live || dt <= 0) return;
            TickWave();
            UnlockDueGods();
            TickPowers(); // browser order: after the unlocks, before training
            TrainRemaining -= dt;
            if (TrainRemaining > 0) return;
            TrainRemaining = Choice.Profile.trainMin + (float)random() * Choice.Profile.trainRand;
            var key = Choice.Pick(state); if (key == null) return;
            var def = state.Database.Unit(Choice.Profile.faction, key);
            var invaders = state.Units.AliveOf(0).Where(u => u.Hex.R < Board.AiRows).ToArray();
            var source = state.Buildings.Of(1).FirstOrDefault(b => !b.Dead && state.Units.AliveOf(0)
                .Any(u => b.Hexes().Min(h => Hex.Distance(h, u.Hex)) <= Rules.defenseDistance));
            if (source == null)
            {
                source = state.Buildings.Of(1, BuildingType.City);
                if (source == null || source.Dead) source = state.Buildings.Of(1, BuildingType.Fortress);
                if (source == null || source.Dead) source = state.Buildings.Of(1).FirstOrDefault(b => !b.Dead);
            }
            if (source == null) return;
            var starts = state.Buildings.AdjacentRoutableHexes(source, def.IsFlying, state.Terrain).OrderByDescending(h => h.R).ToArray();
            if (starts.Length == 0) return;
            int defenders = state.Units.AliveOf(1).Count(u => u.Duty == "defend");
            // invaders[0] is read below, so "defend" needs at least one invader even if a profile's
            // defenseMinimum were ever exported as 0.
            string duty = invaders.Length > 0 && invaders.Length >= Rules.defenseMinimum &&
                          defenders < Math.Min(Rules.defenseMaximum, invaders.Length) ? "defend" : "attack";
            IEnumerable<Hex> goals;
            if (duty == "defend") goals = new[] { invaders[0].Hex }.Concat(invaders[0].Hex.Neighbors()).Where(h => Legal(h, def.IsFlying));
            else
            {
                var wave = EnsureWave();
                int reserved = wave.Units.Count + state.Training.PendingFor(1).Count(o => o.Duty == "attack");
                var available = RallyHexes(def.IsFlying, wave.Lane).Where(h => state.Units.HasCapacity(h, def.IsFlying)).ToArray();
                goals = available.Length > 0 ? new[] { available[Math.Max(0, wave.Size - 1 - reserved) % available.Length] } : Array.Empty<Hex>();
            }
            var path = state.Paths.FindPath(starts[0], goals, def.IsFlying);
            if (path == null || path.Count == 0) return;
            if (state.Training.TryEnqueue(state.Resources[1], 1, def, path[0], source, state.TrainingModifiersFor(1), state.Elapsed,
                out var remaining, out var order, path, duty: duty))
            {
                state.Resources[1] = remaining;
                if (state.Powers.ConsumePtahCharge(1, def)) order.SpawnCount = 2;
            }
        }
    }
}
