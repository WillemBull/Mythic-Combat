using System;
using System.Linq;
using NUnit.Framework;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Match;
using Godsbound.Core.Objectives;
using Godsbound.Core.Training;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>U14. The match loop: tick order, the frame clamp, and the live-match gate.</summary>
    public class MatchTests
    {
        private static GameDatabase _db;

        [OneTimeSetUp]
        public void Load()
        {
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
        }

        private static MatchState State() => new MatchState(_db, pathJitter: false);
        private static UnitData AnyUnit() => _db.AllUnits.First(u => u.cat == "human" && !u.IsFlying);

        /* ---- the frame clamp ---- */

        [Test]
        public void ALongFrameIsClampedToTheBrowsersMaximumStep()
        {
            var s = State();
            var loop = new MatchLoop();

            loop.Tick(s, 10f);
            // The browser does Math.min(0.05, …) before anything reads dt. A frame that advanced
            // the clock by ten seconds would also have moved units ten seconds' worth, straight
            // through any blocker between them and their destination.
            Assert.AreEqual(MatchLoop.MaxStep, s.Elapsed, 1e-5f);
            Assert.AreEqual(0.05f, MatchLoop.MaxStep, 1e-6f, "the clamp must match loop()'s own 0.05");
        }

        [Test]
        public void AdvanceSubStepsALongFrameSoMatchTimeKeepsUpWithWallTime()
        {
            var s = State();
            var loop = new MatchLoop();

            // 0.12s of wall time is three ticks (0.05 + 0.05 + 0.02), none longer than the clamp.
            loop.Advance(s, 0.12f);
            Assert.AreEqual(0.12f, s.Elapsed, 1e-5f, "a dropped frame is caught up, not slowed down");

            // A short frame is one tick, unchanged.
            loop.Advance(s, 0.03f);
            Assert.AreEqual(0.15f, s.Elapsed, 1e-5f);
        }

        [Test]
        public void AdvanceDropsTimeBeyondTheSubStepBudget()
        {
            var s = State();
            var loop = new MatchLoop();

            // A multi-second stall must not start a catch-up spiral: at most
            // DefaultMaxSubsteps ticks of MaxStep run and the remainder is discarded.
            loop.Advance(s, 10f);
            Assert.AreEqual(MatchLoop.DefaultMaxSubsteps * MatchLoop.MaxStep, s.Elapsed, 1e-5f);

            // The budget is a parameter, so a headless simulation can raise it.
            loop.Advance(s, 1f, maxSubsteps: 20);
            Assert.AreEqual(MatchLoop.DefaultMaxSubsteps * MatchLoop.MaxStep + 1f, s.Elapsed, 1e-4f);
        }

        [Test]
        public void AShortFrameIsUsedWhole()
        {
            var s = State();
            new MatchLoop().Tick(s, 0.016f);
            Assert.AreEqual(0.016f, s.Elapsed, 1e-5f);
        }

        [Test]
        public void TheLoopRejectsNonsenseFrames()
        {
            var s = State();
            var loop = new MatchLoop();
            Assert.Throws<ArgumentNullException>(() => loop.Tick(null, 0.016f));
            Assert.Throws<ArgumentOutOfRangeException>(() => loop.Tick(s, -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => loop.Tick(s, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => loop.Tick(s, float.PositiveInfinity));
        }

        /* ---- the live-match gate ---- */

        [Test]
        public void NothingSimulatesOutsideBattlePhase()
        {
            var s = State();
            s.Phase = MatchPhase.Setup;
            Assert.IsFalse(s.Live);

            new MatchLoop().Tick(s, 0.05f);
            Assert.AreEqual(0f, s.Elapsed, 1e-6f, "the clock must not run during setup");
            Assert.AreEqual(0f, s.Resources[0].Food, 1e-6f, "and no resources accrue");
        }

        [Test]
        public void ASettledMatchTicksToANoOp()
        {
            var s = State();
            var loop = new MatchLoop();
            foreach (var b in s.Buildings.Of(1).ToList()) b.TakeDamage(b.MaxHp);

            var settled = loop.Tick(s, 0.05f);
            Assert.IsNotNull(settled, "wiping a side settles the match");

            float elapsedAtEnd = s.Elapsed;
            float foodAtEnd = s.Resources[0].Food;
            var again = loop.Tick(s, 0.05f);

            Assert.AreEqual(settled.Value.Outcome, again.Value.Outcome, "the result stands");
            Assert.AreEqual(elapsedAtEnd, s.Elapsed, 1e-6f, "the clock stops");
            Assert.AreEqual(foodAtEnd, s.Resources[0].Food, 1e-6f, "the economy stops");
        }

        /* ---- economy ---- */

        [Test]
        public void ResourcesAccrueAtTheRateEconomyTestsAlreadyPins()
        {
            var s = State();
            var loop = new MatchLoop();

            // Drive the same conditions through EconomyTick directly; the loop must agree with
            // the system it calls rather than with a number retyped here.
            float expectedFood = EconomyTick.FoodPerSecond(s.ConditionsFor(0), _db.Economy);
            float expectedFavor = EconomyTick.FavorPerSecond(EconomySide.Player, s.ConditionsFor(0), _db.Economy);

            for (int i = 0; i < 20; i++) loop.Tick(s, 0.05f); // one second

            Assert.AreEqual(expectedFood, s.Resources[0].Food, 1e-3f);
            Assert.AreEqual(expectedFavor, s.Resources[0].Favor, 1e-3f);
            Assert.Greater(s.Resources[1].Food, 0f, "the AI side earns too, even with no AI driving it");
        }

        [Test]
        public void LosingTheCityCutsFoodMidMatch()
        {
            var s = State();
            var loop = new MatchLoop();
            for (int i = 0; i < 20; i++) loop.Tick(s, 0.05f);
            float firstSecond = s.Resources[0].Food;

            var city = s.Buildings.Of(0, BuildingType.City);
            city.TakeDamage(city.MaxHp);

            for (int i = 0; i < 20; i++) loop.Tick(s, 0.05f);
            float secondSecond = s.Resources[0].Food - firstSecond;

            // ConditionsFor reads live building state every frame, so this must fall without any
            // explicit notification from the building to the economy.
            Assert.Less(secondSecond, firstSecond, "food income drops once the city is gone");
        }

        [Test]
        public void ConditionsAreDerivedFromLiveBuildings()
        {
            var s = State();
            var c = s.ConditionsFor(0);
            Assert.IsTrue(c.CityStanding);
            Assert.IsTrue(c.TempleStanding);
            Assert.AreEqual(3, c.StandingBuildings);

            var temple = s.Buildings.Of(0, BuildingType.Temple);
            temple.TakeDamage(temple.MaxHp);

            c = s.ConditionsFor(0);
            Assert.IsFalse(c.TempleStanding);
            Assert.AreEqual(2, c.StandingBuildings);
            Assert.IsTrue(c.TempleBonus, "templeBonus is the browser's baseline, not a passive");
        }

        /* ---- training and units ---- */

        [Test]
        public void AQueuedUnitAppearsOnceItsTimerElapses()
        {
            var s = State();
            var loop = new MatchLoop();
            var def = AnyUnit();
            var source = s.Buildings.Of(0, BuildingType.City);

            s.Resources[0] = new Purse(500f, 500f);
            Assert.IsTrue(s.Training.TryEnqueue(s.Resources[0], 0, def, source.HexA, source,
                                                new TrainingModifiers(), 0f,
                                                out var charged, out var order),
                          "the order should be affordable");
            s.Resources[0] = charged;
            Assert.AreEqual(1, s.Training.PendingCount);
            Assert.AreEqual(0, s.Units.AliveCount, "nothing spawns before the loop runs");

            // Wait through this definition's actual training deadline, then one frame to spawn.
            int frames = (int)Math.Ceiling(order.ReadyAt / MatchLoop.MaxStep) + 1;
            for (int i = 0; i < frames && s.Units.AliveCount == 0; i++) loop.Tick(s, MatchLoop.MaxStep);

            Assert.AreEqual(1, s.Units.AliveCount, "the trained unit joined the field");
            Assert.AreEqual(0, s.Training.PendingCount, "and left the queue");
            Assert.AreEqual(1, s.Units.All.Count, "exactly once -- Spawn already adds, the return list must not be re-added");
        }

        [Test]
        public void AUnitWithARouteAdvancesAlongIt()
        {
            var s = State();
            var loop = new MatchLoop();
            var start = new Hex(4, Board.Rows - 2);
            var u = s.Units.Spawn(0, AnyUnit(), start);

            var goal = new Hex(4, Board.Rows - 5);
            var route = s.Paths.FindPath(start, goal, flying: false);
            Assert.IsNotNull(route, "the board should be walkable here");
            u.SetRoute(route);

            for (int i = 0; i < 200 && u.Hex == start; i++) loop.Tick(s, 0.05f);

            Assert.AreNotEqual(start, u.Hex, "the unit left its starting hex");
            Assert.Less(Hex.Distance(u.Hex, goal), Hex.Distance(start, goal), "and moved toward the goal");
        }

        [Test]
        public void DeadUnitsAreSweptEachFrame()
        {
            var s = State();
            var u = s.Units.Spawn(0, AnyUnit(), new Hex(4, Board.Rows - 2));
            u.TakeDamage(u.MaxHp);
            Assert.AreEqual(1, s.Units.All.Count, "still listed before the loop runs");

            new MatchLoop().Tick(s, 0.05f);

            // retainUnitAfterUpdate is NOT ported: holding a corpse for a death animation is a
            // presentation concern, and Core must not carry it.
            Assert.AreEqual(0, s.Units.All.Count, "the corpse is gone from the simulation");
        }

        /* ---- objectives ---- */

        [Test]
        public void RunningTheClockOutSettlesTheMatch()
        {
            var s = State();
            var result = new MatchLoop().Run(s);

            Assert.IsNotNull(result, "a match must end");
            Assert.AreEqual(_db.MatchSeconds, s.Elapsed, 0.05f, "at the full duration");
            Assert.IsFalse(result.Value.Instant, "by time, not by wipe");
            Assert.AreEqual(MatchOutcome.Draw, result.Value.Outcome,
                "with no AI and no combat, nothing separates the sides");
        }

        [Test]
        public void DestroyingTheLastBuildingSettlesTheMatchInTheSameFrame()
        {
            var s = State();
            var loop = new MatchLoop();
            var enemy = s.Buildings.Of(1).ToList();
            for (int i = 0; i < enemy.Count - 1; i++) enemy[i].TakeDamage(enemy[i].MaxHp);

            Assert.IsNull(loop.Tick(s, 0.05f), "two down is not a win");
            float before = s.Elapsed;

            enemy[enemy.Count - 1].TakeDamage(enemy[enemy.Count - 1].MaxHp);
            var result = loop.Tick(s, 0.05f);

            Assert.IsNotNull(result, "objectives are evaluated after damage, not before");
            Assert.AreEqual(MatchOutcome.Victory, result.Value.Outcome);
            Assert.IsTrue(result.Value.Instant);
            Assert.AreEqual(before + 0.05f, result.Value.AtSeconds, 1e-4f, "settled on this frame");
        }

        /* ---- lifecycle ---- */

        [Test]
        public void ResetReturnsASettledMatchToAFreshOne()
        {
            var s = State();
            var loop = new MatchLoop();
            var hitter = _db.AllUnits.First(u => u.cat == "human" && !u.IsFlying && u.dmg > 0f);
            var attacker = s.Units.Spawn(0, hitter, new Hex(4, Board.Rows - 2));
            var victim = s.Units.Spawn(1, AnyUnit(), new Hex(4, Board.Rows - 3));
            // Real combat bookkeeping, so the reset has something to clear: a human death record
            // and building damage in the tiebreaker accumulator that MatchObjectives reads by reference.
            s.Combat.KillUnit(victim, 1f);
            s.Combat.DealDamage(attacker, s.Buildings.Of(1).First(), 1f);
            Assert.AreEqual(1, s.Combat.DeadHumans.Count);
            Assert.Greater(s.Combat.BuildingDamage[0], 0f);
            loop.Run(s);
            foreach (var b in s.Buildings.Of(1).ToList()) b.TakeDamage(b.MaxHp);
            Assert.IsTrue(s.Objectives.Resolved);

            s.Reset();

            Assert.IsFalse(s.Objectives.Resolved);
            Assert.AreEqual(0f, s.Elapsed, 1e-6f);
            Assert.AreEqual(0f, s.Resources[0].Food, 1e-6f);
            Assert.AreEqual(0, s.Units.All.Count);
            Assert.AreEqual(0, s.Training.PendingCount);
            Assert.IsTrue(s.Buildings.All.All(b => !b.Dead), "buildings stand again");
            Assert.IsTrue(s.Live, "and the match is playable");
            Assert.AreEqual(0, s.Combat.DeadHumans.Count, "death records do not carry across matches");
            Assert.AreEqual(0f, s.Combat.BuildingDamage[0], "the damage tiebreaker starts from zero");
            Assert.AreEqual(0f, s.Combat.BuildingDamage[1], "for both sides");
        }

        [Test]
        public void CombatRewardsShareThePursesTheEconomyFills()
        {
            // CombatContext.Resources is pointed at MatchState.Resources, so a kill refund and a
            // second of income land in the same purse rather than two that silently diverge.
            var context = new Godsbound.Core.Combat.CombatContext();
            var s = new MatchState(_db, combatContext: context, pathJitter: false);
            new MatchLoop().Tick(s, 0.05f);
            Assert.Greater(s.Resources[0].Food, 0f, "the economy filled the purse");
            Assert.AreSame(s.Resources, context.Resources,
                "combat and the economy must share one purse array, or rewards land where nothing reads them");
        }

        [Test]
        public void TheUnitUpdateSeamIsCalledForEveryLivingUnit()
        {
            var s = State();
            var spy = new CountingUpdate();
            var loop = new MatchLoop(spy);
            s.Units.Spawn(0, AnyUnit(), new Hex(4, Board.Rows - 2));
            s.Units.Spawn(1, AnyUnit(), new Hex(4, 1));
            var dead = s.Units.Spawn(0, AnyUnit(), new Hex(5, Board.Rows - 2));
            dead.TakeDamage(dead.MaxHp);

            loop.Tick(s, 0.05f);

            Assert.AreEqual(2, spy.Calls, "every living unit updates; the dead one does not");
        }

        private sealed class CountingUpdate : IUnitUpdate
        {
            public int Calls;
            public void Update(MatchState state, Unit unit, float dt, float elapsed) => Calls++;
        }
    }
}
