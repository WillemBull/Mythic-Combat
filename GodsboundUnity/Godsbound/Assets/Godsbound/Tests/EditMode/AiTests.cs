using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.AI;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Economy;
using Godsbound.Core.Movement;
using Godsbound.Core.Gods;
using Godsbound.Core.Match;
using Godsbound.Core.Training;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    public class AiTests
    {
        [Serializable] public class Cell { public int c, r; public Hex Hex => new Hex(c, r); }
        [Serializable] public class Body { public int side, c, r; public string key; }
        [Serializable] public class ChoiceCase
        {
            public string faction, style, mode, expected;
            public float random, food, favor, time, saveStart, heroAt, nextHeroAt, nextSave;
            public Body[] units; public string[] queued, myths;
        }
        [Serializable] public class Order { public string key, source, duty; public Cell[] path; }
        [Serializable] public class Deployment
        {
            public string faction, mode; public Body[] units; public string[] dead;
            public float food, favor; public Order[] orders; public int lane, size;
        }
        [Serializable] public class Rally { public bool flying; public int lane; public Cell[] cells; }
        [Serializable] public class GodEvent { public int step; public float time; public string key; }
        [Serializable] public class GodTimeline
        {
            public string faction, style; public float income, start, stepSeconds;
            public string[] selected, myths; public float[] favorIn, favorOut; public GodEvent[] events;
        }
        [Serializable] public class GodCompetition
        {
            public string faction, style, mode, siege; public float time, food, favor, foodAfter, favorAfter, saveStart, heroAt;
            public bool siegeAlive; public string[] granted, unlocked, orders;
        }
        [Serializable] public class PowerRow { public string key, cls; public Cell hex; public int hits; public bool trigger; }
        [Serializable] public class PowerBody { public int side, c, r; public string key; public float hp; public bool engaged; }
        [Serializable] public class PowerBuilding
        {
            public int side, c0, r0, c1, r1; public string type; public float hp, maxhp, lastHitAt; public bool dead;
        }
        [Serializable] public class Corpse { public int side; public string key; public float at; }
        [Serializable] public class PowerBoard
        {
            public string label, faction; public float time, favor;
            public string[] unlocked, terrain, playerPick; public PowerRow[] rows;
            public PowerBody[] units; public PowerBuilding[] buildings; public Corpse[] dead;
        }
        [Serializable] public class CastUnit
        {
            public int side, c, r; public string key; public float hp, slowUntil, stunUntil, conscriptedUntil;
            public bool dead, transformed;
        }
        [Serializable] public class PowerCastCase
        {
            public string label, faction, key; public float time, favor; public bool gapBlocked;
            public string[] pick, terrain, playerPick;
            public PowerBody[] units; public PowerBuilding[] buildings; public Corpse[] dead;
            public float favorBefore, favorAfter, cdAfter, lastCastAt;
            public float[] chaosAfter, bldHpAfter, bldInvulnAfter;
            public CastUnit[] unitsAfter; public int overlays;
        }
        [Serializable] public class Fixture
        {
            public ChoiceCase[] choices; public Deployment[] deployments; public Rally[] rallies;
            public Cell[] loopInput, loopOutput;
            public GodTimeline[] godTimelines; public GodCompetition[] godCompetition;
            public PowerBoard[] powerDecisions; public PowerCastCase[] powerCasts;
        }
        private sealed class ConstantRandom : System.Random { public override double NextDouble() => 0.5; }
        private AiData data;
        private Fixture fixture;
        [OneTimeSetUp] public void Load()
        {
            data = AiDataLoader.Load();
            fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,"Godsbound/Tests/EditMode/Fixtures/ai_reference.json")));
        }
        private MatchState State()
        {
            var state = new MatchState(GameDataLoader.Load(), rng: new ConstantRandom());
            foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
            return state;
        }
        /// <summary>A plains board whose AI side fields its faction's default four gods.</summary>
        private MatchState StateWithAiGods(string faction)
        {
            var db = GameDataLoader.Load();
            var state = new MatchState(db, rng: new ConstantRandom(),
                gods: new[] { GodState.None(0), GodState.Default(1, db, faction) });
            foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
            return state;
        }
        private void Bodies(MatchState state, string faction, Body[] units)
        {
            foreach (var u in units) state.Units.Spawn(u.side, state.Database.Unit(u.side == 0 ? "egypt" : faction,u.key),new Hex(u.c,u.r));
        }
        private AiController Attach(MatchState state, string faction = "china", string style = "balanced")
        {
            var choice = new AiUnitChoice(data.Profile(faction,style),data.rules,state.Database.Faction(faction).defaultHeroes,()=>0.5);
            return state.Ai = new AiController(state,choice,()=>0.5);
        }
        [Test] public void AllFourHundredChoicesMatchBrowserIncludingTimers()
        {
            foreach (var sample in fixture.choices)
            {
                var state = State(); state.Objectives.Clock.Advance(sample.time);
                state.Resources[1] = new Purse(sample.food,sample.favor); Bodies(state,sample.faction,sample.units);
                foreach (var key in sample.queued)
                    state.Training.TryEnqueue(new Purse(120,250),1,state.Database.Unit(sample.faction,key),new Hex(4,3),
                        state.Buildings.Of(1,BuildingType.City),default,state.Elapsed,out _,out _);
                var choice = new AiUnitChoice(data.Profile(sample.faction,sample.style),data.rules,
                    state.Database.Faction(sample.faction).defaultHeroes,()=>sample.random);
                choice.HeroAt=sample.heroAt; choice.SaveStart=sample.saveStart<0?(float?)null:sample.saveStart;
                choice.UnlockedMyths=()=>sample.myths;
                string label=$"{sample.faction}/{sample.style}/{sample.mode}/{sample.food}/{sample.random}";
                Assert.That(choice.Pick(state),Is.EqualTo(string.IsNullOrEmpty(sample.expected)?null:sample.expected),label);
                Assert.That(choice.HeroAt,Is.EqualTo(sample.nextHeroAt).Within(0.001),label);
                Assert.That(choice.SaveStart??-1,Is.EqualTo(sample.nextSave).Within(0.001),label);
            }
        }
        [Test] public void RandomHeroRosterExcludesRebornForms()
        {
            var state=State();
            foreach(var faction in new[]{"china","egypt"})for(int seed=0;seed<20;seed++)
            {
                var ai=AiController.Create(state,data,faction,new System.Random(seed));
                Assert.That(ai.Choice.Heroes.Distinct().Count(),Is.EqualTo(2));
                foreach(var key in ai.Choice.Heroes)Assert.That(state.Database.Unit(faction,key).cat,Is.EqualTo("hero"));
            }
        }
        [Test] public void DeploymentPaymentSourcesDutiesAndRoutesMatchBrowser()
        {
            foreach (var sample in fixture.deployments)
            {
                var state=State(); state.Objectives.Clock.Advance(20); state.Resources[1]=new Purse(120,100);
                Bodies(state,sample.faction,sample.units);
                foreach(var type in sample.dead){var b=state.Buildings.Of(1).Single(x=>x.Type.ToString().ToLowerInvariant()==type);b.TakeDamage(b.Hp);}
                var ai=Attach(state,sample.faction);ai.Tick(data.rules.initialTrainDelay+0.05f);
                string label=sample.faction+"/"+sample.mode;
                Assert.That(state.Resources[1].Food,Is.EqualTo(sample.food),label);
                Assert.That(state.Resources[1].Favor,Is.EqualTo(sample.favor),label);
                Assert.That(state.Training.PendingCount,Is.EqualTo(sample.orders.Length),label);
                for(int i=0;i<sample.orders.Length;i++)
                {
                    var actual=state.Training.Pending[i];var expected=sample.orders[i];
                    Assert.That(actual.Def.key,Is.EqualTo(expected.key),label);
                    Assert.That(actual.Duty,Is.EqualTo(expected.duty),label);
                    Assert.That(actual.Source.Type.ToString().ToLowerInvariant(),Is.EqualTo(expected.source),label);
                    Assert.That(actual.Route,Is.EqualTo(expected.path.Select(h=>h.Hex).ToArray()),label);
                }
                Assert.That(ai.Wave?.Lane??-1,Is.EqualTo(sample.lane),label);
                Assert.That(ai.Wave?.Size??0,Is.EqualTo(sample.size),label);
            }
        }
        [Test] public void RallyOrderAndLoopErasureMatchBrowser()
        {
            var state=State();var ai=Attach(state);
            foreach(var sample in fixture.rallies)
                Assert.That(ai.RallyHexes(sample.flying,sample.lane),Is.EqualTo(sample.cells.Select(h=>h.Hex).ToArray()));
            Assert.That(AiController.LoopErasedPath(fixture.loopInput.Select(h=>h.Hex)),Is.EqualTo(fixture.loopOutput.Select(h=>h.Hex).ToArray()));
        }
        // ---- U24: AI god progression --------------------------------------------------

        [Test] public void ProfilesCarryTheBrowserGodSchedules()
        {
            foreach (var p in data.profiles)
            {
                Assert.That(p.godSched, Is.Not.Empty, p.faction + "/" + p.style);
                var pick = GameDataLoader.Load().Faction(p.faction).defaultGodPick;
                foreach (var e in p.godSched) Assert.That(pick, Does.Contain(e.key), $"{p.faction}/{p.style} schedules {e.key}");
                Assert.That(p.GodDueAt("not-a-god"), Is.Null);
            }
        }
        /// <summary>
        /// The board a U29 power fixture describes: the exporter's own building ORDER (not the
        /// standard export order), because "any own hex" powers read the first standing building.
        /// </summary>
        private MatchState PowerState(string faction, string[] playerPick, string[] pick, float time, float favor,
                                      PowerBuilding[] blds, string[] terrain, PowerBody[] units, Corpse[] dead)
        {
            var db = GameDataLoader.Load();
            var map = new BuildingMap(blds.Select(b => new Building(b.side, Building.ParseType(b.type), b.type,
                new Hex(b.c0, b.r0), new Hex(b.c1, b.r1), b.maxhp)));
            Func<int, string> sideFaction = s2 => s2 == 0 ? "egypt" : faction;
            var state = new MatchState(db, buildings: map, rng: new ConstantRandom(),
                combatContext: new CombatContext { FactionForSide = sideFaction },
                movementContext: new MovementContext { FactionForSide = sideFaction },
                gods: new[] { GodState.Create(0, db, "egypt", playerPick), GodState.Create(1, db, faction, pick) });
            for (int r = 0; r < Board.Rows; r++)
                for (int c = 0; c < Board.Cols; c++)
                    state.Terrain[c, r] = TerrainTable.FromCode(terrain[r][c]);
            for (int i = 0; i < blds.Length; i++)
            {
                var row = blds[i]; var b = map.All[i];
                if (row.dead || row.hp <= 0f) b.TakeDamage(b.Hp);
                else if (row.hp < b.Hp) b.TakeDamage(b.Hp - row.hp);
                b.LastHitAt = row.lastHitAt < 0f ? 0f : row.lastHitAt; // the browser leaves it undefined
            }
            state.Objectives.Clock.Advance(time);
            foreach (var u in units)
            {
                var body = state.Units.Spawn(u.side, db.ResolveUnit(sideFaction(u.side), u.key), new Hex(u.c, u.r));
                if (u.hp < body.Hp) body.TakeDamage(body.Hp - u.hp);
                // The control trigger asks only whether a caught unit HAS a target, never which,
                // so any contact reproduces the browser's u.target faithfully enough to assert on.
                if (u.engaged) body.Engagement = new MovementContact(map.All[0]);
            }
            foreach (var d in dead) state.Combat.RememberDeath(new HumanDeath(d.key, d.side, new Hex(0, 0), d.at));
            state.Resources[1] = new Purse(120f, favor);
            return state;
        }

        [Test] public void AiPowerPicksAndTriggersMatchBrowserBoardForBoard()
        {
            Assert.That(fixture.powerDecisions, Is.Not.Empty);
            foreach (var board in fixture.powerDecisions)
            {
                var state = PowerState(board.faction, board.playerPick, board.rows.Select(r => r.key).ToArray(),
                    board.time, board.favor, board.buildings, board.terrain, board.units, board.dead);
                foreach (var key in board.unlocked) Assert.That(state.Gods[1].Grant(key), Is.True, board.label + " " + key);
                foreach (var row in board.rows)
                {
                    string label = board.label + " " + row.key;
                    Assert.That(data.rules.PowerClass(row.key), Is.EqualTo(row.cls), label + " class");
                    var hex = AiPowers.PickHex(state, data.rules, row.key);
                    if (row.hex.c < 0) { Assert.That(hex.HasValue, Is.False, label + " holds the power"); continue; }
                    Assert.That(hex.HasValue, Is.True, label + " has a target");
                    Assert.That(hex.Value, Is.EqualTo(row.hex.Hex), label + " aim");
                    Assert.That(AiPowers.Hits(state, data.rules, row.key, hex.Value), Is.EqualTo(row.hits), label + " hits");
                    Assert.That(AiPowers.TriggerOk(state, data.rules, row.key, hex.Value), Is.EqualTo(row.trigger), label + " trigger");
                }
            }
        }

        [Test] public void AiCastsThroughPowerSystemOnlyWhenTheSituationAsksForIt()
        {
            Assert.That(fixture.powerCasts, Is.Not.Empty);
            foreach (var c in fixture.powerCasts)
            {
                var state = PowerState(c.faction, c.playerPick, c.pick, c.time, c.favor,
                    c.buildings, c.terrain, c.units, c.dead);
                Assert.That(state.Gods[1].Grant(c.key), Is.True, c.label);
                var ai = Attach(state, c.faction);
                ai.LastCastAt = c.gapBlocked ? state.Elapsed : data.rules.castNever;
                Assert.That(state.Resources[1].Favor, Is.EqualTo(c.favorBefore).Within(1e-3), c.label + " favor before");
                ai.Tick(0.05f);
                Assert.That(state.Resources[1].Favor, Is.EqualTo(c.favorAfter).Within(1e-3), c.label + " favor after");
                Assert.That(state.Gods[1].Slot(c.key).CooldownUntil, Is.EqualTo(c.cdAfter).Within(1e-3), c.label + " cooldown");
                Assert.That(ai.LastCastAt, Is.EqualTo(c.lastCastAt).Within(1e-3), c.label + " cast guard");
                for (int side = 0; side < 2; side++)
                    Assert.That(state.Powers.ChaosBuffUntil[side], Is.EqualTo(c.chaosAfter[side]).Within(1e-3),
                        c.label + " chaos " + side);
                Assert.That(state.Terrain.Overlays.Count, Is.EqualTo(c.overlays), c.label + " overlays");
                var live = state.Units.All;
                Assert.That(live.Count, Is.EqualTo(c.unitsAfter.Length), c.label + " unit count");
                for (int i = 0; i < live.Count; i++)
                {
                    var u = live[i]; var w = c.unitsAfter[i]; string label = c.label + " unit " + i + " " + w.key;
                    Assert.That(u.Key, Is.EqualTo(w.key), label);
                    Assert.That(u.Side, Is.EqualTo(w.side), label + " side");
                    Assert.That(u.Hex, Is.EqualTo(new Hex(w.c, w.r)), label + " hex");
                    Assert.That(u.Hp, Is.EqualTo(w.hp).Within(1e-3), label + " hp");
                    Assert.That(u.Dead, Is.EqualTo(w.dead), label + " dead");
                    Assert.That(u.SlowUntil, Is.EqualTo(w.slowUntil).Within(1e-3), label + " slow");
                    Assert.That(u.StunUntil, Is.EqualTo(w.stunUntil).Within(1e-3), label + " stun");
                    Assert.That(u.ConscriptedUntil, Is.EqualTo(w.conscriptedUntil).Within(1e-3), label + " conscripted");
                    Assert.That(u.Transformed, Is.EqualTo(w.transformed), label + " transformed");
                }
                var blds = state.Buildings.All;
                for (int i = 0; i < blds.Count; i++)
                {
                    Assert.That(blds[i].Hp, Is.EqualTo(c.bldHpAfter[i]).Within(1e-3), c.label + " building hp " + i);
                    Assert.That(blds[i].InvulnerableUntil, Is.EqualTo(c.bldInvulnAfter[i]).Within(1e-3),
                        c.label + " building ward " + i);
                }
            }
        }

        [Test] public void EveryAiCastableGodHasATriggerClassAndAPicker()
        {
            var db = GameDataLoader.Load();
            var state = PowerState("china", db.Faction("egypt").defaultGodPick.ToArray(), db.Faction("china").defaultGodPick.ToArray(),
                60f, 300f, fixture.powerDecisions[0].buildings, fixture.powerDecisions[0].terrain,
                new PowerBody[0], new Corpse[0]);
            foreach (var entry in data.rules.powerClasses)
            {
                Assert.That(entry.cls, Is.Not.Empty, entry.key);
                // A class entry with no picker would be a god the AI is told how to judge but never aims.
                Assert.That(db.AllGods.Any(g => g.key == entry.key), Is.True, entry.key + " is a real god");
            }
            // The factions the AI never plays are left to the player, exactly as in the browser.
            foreach (var f in new[] { "greek", "aztec" })
                foreach (var g in db.Roster(f))
                    Assert.That(AiPowers.PickHex(state, data.rules, g.key), Is.Null, g.key + " is not AI-cast");
        }

        [Test] public void GodUnlockTimelinesMatchBrowserTickForTick()
        {
            Assert.That(fixture.godTimelines, Is.Not.Empty);
            foreach (var t in fixture.godTimelines)
            {
                var state = StateWithAiGods(t.faction);
                var ai = Attach(state, t.faction, t.style);
                Assert.That(state.Gods[1].Selected.Select(g => g.Key), Is.EqualTo(t.selected));
                string label0 = $"{t.faction}/{t.style} income {t.income} start {t.start}";
                for (int i = 0; i < t.favorIn.Length; i++)
                {
                    if (i > 0) state.Objectives.Clock.Advance(t.stepSeconds);
                    string label = label0 + " t=" + state.Elapsed;
                    var before = state.Gods[1].UnlockedSlots.Select(g => g.Key).ToList();
                    state.Resources[1] = new Purse(0f, t.favorIn[i]);
                    ai.UnlockDueGods();
                    Assert.That(state.Resources[1].Favor, Is.EqualTo(t.favorOut[i]).Within(1e-3), label);
                    var bought = state.Gods[1].Selected.Where(g => g.Unlocked && !before.Contains(g.Key)).Select(g => g.Key);
                    Assert.That(bought, Is.EqualTo(t.events.Where(e => e.step == i).Select(e => e.key)), label);
                }
                Assert.That(state.Gods[1].UnlockedMythsInRosterOrder(), Is.EqualTo(t.myths), label0);
            }
        }
        [Test] public void DueGodIsBoughtBeforeTrainingInTheSameTick()
        {
            Assert.That(fixture.godCompetition, Is.Not.Empty);
            foreach (var c in fixture.godCompetition)
            {
                var state = StateWithAiGods(c.faction); state.Objectives.Clock.Advance(c.time);
                state.Resources[1] = new Purse(c.food, c.favor);
                foreach (var k in c.granted) state.Gods[1].Grant(k);
                if (c.siegeAlive) state.Units.Spawn(1, state.Database.Unit(c.faction, c.siege), new Hex(4, 3));
                var ai = Attach(state, c.faction, c.style); ai.Choice.HeroAt = c.heroAt;
                ai.LastCastAt = state.Elapsed; // the exporter closes the cast guard here: this case is about myths and siege
                ai.Tick(data.rules.initialTrainDelay + 0.05f);
                string label = c.faction + "/" + c.mode;
                Assert.That(state.Gods[1].UnlockedSlots.Select(g => g.Key), Is.EqualTo(c.unlocked), label);
                Assert.That(state.Training.PendingFor(1).Select(o => o.Def.key), Is.EqualTo(c.orders), label);
                Assert.That(state.Resources[1].Food, Is.EqualTo(c.foodAfter).Within(1e-3), label);
                Assert.That(state.Resources[1].Favor, Is.EqualTo(c.favorAfter).Within(1e-3), label);
                Assert.That(ai.Choice.SaveStart ?? -1, Is.EqualTo(c.saveStart).Within(1e-3), label);
            }
        }
        [TestCase(2)] [TestCase(11)]
        public void SeededMatchesWithGodsNeverOverdrawOrUnlockOffSchedule(int seed)
        {
            var state = StateWithAiGods("china");
            state.Ai = AiController.Create(state, data, "china", new System.Random(seed));
            var profile = state.Ai.Choice.Profile;
            var unlockedAt = new System.Collections.Generic.Dictionary<string, float>();
            var announced = new System.Collections.Generic.List<string>();
            state.Ai.GodUnlocked += announced.Add;
            var loop = new MatchLoop(new UnitUpdate(new System.Random(seed)));
            for (int frame = 0; frame < 4000 && !state.Objectives.Resolved; frame++)
            {
                loop.Tick(state, MatchLoop.MaxStep);
                Assert.That(state.Resources[1].Favor, Is.GreaterThanOrEqualTo(0f));
                foreach (var g in state.Gods[1].UnlockedSlots)
                    if (!unlockedAt.ContainsKey(g.Key)) { unlockedAt[g.Key] = state.Elapsed; Assert.That(announced, Does.Contain(g.Key)); }
            }
            Assert.That(unlockedAt, Is.Not.Empty, "a full match earns enough favor for at least one god");
            foreach (var kv in unlockedAt)
            {
                Assert.That(state.Gods[1].IsSelected(kv.Key), Is.True);
                Assert.That(kv.Value, Is.GreaterThanOrEqualTo(profile.GodDueAt(kv.Key).Value - 1e-3f), kv.Key);
            }
            Assert.That(state.Gods[0].UnlockedSlots, Is.Empty, "the AI never unlocks for the player");
            state.Reset();
            Assert.That(state.Gods[1].UnlockedSlots, Is.Empty);
        }

        [Test] public void WaveHoldsReleasesAndNeverRepeatsItsLastLane()
        {
            var state=State();var ai=Attach(state);var wave=ai.EnsureWave();
            for(int i=0;i<wave.Size;i++)
            {
                var u=state.Units.Spawn(1,state.Database.Unit("china","ji"),new Hex(i,7));u.Duty="attack";
                ai.RegisterSpawns(new[]{u});ai.PrepareUnit(u);Assert.That(u.Hold,Is.True);
            }
            int lane=wave.Lane;ai.TickWave();Assert.That(ai.Wave,Is.Null);
            foreach(var u in wave.Units){Assert.That(u.Hold,Is.False);Assert.That(u.MarchGoal,Is.Not.Null);Assert.That(u.HasRoute,Is.True);}
            Assert.That(ai.EnsureWave().Lane,Is.Not.EqualTo(lane));
        }
        [Test] public void WaveTimesOutAndSkipsDeadMembers()
        {
            var state=State();var ai=Attach(state);var wave=ai.EnsureWave();
            var u=state.Units.Spawn(1,state.Database.Unit("china","ji"),new Hex(4,6));u.Duty="attack";ai.RegisterSpawns(new[]{u});u.Kill();
            ai.TickWave();Assert.That(ai.Wave,Is.Not.Null);Assert.That(wave.Units,Is.Empty);
            state.Objectives.Clock.Advance(data.rules.waveTimeout+0.1f);ai.TickWave();Assert.That(ai.Wave,Is.Null);
        }
        [Test] public void HeldMemberAllowsFollowerToSettleWithoutSharingItsHex()
        {
            var state=State();var ai=Attach(state);var wave=ai.EnsureWave();
            var held=state.Units.Spawn(1,state.Database.Unit("china","ji"),new Hex(wave.Lane,7));held.Duty="attack";held.Hold=true;
            var follower=state.Units.Spawn(1,held.Def,new Hex(wave.Lane,6));follower.Duty="attack";follower.SetRoute(new[]{held.Hex});
            ai.RegisterSpawns(new[]{held,follower});ai.PrepareUnit(follower);
            Assert.That(follower.Hold,Is.True);Assert.That(follower.Free,Is.True);Assert.That(follower.Hex,Is.Not.EqualTo(held.Hex));
        }
        [Test] public void CrowdedExitStillSpawnsOnBuildingAndJoinsWave()
        {
            var state=State();state.Resources[1]=new Purse(120,100);var ai=Attach(state);ai.Tick(3.05f);
            var order=state.Training.Pending.Single();state.Units.Spawn(1,order.Def,order.ExitHex);
            var units=state.Training.Tick(order.ReadyAt,state.Units,state.Buildings);ai.RegisterSpawns(units);
            Assert.That(units.Single().Duty,Is.EqualTo("attack"));Assert.That(order.Source.Occupies(units.Single().Hex),Is.True);
            Assert.That(ai.Wave.Units,Does.Contain(units.Single()));
        }
        [TestCase(1)] [TestCase(9)] [TestCase(27)]
        public void SeededFullMatchesTrainMarchAndDamagePlayerBuildings(int seed)
        {
            var state=State();state.Ai=AiController.Create(state,data,"china",new System.Random(seed));
            new MatchLoop(new UnitUpdate(new System.Random(seed))).Run(state);
            Assert.That(state.Objectives.Resolved,Is.True);Assert.That(state.Buildings.DamageTaken(0),Is.GreaterThan(0));
            Assert.That(state.Ai.ReleasedWaves,Is.GreaterThan(0));
            foreach(var u in state.Units.All)Assert.That(Board.InBounds(u.Hex),Is.True);
            state.Reset();Assert.That(state.Ai.Wave,Is.Null);Assert.That(state.Ai.LastLane,Is.Null);
            Assert.That(state.Ai.Choice.SaveStart,Is.Null);Assert.That(state.Training.PendingCount,Is.Zero);
        }
        [Test] public void SceneEnablesAiAndPauseFreezesItsTraining()
        {
            var go=new GameObject("AiSceneTest");
            try
            {
                var controller=go.AddComponent<MatchController>();controller.Initialize();
                Assert.That(controller.State.Ai,Is.Not.Null);controller.Begin();
                for(int i=0;i<600;i++)controller.Advance(0.05f);
                var units=controller.State.Units.AliveOf(1).ToArray();Assert.That(units.Length,Is.GreaterThan(0));
                var view=go.GetComponent<UnitsView>();view.Sync();Assert.That(view.BodyOf(units[0].Id),Is.Not.Null);
                controller.SetPaused(true);float timer=controller.State.Ai.TrainRemaining;
                controller.Advance(0.05f);Assert.That(controller.State.Ai.TrainRemaining,Is.EqualTo(timer));
                controller.NewMatch();Assert.That(controller.State.Units.AliveCount,Is.Zero);Assert.That(controller.State.Ai.Wave,Is.Null);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
