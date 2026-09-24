using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Objectives;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// U13. The win comparison and the match clock, checked against the browser game's own
    /// <c>endMatch()</c> rather than against expectations retyped here — see
    /// <c>tools/export_unity_objectives.js</c> for why.
    /// </summary>
    public class ObjectiveTests
    {
        [Serializable] public class ObjectiveCase
        {
            public string name;
            public int playerBuildingsDestroyed, enemyBuildingsDestroyed;
            public float playerBuildingDamage, enemyBuildingDamage;
            public float atSeconds;
            public bool instant;
            public int outcome;
            public string title, detail;
        }
        [Serializable] public class Fixture
        {
            public string generated, source;
            public int matchSeconds;
            public ObjectiveCase[] cases;
        }

        private static GameDatabase _db;
        private static Fixture _fixture;

        [OneTimeSetUp]
        public void Load()
        {
            GameDataLoader.Invalidate();
            _db = GameDataLoader.Load();
            _fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(
                Application.dataPath, "Godsbound/Tests/EditMode/Fixtures/objectives_reference.json")));
        }

        /// <summary>Three buildings a side, the first <paramref name="deadPerSide"/> of each destroyed.</summary>
        private static BuildingMap Map(int playerBuildingsDestroyed, int enemyBuildingsDestroyed)
        {
            var map = new BuildingMap();
            var types = new[] { BuildingType.City, BuildingType.Temple, BuildingType.Fortress };
            for (int side = 0; side < 2; side++)
            {
                // playerBuildingsDestroyed counts DESTROYED SIDE-1 buildings, matching the
                // browser's pKills; enemyBuildingsDestroyed counts the player's own losses.
                int dead = side == 1 ? playerBuildingsDestroyed : enemyBuildingsDestroyed;
                int row = side == 1 ? 1 : Board.Rows - 2;
                for (int i = 0; i < 3; i++)
                {
                    var b = new Building(side, types[i], types[i].ToString(),
                                         new Hex(i * 2, row), new Hex(i * 2 + 1, row), 100f);
                    if (i < dead) b.TakeDamage(b.MaxHp);
                    map.Add(b);
                }
            }
            map.Rebuild();
            return map;
        }

        private static MatchObjectives Objectives(BuildingMap map, float[] damage, float duration = 180f) =>
            new MatchObjectives(map, damage, new MatchClock(duration));

        /* ---- the clock ---- */

        [Test]
        public void ClockDurationComesFromTheExport()
        {
            Assert.AreEqual(_fixture.matchSeconds, _db.MatchSeconds,
                "the exported match length and the fixture's own reading of S.t disagree");
            Assert.AreEqual((float)_db.MatchSeconds, MatchClock.From(_db).Duration);
        }

        [Test]
        public void ClockCountsUpAndRemainingMirrorsTheBrowserCountdown()
        {
            var clock = MatchClock.From(_db);
            Assert.AreEqual(0f, clock.Elapsed, "a match starts at zero elapsed");
            Assert.AreEqual(_db.MatchSeconds, clock.Remaining, 1e-4f, "...and a full clock remaining");

            clock.Advance(30f);
            // The browser reports elapsed() and displays S.t; both must still be derivable.
            Assert.AreEqual(30f, clock.Elapsed, 1e-4f);
            Assert.AreEqual(_db.MatchSeconds - 30f, clock.Remaining, 1e-4f);
            Assert.IsFalse(clock.Expired);
        }

        [Test]
        public void ClockClampsAtTheEndAndReportsHowMuchItApplied()
        {
            var clock = new MatchClock(10f);
            Assert.AreEqual(4f, clock.Advance(4f), 1e-4f);
            // The tick that runs out the match applies only the remaining time, so a caller
            // stepping a fixed timestep cannot accumulate past the end.
            Assert.AreEqual(6f, clock.Advance(9f), 1e-4f, "only the remaining time is applied");
            Assert.AreEqual(10f, clock.Elapsed, 1e-4f);
            Assert.AreEqual(0f, clock.Remaining, 1e-4f);
            Assert.IsTrue(clock.Expired);
            Assert.AreEqual(0f, clock.Advance(5f), 1e-4f, "an expired clock absorbs nothing further");
            Assert.AreEqual(10f, clock.Elapsed, 1e-4f);
        }

        [Test]
        public void ClockRejectsNonsense()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchClock(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchClock(-1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MatchClock(10f).Advance(-1f));
        }

        /* ---- the win comparison, every case straight from the browser ---- */

        [Test]
        public void EveryBrowserEndStateResolvesTheSameWay()
        {
            Assert.IsNotNull(_fixture.cases, "fixture missing — run node tools/export_unity_objectives.js");
            Assert.Greater(_fixture.cases.Length, 0);

            foreach (var c in _fixture.cases)
            {
                var map = Map(c.playerBuildingsDestroyed, c.enemyBuildingsDestroyed);
                var objectives = Objectives(map, new[] { c.playerBuildingDamage, c.enemyBuildingDamage });
                objectives.Clock.Advance(c.atSeconds);

                var result = objectives.Evaluate();
                Assert.IsNotNull(result, $"[{c.name}] the match should have settled");

                Assert.AreEqual(c.outcome, (int)result.Value.Outcome,
                    $"[{c.name}] browser said {c.title.Trim()} for " +
                    $"{c.playerBuildingsDestroyed}-{c.enemyBuildingsDestroyed} buildings, " +
                    $"{c.playerBuildingDamage}-{c.enemyBuildingDamage} damage");
                Assert.AreEqual(c.playerBuildingsDestroyed, result.Value.PlayerBuildingsDestroyed, $"[{c.name}] pKills");
                Assert.AreEqual(c.enemyBuildingsDestroyed, result.Value.EnemyBuildingsDestroyed, $"[{c.name}] aKills");
                Assert.AreEqual(c.instant, result.Value.Instant, $"[{c.name}] instant-win flag");
                Assert.AreEqual(c.atSeconds, result.Value.AtSeconds, 1e-3f, $"[{c.name}] reported time");
            }
        }

        [Test]
        public void FixtureCoversBothDirectionsOfEveryBranch()
        {
            // A one-sided fixture would pass against an inverted comparison, which is the exact
            // failure this whole approach exists to catch.
            var outcomes = _fixture.cases.Select(c => c.outcome).ToList();
            CollectionAssert.Contains(outcomes, 1, "no victory case");
            CollectionAssert.Contains(outcomes, -1, "no defeat case");
            CollectionAssert.Contains(outcomes, 0, "no draw case");
            Assert.IsTrue(_fixture.cases.Any(c => c.playerBuildingsDestroyed == c.enemyBuildingsDestroyed
                                               && c.playerBuildingDamage != c.enemyBuildingDamage),
                "no case exercises the damage tiebreaker");
            Assert.IsTrue(_fixture.cases.Any(c => c.instant), "no instant-win case");
        }

        /* ---- how a live match reaches those states ---- */

        [Test]
        public void DestroyingTheThirdBuildingEndsTheMatchImmediately()
        {
            var map = Map(0, 0);
            var damage = new[] { 0f, 0f };
            var objectives = Objectives(map, damage);
            var enemy = map.Of(1).ToList();

            objectives.Clock.Advance(20f);
            Assert.IsNull(objectives.Evaluate(), "a match with every building standing is live");

            damage[0] += enemy[0].TakeDamage(enemy[0].MaxHp);
            Assert.IsNull(objectives.Evaluate(), "one building down is not a win");
            damage[0] += enemy[1].TakeDamage(enemy[1].MaxHp);
            Assert.IsNull(objectives.Evaluate(), "two buildings down is not a win");

            damage[0] += enemy[2].TakeDamage(enemy[2].MaxHp);
            var result = objectives.Evaluate();
            Assert.IsNotNull(result, "the third building ends it");
            Assert.AreEqual(MatchOutcome.Victory, result.Value.Outcome);
            Assert.IsTrue(result.Value.Instant, "an all-buildings win is flagged instant");
            Assert.AreEqual(20f, result.Value.AtSeconds, 1e-3f, "it ends when it happened, not at time");
            Assert.AreEqual(3, result.Value.PlayerBuildingsDestroyed);
        }

        [Test]
        public void AMatchThatRunsOutOfTimeSettlesOnTheTiebreaker()
        {
            var map = Map(0, 0);
            var objectives = Objectives(map, new[] { 120f, 119f }, 10f);
            Assert.IsNull(objectives.Tick(4f), "still playing");
            Assert.IsNull(objectives.Tick(4f), "still playing");

            var result = objectives.Tick(4f);
            Assert.IsNotNull(result, "the clock ran out");
            Assert.AreEqual(MatchOutcome.Victory, result.Value.Outcome, "level on buildings, ahead on damage");
            Assert.IsFalse(result.Value.Instant, "running out of time is not an instant win");
            Assert.AreEqual(10f, result.Value.AtSeconds, 1e-3f, "reported at the full duration, not past it");
        }

        [Test]
        public void ADeadLevelMatchIsADraw()
        {
            var objectives = Objectives(Map(1, 1), new[] { 250f, 250f }, 10f);
            objectives.Clock.Advance(10f);
            var result = objectives.Evaluate();
            Assert.IsNotNull(result);
            Assert.AreEqual(MatchOutcome.Draw, result.Value.Outcome);
        }

        [Test]
        public void ResolutionLatchesLikeTheBrowsersOverFlag()
        {
            var map = Map(0, 0);
            var damage = new[] { 5f, 0f };
            var objectives = Objectives(map, damage, 10f);
            objectives.Clock.Advance(10f);

            var first = objectives.Evaluate();
            Assert.IsNotNull(first);
            Assert.IsTrue(objectives.Resolved);

            // The browser guards re-entry with S.over; a settled match must not re-decide itself
            // when the world keeps changing underneath it.
            damage[1] = 9999f;
            foreach (var b in map.Of(0).ToList()) b.TakeDamage(b.MaxHp);

            var again = objectives.Evaluate();
            Assert.AreEqual(first.Value.Outcome, again.Value.Outcome, "a settled match keeps its result");
            Assert.AreEqual(first.Value.EnemyBuildingDamage, again.Value.EnemyBuildingDamage);
            Assert.AreEqual(first.Value.Outcome, objectives.Tick(5f).Value.Outcome, "ticking on changes nothing");
            Assert.AreEqual(first.Value.Outcome, objectives.Resolve().Outcome);
        }

        [Test]
        public void ResolveSettlesAnUnfinishedMatch()
        {
            var objectives = Objectives(Map(1, 0), new[] { 0f, 0f });
            objectives.Clock.Advance(12f);
            Assert.IsNull(objectives.Evaluate(), "not over on its own");

            var forced = objectives.Resolve();
            Assert.AreEqual(MatchOutcome.Victory, forced.Outcome, "ahead on buildings when forced to settle");
            Assert.AreEqual(12f, forced.AtSeconds, 1e-3f);
            Assert.IsFalse(forced.Instant);
        }

        [Test]
        public void ResetReturnsToALiveMatch()
        {
            var objectives = Objectives(Map(0, 0), new[] { 1f, 0f }, 10f);
            objectives.Tick(10f);
            Assert.IsTrue(objectives.Resolved);

            objectives.Reset();
            Assert.IsFalse(objectives.Resolved);
            Assert.IsNull(objectives.Result);
            Assert.AreEqual(0f, objectives.Clock.Elapsed, 1e-4f);
        }

        [Test]
        public void ObjectivesRejectNonsense()
        {
            var map = Map(0, 0);
            Assert.Throws<ArgumentNullException>(() => new MatchObjectives(null, new[] { 0f, 0f }, new MatchClock(10f)));
            Assert.Throws<ArgumentNullException>(() => new MatchObjectives(map, null, new MatchClock(10f)));
            Assert.Throws<ArgumentNullException>(() => new MatchObjectives(map, new[] { 0f, 0f }, null));
            Assert.Throws<ArgumentException>(() => new MatchObjectives(map, new[] { 0f }, new MatchClock(10f)),
                "damage must be indexed by side");
        }

        [Test]
        public void DamageIsReadLiveRatherThanCopied()
        {
            // MatchObjectives holds the accumulator by reference so CombatSystem.BuildingDamage
            // keeps working as the match runs; a copy taken at construction would freeze at zero.
            var damage = new[] { 0f, 0f };
            var objectives = Objectives(Map(0, 0), damage, 10f);
            damage[0] = 500f;
            objectives.Clock.Advance(10f);
            Assert.AreEqual(500f, objectives.Evaluate().Value.PlayerBuildingDamage,
                "the result must see damage accumulated after construction");
        }
    }
}
