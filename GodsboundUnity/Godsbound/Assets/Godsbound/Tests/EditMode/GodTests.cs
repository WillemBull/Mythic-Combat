using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.AI;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Gods;
using Godsbound.Core.Match;
using Godsbound.Core.Movement;
using Godsbound.Core.Training;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// U23: god rosters, the unlock transaction, hasPassive and the passive hooks, replayed from
    /// browser answers in <c>gods_reference.json</c> (<c>node tools/export_unity_gods.js</c>).
    /// </summary>
    public class GodTests
    {
        [Serializable] public class RosterCase { public string faction; public string[] keys, defaultPick; }
        [Serializable] public class UnlockStep
        {
            public string key; public float favorBefore, favorAfterPreview, favorAfter;
            public bool previewUnlocked, wasUnlocked;
            public string[] unlocked, myths, passives; public float[] maxHpRatio, hpRatio;
        }
        [Serializable] public class UnlockCase
        {
            public string faction, aiFaction, killed; public string[] pick, order, selected;
            public float startFavor; public bool refill, temple50; public UnlockStep[] steps;
        }
        [Serializable] public class SideCase
        {
            public string faction; public int side; public string[] unlocked, passives, otherSide;
            public float elapsed, stripUntil;
        }
        [Serializable] public class Cell { public int c, r; }
        [Serializable] public class EconomyCase
        {
            public string faction, god; public int side; public bool on;
            public string[] dead, selected; public Cell[] water; public float[] food, favor;
        }
        [Serializable] public class GodCost { public string key; public int unlock, power; }
        [Serializable] public class PriceCase
        {
            public string faction, god, unit; public int side; public bool on;
            public float food, favor, trainSeconds; public GodCost[] godCosts;
        }
        [Serializable] public class Fixture
        {
            public string[] passiveKeys; public RosterCase[] rosters; public UnlockCase[] unlockCases;
            public SideCase[] sidePassiveCases; public EconomyCase[] economyCases; public PriceCase[] priceCases;
        }

        private static readonly string[] Types = { "city", "temple", "fortress" };
        private GameDatabase db;
        private Fixture fx;

        [OneTimeSetUp] public void Load()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
            fx = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/gods_reference.json")));
        }

        private static string Other(string faction) => faction == "egypt" ? "china" : "egypt";

        private MatchState Match(string f0, IEnumerable<string> pick0, string f1, IEnumerable<string> pick1, float elapsed = 30f)
        {
            Func<int, string> faction = s => s == 0 ? f0 : f1;
            var gods = new[] { GodState.Create(0, db, f0, pick0), GodState.Create(1, db, f1, pick1) };
            var state = new MatchState(db, combatContext: new CombatContext { FactionForSide = faction },
                movementContext: new MovementContext { FactionForSide = faction }, gods: gods);
            state.Objectives.Clock.Advance(elapsed);
            return state;
        }

        private string[] TruePassives(MatchState state, int side) =>
            fx.passiveKeys.Where(k => state.HasPassive(side, k)).ToArray();

        private static Building Of(MatchState s, int side, string type) => s.Buildings.Of(side, Building.ParseType(type));

        // ---- rosters and the selected-four constraint --------------------------------

        [Test] public void RostersMatchTheBrowserPerFaction()
        {
            Assert.That(fx.rosters.Length, Is.EqualTo(db.Factions.Count));
            foreach (var r in fx.rosters)
            {
                Assert.That(db.Roster(r.faction).Select(g => g.key), Is.EqualTo(r.keys), r.faction);
                Assert.That(db.Faction(r.faction).defaultGodPick, Is.EqualTo(r.defaultPick), r.faction);
                Assert.That(GodState.Validate(db, r.faction, r.defaultPick), Is.Null, r.faction);
            }
        }

        [Test] public void IllegalPicksAreRejectedWithAReason()
        {
            var eg = db.Faction("egypt").defaultGodPick;
            Assert.That(GodState.Validate(db, "egypt", eg.Take(3)), Does.Contain("exactly 4"));
            Assert.That(GodState.Validate(db, "egypt", eg.Concat(new[] { "ra" })), Does.Contain("exactly 4"));
            Assert.That(GodState.Validate(db, "egypt", new[] { eg[0], eg[0], eg[1], eg[2] }), Does.Contain("twice"));
            Assert.That(GodState.Validate(db, "egypt", new[] { eg[0], eg[1], eg[2], "jade" }), Does.Contain("not a god of egypt"));
            Assert.That(GodState.Validate(db, "atlantis", eg), Does.Contain("unknown faction"));
            Assert.Throws<ArgumentException>(() => GodState.Create(0, db, "egypt", eg.Take(3)));
        }

        [Test] public void SelectedGodsAreInRosterOrderNotPickOrder()
        {
            foreach (var r in fx.rosters)
            {
                var reversed = r.defaultPick.Reverse().ToArray();
                var gods = GodState.Create(0, db, r.faction, reversed);
                Assert.That(gods.Selected.Select(s => s.Key), Is.EqualTo(r.keys.Where(reversed.Contains)), r.faction);
            }
        }

        // ---- the real two-tap unlock flow --------------------------------------------

        [Test] public void BrowserUnlockSequencesReplayStepForStep()
        {
            Assert.That(fx.unlockCases.Length, Is.GreaterThan(0));
            foreach (var c in fx.unlockCases)
            {
                var state = Match(c.faction, c.pick, c.aiFaction, db.Faction(c.aiFaction).defaultGodPick);
                string label0 = $"{c.faction} [{string.Join(",", c.order)}] favor {c.startFavor}{(c.refill ? " refill" : "")}";
                Assert.That(state.Gods[0].Selected.Select(s => s.Key), Is.EqualTo(c.selected), label0);
                if (!string.IsNullOrEmpty(c.killed)) { var k = Of(state, 0, c.killed); k.TakeDamage(k.Hp); }
                if (c.temple50) { var t = Of(state, 0, "temple"); t.TakeDamage(t.MaxHp * 0.5f); }
                var baseMax = Types.Select(t => Of(state, 0, t).MaxHp).ToArray();
                var baseHp = Types.Select(t => Of(state, 0, t).Hp).ToArray();
                state.Resources[0] = new Purse(0f, c.startFavor);
                var flow = new GodTapFlow(0);
                foreach (var s in c.steps)
                {
                    string label = label0 + " step " + s.key;
                    if (c.refill) state.Resources[0] = new Purse(0f, c.startFavor);
                    Assert.That(state.Resources[0].Favor, Is.EqualTo(s.favorBefore).Within(1e-3), label);
                    Assert.That(state.Gods[0].IsUnlocked(s.key), Is.EqualTo(s.wasUnlocked), label);
                    flow.Tap(state, s.key);
                    Assert.That(state.Resources[0].Favor, Is.EqualTo(s.favorAfterPreview).Within(1e-3), label + " (preview pays nothing)");
                    Assert.That(state.Gods[0].IsUnlocked(s.key), Is.EqualTo(s.previewUnlocked), label);
                    flow.Tap(state, s.key);
                    Assert.That(state.Resources[0].Favor, Is.EqualTo(s.favorAfter).Within(1e-3), label);
                    Assert.That(state.Gods[0].UnlockedSlots.Select(g => g.Key), Is.EqualTo(s.unlocked), label);
                    Assert.That(state.Gods[0].MythDeck, Is.EqualTo(s.myths), label);
                    Assert.That(TruePassives(state, 0), Is.EqualTo(s.passives), label);
                    for (int i = 0; i < Types.Length; i++)
                    {
                        var b = Of(state, 0, Types[i]);
                        Assert.That(b.MaxHp / baseMax[i], Is.EqualTo(s.maxHpRatio[i]).Within(1e-4), label + " maxhp " + Types[i]);
                        float hpRatio = baseHp[i] > 0f ? b.Hp / baseHp[i] : 0f;
                        Assert.That(hpRatio, Is.EqualTo(s.hpRatio[i]).Within(1e-4), label + " hp " + Types[i]);
                    }
                }
            }
        }

        [Test] public void ResetLocksEverythingAndRestoresBuildings()
        {
            var state = Match("egypt", new[] { "ptah", "thoth", "ra", "set" }, "china", db.Faction("china").defaultGodPick);
            state.Resources[0] = new Purse(0f, 250f);
            Assert.That(state.TryUnlockGod(0, "ptah"), Is.EqualTo(UnlockOutcome.Unlocked));
            Assert.That(state.Buildings.Of(0).All(b => b.MaxHp > b.BaseMaxHp), Is.True, "Ptah boosted every standing building");
            state.Gods[0].PassivesStrippedUntil = 99f;
            state.Gods[0].Selected[0].CooldownUntil = 50f;
            state.Reset();
            Assert.That(state.Gods[0].UnlockedSlots, Is.Empty);
            Assert.That(state.Gods[0].MythDeck, Is.Empty);
            Assert.That(state.Gods[0].PassivesStrippedUntil, Is.EqualTo(0f));
            Assert.That(state.Gods[0].Selected.All(s => s.CooldownUntil == 0f && s.LockedUntil == 0f), Is.True);
            Assert.That(state.Buildings.Of(0).All(b => b.MaxHp == b.BaseMaxHp && b.Hp == b.BaseMaxHp), Is.True,
                "unlike the browser, a boost never compounds into the next match");
        }

        [Test] public void UnlockOutcomesNeverOverdraw()
        {
            var state = Match("egypt", db.Faction("egypt").defaultGodPick, "china", db.Faction("china").defaultGodPick);
            var horus = db.God("egypt", "horus");
            state.Resources[0] = new Purse(7f, horus.cost - 1);
            Assert.That(state.TryUnlockGod(0, "horus"), Is.EqualTo(UnlockOutcome.CannotAfford));
            Assert.That(state.Resources[0].Favor, Is.EqualTo(horus.cost - 1));
            Assert.That(state.TryUnlockGod(0, "ra"), Is.EqualTo(UnlockOutcome.NotSelected));
            Assert.That(state.TryUnlockGod(0, "jade"), Is.EqualTo(UnlockOutcome.NotSelected));
            state.Resources[0] = new Purse(7f, horus.cost);
            Assert.That(state.TryUnlockGod(0, "horus"), Is.EqualTo(UnlockOutcome.Unlocked));
            Assert.That(state.Resources[0].Favor, Is.EqualTo(0f));
            Assert.That(state.Resources[0].Food, Is.EqualTo(7f), "unlocking costs favor only");
            Assert.That(state.TryUnlockGod(0, "horus"), Is.EqualTo(UnlockOutcome.AlreadyUnlocked));
            Assert.That(state.Gods[1].UnlockedSlots, Is.Empty, "a player unlock never touches the AI side");
        }

        [Test] public void JsRoundMatchesMathRoundHalvesUp()
        {
            Assert.That(GodRules.JsRound(2.5), Is.EqualTo(3));
            Assert.That(GodRules.JsRound(3.5), Is.EqualTo(4));
            Assert.That(GodRules.JsRound(-0.5), Is.EqualTo(0));
            Assert.That(GodRules.JsRound(63.9999), Is.EqualTo(64));
        }

        // ---- hasPassive ----------------------------------------------------------------

        [Test] public void HasPassiveMatchesTheBrowserOnBothSidesIncludingTheFlaying()
        {
            Assert.That(fx.sidePassiveCases.Length, Is.GreaterThan(0));
            foreach (var c in fx.sidePassiveCases)
            {
                string other = Other(c.faction);
                var pick = db.Faction(c.faction).defaultGodPick;
                var otherPick = db.Faction(other).defaultGodPick;
                var state = c.side == 0 ? Match(c.faction, pick, other, otherPick, c.elapsed)
                                        : Match(other, otherPick, c.faction, pick, c.elapsed);
                foreach (var k in c.unlocked) Assert.That(state.Gods[c.side].Grant(k), Is.True, k);
                state.Gods[c.side].PassivesStrippedUntil = c.stripUntil;
                string label = $"{c.faction} side {c.side} [{string.Join(",", c.unlocked)}] strip {c.stripUntil}";
                Assert.That(TruePassives(state, c.side), Is.EqualTo(c.passives), label);
                Assert.That(TruePassives(state, 1 - c.side), Is.EqualTo(c.otherSide), label);
            }
        }

        [Test] public void EveryPassiveHookReadsTheSameAnswer()
        {
            var state = Match("greek", new[] { "hephaestus", "poseidon", "ares", "hermes" }, "china", db.Faction("china").defaultGodPick);
            state.Gods[0].Grant("hephaestus"); state.Gods[0].Grant("ares");
            foreach (var key in new[] { PassiveKeys.Automata, PassiveKeys.Warcry, PassiveKeys.Earthshaker, PassiveKeys.Messenger })
            {
                bool expected = state.HasPassive(0, key);
                Assert.That(state.UnitContext.HasPassive(0, key), Is.EqualTo(expected), key);
                Assert.That(GodRules.HasPassive(state.Gods[0], key, state.Elapsed), Is.EqualTo(expected), key);
            }
            Assert.That(state.HasPassive(0, PassiveKeys.Automata), Is.True);
            Assert.That(state.HasPassive(0, PassiveKeys.Earthshaker), Is.False, "Poseidon is selected but locked");
            Assert.That(state.HasPassive(1, PassiveKeys.Automata), Is.False, "passives never leak across sides");
            Assert.That(state.TrainingModifiersFor(0).Messenger, Is.False);
            state.Gods[0].Grant("hermes");
            Assert.That(state.TrainingModifiersFor(0).Messenger, Is.True);
        }

        [Test] public void AMatchWithoutGodsKeepsCallerSuppliedPassiveHooks()
        {
            var ctx = new CombatContext { HasPassive = (s, k) => k == "inevitable" };
            var state = new MatchState(db, combatContext: ctx);
            Assert.That(state.Gods[0].Selected, Is.Empty);
            Assert.That(state.HasPassive(0, "inevitable"), Is.True);
            Assert.That(state.TryUnlockGod(0, "horus"), Is.EqualTo(UnlockOutcome.NotSelected));
        }

        // ---- passive-driven economy and pricing ---------------------------------------

        [Test] public void EconomyWithEachEconomicPassiveMatchesTheBrowser()
        {
            Assert.That(fx.economyCases.Length, Is.GreaterThan(0));
            foreach (var c in fx.economyCases)
            {
                string other = Other(c.faction);
                var otherPick = db.Faction(other).defaultGodPick;
                var state = c.side == 0 ? Match(c.faction, c.selected, other, otherPick)
                                        : Match(other, otherPick, c.faction, c.selected);
                foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
                foreach (var w in c.water) state.Terrain[w.c, w.r] = TerrainType.Water;
                for (int side = 0; side < 2; side++)
                    foreach (var t in c.dead) { var b = Of(state, side, t); b.TakeDamage(b.Hp); }
                if (c.on) Assert.That(state.Gods[c.side].Grant(c.god), Is.True, c.god);
                string label = $"{c.god} side {c.side} on={c.on} dead=[{string.Join(",", c.dead)}]";
                for (int side = 0; side < 2; side++)
                {
                    var p = EconomyTick.Advance(Purse.Empty, (EconomySide)side, state.ConditionsFor(side), db.Economy, 1f);
                    Assert.That(p.Food, Is.EqualTo(c.food[side]).Within(1e-3), label + " food side " + side);
                    Assert.That(p.Favor, Is.EqualTo(c.favor[side]).Within(1e-3), label + " favor side " + side);
                }
            }
        }

        [Test] public void LongWangCountsOnlyTheOwnHalf()
        {
            var state = Match("china", db.Faction("china").defaultGodPick, "egypt", db.Faction("egypt").defaultGodPick);
            foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
            state.Terrain[0, Board.PlayerRow0 - 1] = TerrainType.Water;
            Assert.That(state.WaterTilesOnOwnHalf(0), Is.EqualTo(0), "no-man's land pays nobody");
            Assert.That(state.WaterTilesOnOwnHalf(1), Is.EqualTo(0));
            state.Terrain[0, Board.Rows - 1] = TerrainType.Water;
            state.Terrain[0, 0] = TerrainType.Water;
            Assert.That(state.WaterTilesOnOwnHalf(0), Is.EqualTo(1), "the back row counts");
            Assert.That(state.WaterTilesOnOwnHalf(1), Is.EqualTo(1));
            Assert.That(state.ConditionsFor(0).WaterFavorPerSecond, Is.EqualTo(0f), "locked Long Wang pays nothing");
            state.Gods[0].Grant("longwang");
            Assert.That(state.ConditionsFor(0).WaterFavorPerSecond, Is.EqualTo(db.GodRates.waterFavorPerTile).Within(1e-6));
        }

        [Test] public void PricesTrainTimesAndGodCostsMatchTheBrowser()
        {
            Assert.That(fx.priceCases.Length, Is.GreaterThan(0));
            foreach (var c in fx.priceCases)
            {
                string other = Other(c.faction);
                var otherPick = db.Faction(other).defaultGodPick;
                var pick = new[] { c.god }.Concat(db.Faction(c.faction).defaultGodPick.Where(k => k != c.god).Take(3)).ToArray();
                var state = c.side == 0 ? Match(c.faction, pick, other, otherPick) : Match(other, otherPick, c.faction, pick);
                if (c.on) state.Gods[c.side].Grant(c.god);
                var mods = state.TrainingModifiersFor(c.side);
                var def = db.Unit(c.faction, c.unit);
                var price = UnitPrice.For(def, mods, db.Training);
                string label = $"{c.faction}/{c.unit} side {c.side} {c.god}={c.on}";
                Assert.That(price.Food, Is.EqualTo(c.food).Within(1e-3), label);
                Assert.That(price.Favor, Is.EqualTo(c.favor).Within(1e-3), label);
                Assert.That(UnitPrice.TrainSeconds(def, mods, db.Training), Is.EqualTo(c.trainSeconds).Within(1e-3), label);
                Assert.That(state.Gods[c.side].Selected.Select(s => s.Key), Is.EqualTo(c.godCosts.Select(g => g.key)), label);
                foreach (var g in c.godCosts)
                {
                    var god = db.God(c.faction, g.key);
                    Assert.That(state.GodUnlockCost(c.side, god), Is.EqualTo(g.unlock), label + " unlock " + g.key);
                    Assert.That(state.GodPowerCost(c.side, god), Is.EqualTo(g.power), label + " power " + g.key);
                }
            }
        }

        // ---- myth access ----------------------------------------------------------------

        [Test] public void UnlockingFillsTheMythPoolsForPlayerAndAi()
        {
            var state = Match("egypt", db.Faction("egypt").defaultGodPick, "china", db.Faction("china").defaultGodPick);
            var ai = AiController.Create(state, AiDataLoader.Load(), "china", new System.Random(3));
            Assert.That(ai.Choice.UnlockedMyths(), Is.Empty);
            var china = state.Gods[1].Selected;
            state.Gods[1].Grant(china[2].Key); state.Gods[1].Grant(china[0].Key);
            Assert.That(ai.Choice.UnlockedMyths(), Is.EqualTo(new[] { china[0].Def.myth, china[2].Def.myth }),
                "the AI reads myths in roster order");
            state.Resources[0] = new Purse(0f, 1000f); // enough for any two gods; the cap is the economy's, not the purse's
            var eg = state.Gods[0].Selected;
            Assert.That(state.TryUnlockGod(0, eg[3].Key), Is.EqualTo(UnlockOutcome.Unlocked));
            Assert.That(state.TryUnlockGod(0, eg[0].Key), Is.EqualTo(UnlockOutcome.Unlocked));
            Assert.That(state.Gods[0].MythDeck, Is.EqualTo(new[] { eg[3].Def.myth, eg[0].Def.myth }),
                "the player's deck gains myths in unlock order");
            foreach (var m in state.Gods[0].MythDeck)
                Assert.That(db.Unit("egypt", m).cat, Is.EqualTo("myth"), m);
            Assert.Throws<ArgumentException>(() => AiController.Create(state, AiDataLoader.Load(), "egypt"));
        }
    }
}
