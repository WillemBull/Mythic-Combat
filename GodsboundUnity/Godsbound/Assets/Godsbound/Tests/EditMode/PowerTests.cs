using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Gods;
using Godsbound.Core.Gods.Powers;
using Godsbound.Core.Match;
using Godsbound.Core.Movement;
using Godsbound.Core.Training;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// U26: the power framework, Egypt's eleven powers and the god-effect tick, replayed against
    /// browser casts in <c>powers_reference.json</c> (<c>node tools/export_unity_powers.js</c>).
    /// </summary>
    public class PowerTests
    {
        [Serializable] public class Cell { public int c, r; public Hex Hex => new Hex(c, r); }
        [Serializable] public class BuildingRow { public int side, c0, r0, c1, r1; public string type; public float maxhp, hp; public bool dead; }
        [Serializable] public class UnitIn
        {
            public int side, c, r, pathIdx, conscriptSide; public string key; public float hp, invuln, poisonUntil, poisonDps, despawn, conscriptUntil;
            public bool invisible;
            public Cell[] path;
        }
        [Serializable] public class UnitOut
        {
            public int side, c, r, routeIndex; public string key; public float hp, slowUntil, frenzyUntil, invulnUntil, despawnAt, lastStandEndsAt;
            public float maxhp, stunUntil, conscriptedUntil;
            public float aegisUntil, bloodlustUntil, buffUntil, buffDmg, buffSpd, weakenUntil, rootUntil;
            public bool dead, free, hasRevived, retreating, invisible, transformed, flying; public Cell[] route;
        }
        [Serializable] public class HuntRow { public int unit; public float until; }
        [Serializable] public class TrainRow { public int side, spawnCount, c, r; public string key; public float readyAt; }
        [Serializable] public class GodRow { public string key; public bool unlocked; public float cd, locked; }
        [Serializable] public class FloodRow { public int c, r, owner; public string orig, to; public float dps, expires; }
        [Serializable] public class DeadRow { public int side; public string key; public float at; }
        [Serializable] public class Snapshot
        {
            public UnitOut[] units; public float[] favor, food, bldInvuln, bldHp, trueSight, chaos; public GodRow[] playerGods, aiGods;
            public float veilUntil; public bool[] ptah; public DeadRow[] deadHumans;
            public string[] terrain; public FloodRow[] floods;
            public HuntRow[] hunt; public float[] blight, bldDmg, stripped; public int[] forge;
            public TrainRow[] training;
        }
        [Serializable] public class Before
        {
            public BuildingRow[] buildings; public UnitIn[] units; public string[] playerPick, aiPick, terrain; public Snapshot state;
        }
        [Serializable] public class CastCase
        {
            public string label, config, player, ai, god, pickedGod; public int caster;
            public Cell hex; public float time, random, dt; public bool attempted, applied, viaPlayerCast;
            public Before before; public Snapshot after; public Snapshot[] ticks;
        }
        [Serializable] public class GodOn { public int side; public string key; }
        [Serializable] public class EffectCase
        {
            public string label, player, ai; public float time, dt; public GodOn[] gods; public Before before; public Snapshot[] ticks;
        }
        [Serializable] public class TrainedRow { public string key; public float maxhp, hp; public bool forged; }
        [Serializable] public class QueuedRow { public string key; public int spawnCount; }
        [Serializable] public class TrainingCase
        {
            public int forge; public bool ptah; public string[] keys; public QueuedRow[] queued;
            public int forgeAfter; public bool ptahAfter; public TrainedRow[] units;
        }
        [Serializable] public class Fixture { public CastCase[] casts; public EffectCase[] effects; public TrainingCase[] training; }

        private GameDatabase db;
        private Fixture fx;

        [OneTimeSetUp] public void Load()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
            fx = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/powers_reference.json")));
        }

        private MatchState Build(string player, string ai, Before b, float time)
        {
            var map = new BuildingMap(b.buildings.Select(r => new Building(r.side, Building.ParseType(r.type), r.type,
                new Hex(r.c0, r.r0), new Hex(r.c1, r.r1), r.maxhp)));
            Func<int, string> faction = s => s == 0 ? player : ai;
            var gods = new[] { GodState.Create(0, db, player, b.playerPick), GodState.Create(1, db, ai, b.aiPick) };
            var state = new MatchState(db, buildings: map, combatContext: new CombatContext { FactionForSide = faction },
                movementContext: new MovementContext { FactionForSide = faction }, gods: gods);
            for (int r = 0; r < Board.Rows; r++)
                for (int col = 0; col < Board.Cols; col++)
                    state.Terrain[col, r] = TerrainTable.FromCode(b.terrain[r][col]);
            for (int i = 0; i < b.buildings.Length; i++)
            {
                var row = b.buildings[i]; var bld = map.All[i];
                if (row.dead || row.hp <= 0f) bld.TakeDamage(bld.Hp);
                else if (row.hp < bld.Hp) bld.TakeDamage(bld.Hp - row.hp);
            }
            state.Objectives.Clock.Advance(time);
            foreach (var u in b.units)
            {
                var def = db.ResolveUnit(faction(u.side), u.key);
                Assert.That(def, Is.Not.Null, u.key);
                var body = state.Units.Spawn(u.side, def, new Hex(u.c, u.r));
                if (u.hp < body.Hp) body.TakeDamage(body.Hp - u.hp);
                if (u.path != null && u.path.Length > 0) body.SetRoute(u.path.Select(p => p.Hex), u.pathIdx);
                body.Combat.InvulnerableUntil = u.invuln;
                body.Combat.PoisonUntil = u.poisonUntil;
                body.Combat.PoisonDps = u.poisonDps;
                body.DespawnAt = u.despawn;
                body.Combat.Invisible = u.invisible;
                if (u.conscriptSide >= 0) body.Conscript(u.conscriptSide, u.conscriptUntil);
            }
            return state;
        }

        private static void ApplyState(MatchState state, Snapshot s)
        {
            for (int side = 0; side < 2; side++) state.Resources[side] = new Purse(s.food[side], s.favor[side]);
            foreach (var pair in new[] { (0, s.playerGods), (1, s.aiGods) })
                foreach (var g in pair.Item2)
                {
                    var slot = state.Gods[pair.Item1].Slot(g.key);
                    if (g.unlocked) state.Gods[pair.Item1].Grant(g.key);
                    slot.CooldownUntil = g.cd;
                    slot.LockedUntil = g.locked;
                }
            for (int side = 0; side < 2; side++)
            {
                state.UnitContext.RevealedUntil[side] = s.trueSight[side];
                state.Powers.PtahCharge[side] = s.ptah[side];
                state.Powers.ChaosBuffUntil[side] = s.chaos[side];
            }
            state.UnitContext.VeilUntil = s.veilUntil;
            var blds = state.Buildings.All;
            for (int i = 0; i < blds.Count && i < s.bldInvuln.Length; i++) blds[i].InvulnerableUntil = s.bldInvuln[i];
            for (int side = 0; side < 2; side++)
            {
                state.Powers.BlightUntil[side] = s.blight[side];
                state.Powers.Forge[side] = s.forge[side];
                state.Gods[side].PassivesStrippedUntil = s.stripped[side];
                var h = s.hunt[side];
                state.Powers.Hunt[side] = h.unit < 0 ? null
                    : new HuntMark { Unit = state.Units.All[h.unit], Until = h.until };
            }
            foreach (var d in s.deadHumans) state.Combat.RememberDeath(new HumanDeath(d.key, d.side, new Hex(0, 0), d.at));
        }

        private static void AssertState(MatchState state, Snapshot want, string label, bool full)
        {
            var units = state.Units.All;
            Assert.That(units.Count, Is.EqualTo(want.units.Length), label + " unit count");
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i]; var w = want.units[i]; string l = $"{label} unit {i} {w.key}";
                Assert.That(u.Key, Is.EqualTo(w.key), l);
                Assert.That(u.Side, Is.EqualTo(w.side), l);
                Assert.That(u.Hex, Is.EqualTo(new Hex(w.c, w.r)), l);
                Assert.That(u.Dead, Is.EqualTo(w.dead), l + " dead");
                if (!w.dead) Assert.That(u.Hp, Is.EqualTo(w.hp).Within(1e-3), l + " hp");
                if (!full) { Assert.That(u.SlowUntil, Is.EqualTo(w.slowUntil).Within(1e-3), l + " slow"); continue; }
                Assert.That(u.SlowUntil, Is.EqualTo(w.slowUntil).Within(1e-3), l + " slow");
                Assert.That(u.Combat.FrenzyUntil, Is.EqualTo(w.frenzyUntil).Within(1e-3), l + " frenzy");
                Assert.That(u.Combat.InvulnerableUntil, Is.EqualTo(w.invulnUntil).Within(1e-3), l + " invuln");
                Assert.That(u.DespawnAt, Is.EqualTo(w.despawnAt).Within(1e-3), l + " despawn");
                Assert.That(u.Combat.HasRevived, Is.EqualTo(w.hasRevived), l + " revived");
                Assert.That(u.Combat.Retreating, Is.EqualTo(w.retreating), l + " retreating");
                Assert.That(u.Combat.LastStandEndsAt, Is.EqualTo(w.lastStandEndsAt).Within(1e-3), l + " last stand");
                Assert.That(u.Free, Is.EqualTo(w.free), l + " free");
                Assert.That((u.Route ?? Array.Empty<Hex>()).ToArray(), Is.EqualTo(w.route.Select(h => h.Hex).ToArray()), l + " route");
                Assert.That(u.RouteIndex, Is.EqualTo(w.routeIndex), l + " route index");
                Assert.That(u.MaxHp, Is.EqualTo(w.maxhp).Within(1e-3), l + " max hp");
                Assert.That(u.StunUntil, Is.EqualTo(w.stunUntil).Within(1e-3), l + " stun");
                Assert.That(u.Combat.Invisible, Is.EqualTo(w.invisible), l + " invisible");
                Assert.That(u.ConscriptedUntil, Is.EqualTo(w.conscriptedUntil).Within(1e-3), l + " conscripted");
                Assert.That(u.Transformed, Is.EqualTo(w.transformed), l + " transformed");
                Assert.That(u.Flying, Is.EqualTo(w.flying), l + " flying");
                Assert.That(u.Combat.AegisUntil, Is.EqualTo(w.aegisUntil).Within(1e-3), l + " aegis");
                Assert.That(u.Combat.BloodlustUntil, Is.EqualTo(w.bloodlustUntil).Within(1e-3), l + " bloodlust");
                Assert.That(u.BuffUntil, Is.EqualTo(w.buffUntil).Within(1e-3), l + " buff");
                Assert.That(u.Combat.BuffDamage, Is.EqualTo(w.buffDmg).Within(1e-3), l + " buff damage");
                Assert.That(u.BuffSpeed, Is.EqualTo(w.buffSpd).Within(1e-3), l + " buff speed");
                Assert.That(u.Combat.WeakenUntil, Is.EqualTo(w.weakenUntil).Within(1e-3), l + " weaken");
                Assert.That(u.RootUntil, Is.EqualTo(w.rootUntil).Within(1e-3), l + " root");
            }
            for (int side = 0; side < 2; side++)
            {
                Assert.That(state.Resources[side].Favor, Is.EqualTo(want.favor[side]).Within(1e-3), label + " favor " + side);
                Assert.That(state.Resources[side].Food, Is.EqualTo(want.food[side]).Within(1e-3), label + " food " + side);
            }
            AssertTerrain(state, want, label);
            if (!full) return;
            var blds = state.Buildings.All;
            for (int i = 0; i < blds.Count; i++)
                if (!blds[i].Dead && i < want.bldHp.Length)
                    Assert.That(blds[i].Hp, Is.EqualTo(want.bldHp[i]).Within(1e-3), label + " building hp " + i);
            for (int i = 0; i < blds.Count; i++)
                Assert.That(blds[i].InvulnerableUntil, Is.EqualTo(want.bldInvuln[i]).Within(1e-3), label + " building " + i);
            foreach (var pair in new[] { (0, want.playerGods), (1, want.aiGods) })
                foreach (var g in pair.Item2)
                {
                    var slot = state.Gods[pair.Item1].Slot(g.key);
                    Assert.That(slot.Unlocked, Is.EqualTo(g.unlocked), $"{label} {g.key} unlocked");
                    Assert.That(slot.CooldownUntil, Is.EqualTo(g.cd).Within(1e-3), $"{label} {g.key} cooldown");
                    Assert.That(slot.LockedUntil, Is.EqualTo(g.locked).Within(1e-3), $"{label} {g.key} locked");
                }
            Assert.That(state.UnitContext.VeilUntil, Is.EqualTo(want.veilUntil).Within(1e-3), label + " veil");
            for (int side = 0; side < 2; side++)
            {
                Assert.That(state.Powers.PtahCharge[side], Is.EqualTo(want.ptah[side]), label + " ptah " + side);
                Assert.That(state.Powers.ChaosBuffUntil[side], Is.EqualTo(want.chaos[side]).Within(1e-3), label + " chaos " + side);
                Assert.That(state.Powers.BlightUntil[side], Is.EqualTo(want.blight[side]).Within(1e-3), label + " blight " + side);
                Assert.That(state.Powers.Forge[side], Is.EqualTo(want.forge[side]), label + " forge " + side);
                Assert.That(state.Combat.BuildingDamage[side], Is.EqualTo(want.bldDmg[side]).Within(1e-3), label + " building damage " + side);
                var mark = state.Powers.Hunt[side];
                Assert.That(mark == null ? -1 : units.ToList().IndexOf(mark.Unit),
                    Is.EqualTo(want.hunt[side].unit), label + " quarry " + side);
                Assert.That(mark == null ? 0f : mark.Until, Is.EqualTo(want.hunt[side].until).Within(1e-3), label + " quarry until " + side);
                Assert.That(state.Gods[side].PassivesStrippedUntil, Is.EqualTo(want.stripped[side]).Within(1e-3),
                    label + " passives stripped " + side);
            }
            var queue = state.Training.Pending;
            Assert.That(queue.Count, Is.EqualTo(want.training.Length), label + " training count");
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i]; var w = want.training[i]; string l = $"{label} order {i} {w.key}";
                Assert.That(o.Def.key, Is.EqualTo(w.key), l);
                Assert.That(o.Side, Is.EqualTo(w.side), l + " side");
                Assert.That(o.ReadyAt, Is.EqualTo(w.readyAt).Within(1e-3), l + " ready at");
                Assert.That(o.SpawnCount, Is.EqualTo(w.spawnCount), l + " spawn count");
                Assert.That(o.ExitHex, Is.EqualTo(new Hex(w.c, w.r)), l + " exit");
            }
            Assert.That(state.Combat.DeadHumans.Select(d => d.Key + "@" + d.Side).ToArray(),
                Is.EqualTo(want.deadHumans.Select(d => d.key + "@" + d.side).ToArray()), label + " dead humans");
        }

        private static void AssertTerrain(MatchState state, Snapshot want, string label)
        {
            for (int r = 0; r < Board.Rows; r++)
            {
                var row = new char[Board.Cols];
                for (int c = 0; c < Board.Cols; c++) row[c] = state.Terrain.CodeAt(c, r);
                Assert.That(new string(row), Is.EqualTo(want.terrain[r]), $"{label} terrain row {r}");
            }
            var overlays = state.Terrain.Overlays;
            Assert.That(overlays.Count, Is.EqualTo(want.floods.Length), label + " overlay count");
            for (int i = 0; i < overlays.Count; i++)
            {
                var o = overlays[i]; var w = want.floods[i]; string l = $"{label} overlay {i}";
                Assert.That(o.Hex, Is.EqualTo(new Hex(w.c, w.r)), l);
                Assert.That(TerrainTable.ToCode(o.Original).ToString(), Is.EqualTo(w.orig), l + " original");
                Assert.That(TerrainTable.ToCode(o.To).ToString(), Is.EqualTo(w.to), l + " to");
                Assert.That(o.Owner, Is.EqualTo(w.owner), l + " owner");
                Assert.That(o.DamagePerSecond, Is.EqualTo(w.dps).Within(1e-4), l + " dps");
                Assert.That(o.ExpiresAt, Is.EqualTo(w.expires).Within(1e-3), l + " expires");
            }
        }

        [Test] public void PowerRatesWereExported()
        {
            var p = db.Powers;
            Assert.That(p.raAspects.Length, Is.EqualTo(3));
            Assert.That(p.ptahExtraKey, Is.Not.Empty);
            foreach (var key in new[] { "horusDamage", "bastetWard", "osirisWindow", "osirisWindowAnubis", "osirisCount",
                "osirisDespawn", "isisLock", "anubisRadius", "setRadius", "setSlow", "thothRadius", "sekhmetRadius",
                "sekhmetFrenzy", "nephthysVeil", "chaosWindow", "lyreRange", "lyreHeal", "heroRegen", "auraRange", "judgmentBase" })
                Assert.That(p.Value(key), Is.GreaterThan(0f), key);
            Assert.Throws<InvalidOperationException>(() => p.Value("nope"));
        }

        [Test] public void EveryGodOfEveryFactionHasAPortedPower()
        {
            var state = new MatchState(db, gods: new[] { GodState.None(0), GodState.None(1) });
            foreach (var f in new[] { "egypt", "china", "greek", "aztec" })
                foreach (var g in db.Roster(f)) Assert.That(state.Powers.IsPorted(g.key), Is.True, g.key);
            Assert.That(db.AllGods.Count, Is.EqualTo(db.Factions.Sum(f => f.allGodKeys.Count)), "no god is unreachable");
        }

        [Test] public void BrowserCastsReplayCaseForCase()
        {
            Assert.That(fx.casts.Length, Is.GreaterThan(20));
            foreach (var c in fx.casts)
            {
                var state = Build(c.player, c.ai, c.before, c.time);
                ApplyState(state, c.before.state);
                AssertState(state, c.before.state, c.label + " (setup)", true);
                state.Powers.Random = () => c.random;
                bool applied; bool attempted;
                if (c.viaPlayerCast)
                {
                    var result = state.Powers.TryCast(0, c.god, c.hex.Hex, string.IsNullOrEmpty(c.pickedGod) ? null : c.pickedGod);
                    attempted = result.Paid; applied = result.Applied;
                }
                else
                {
                    attempted = true;
                    applied = state.Powers.Apply(c.caster, db.God(c.caster == 0 ? c.player : c.ai, c.god), c.hex.Hex,
                        string.IsNullOrEmpty(c.pickedGod) ? null : c.pickedGod);
                }
                Assert.That(attempted, Is.EqualTo(c.attempted), c.label + " attempted");
                Assert.That(applied, Is.EqualTo(c.applied), c.label + " applied");
                AssertState(state, c.after, c.label + " (after cast)", true);
                for (int i = 0; i < c.ticks.Length; i++)
                {
                    state.Objectives.Clock.Advance(c.dt);
                    state.GodEffects.Tick(c.dt, state.Elapsed);
                    AssertState(state, c.ticks[i], $"{c.label} (tick {i})", false);
                }
            }
        }

        [Test] public void BrowserEffectTimelinesReplayTickForTick()
        {
            Assert.That(fx.effects.Length, Is.GreaterThan(5));
            foreach (var e in fx.effects)
            {
                var state = Build(e.player, e.ai, e.before, e.time);
                foreach (var g in e.gods) Assert.That(state.Gods[g.side].Grant(g.key), Is.True, e.label + " " + g.key);
                for (int i = 0; i < e.ticks.Length; i++)
                {
                    state.Objectives.Clock.Advance(e.dt);
                    state.GodEffects.Tick(e.dt, state.Elapsed);
                    AssertState(state, e.ticks[i], $"{e.label} (tick {i})", false);
                }
            }
        }

        private MatchState Egypt(params string[] pick)
        {
            var state = new MatchState(db, gods: new[] { GodState.Create(0, db, "egypt", pick), GodState.Default(1, db, "china") },
                combatContext: new CombatContext { FactionForSide = s => s == 0 ? "egypt" : "china" });
            state.Objectives.Clock.Advance(30f);
            return state;
        }

        [Test] public void FrameworkNeverPaysForARefusedCast()
        {
            var state = Egypt("horus", "bastet", "osiris", "isis");
            state.Resources[0] = new Purse(0f, 250f);
            Assert.That(state.Powers.TryCast(0, "ra", new Hex(4, 4)).Check, Is.EqualTo(PowerCheck.NotSelected));
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Check, Is.EqualTo(PowerCheck.NotUnlocked));
            state.Gods[0].Grant("horus");
            var slot = state.Gods[0].Slot("horus");
            slot.CooldownUntil = 31f;
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Check, Is.EqualTo(PowerCheck.Recharging));
            slot.CooldownUntil = 0f; slot.LockedUntil = 31f;
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Check, Is.EqualTo(PowerCheck.PowerLocked));
            slot.LockedUntil = 0f;
            state.Resources[0] = new Purse(0f, state.GodPowerCost(0, slot.Def) - 1);
            Assert.That(state.Powers.TryCast(0, "horus", new Hex(4, 4)).Check, Is.EqualTo(PowerCheck.CannotAfford));
            Assert.That(state.Resources[0].Favor, Is.EqualTo(state.GodPowerCost(0, slot.Def) - 1), "nothing was paid");
            Assert.That(slot.CooldownUntil, Is.EqualTo(0f), "no cooldown started");
            Assert.That(state.Powers.ChaosBuffUntil, Is.EqualTo(new[] { 0f, 0f }), "no chaos window opened");
            state.Resources[0] = new Purse(0f, state.GodPowerCost(0, slot.Def));
            var ok = state.Powers.TryCast(0, "horus", new Hex(4, 4));
            Assert.That(ok.Paid && ok.Applied, Is.True, "an empty row still applies (Horus always returns true)");
            Assert.That(state.Resources[0].Favor, Is.EqualTo(0f).Within(1e-3f), "paid exactly the power cost");
            Assert.That(slot.CooldownUntil, Is.EqualTo(state.Elapsed + slot.Def.cd).Within(1e-3f));
            Assert.That(state.Powers.ChaosBuffUntil[1], Is.GreaterThan(state.Elapsed), "the OTHER side's chaos window opened");
            Assert.That(state.Powers.ChaosBuffUntil[0], Is.EqualTo(0f));
        }

        /// <summary>
        /// With U29 every god of all four factions is ported, so <see cref="PowerCheck.NotPorted"/> is
        /// no longer reachable from real data — the framework's refusal is asserted on an unknown key
        /// instead, and the check is kept because a new god arrives unported.
        /// </summary>
        [Test] public void AnUnknownPowerKeyIsNeitherPortedNorCastable()
        {
            var state = new MatchState(db, gods: new[] { GodState.Default(0, db, "aztec"), GodState.Default(1, db, "egypt") });
            state.Objectives.Clock.Advance(30f);
            state.Resources[0] = new Purse(0f, 250f);
            Assert.That(state.Powers.IsPorted("nobody"), Is.False);
            Assert.That(state.Powers.Definition("nobody"), Is.Null);
            Assert.That(state.Powers.TryCast(0, "nobody", new Hex(4, 4)).Check, Is.EqualTo(PowerCheck.NotSelected));
            var key = state.Gods[0].Selected[0].Key;
            state.Gods[0].Grant(key);
            var ok = state.Powers.TryCast(0, key, new Hex(4, 4));
            Assert.That(ok.Paid, Is.True, key + " is ported now");
            Assert.That(state.Resources[0].Favor, Is.LessThan(250f), "a ported cast pays");
        }

        [Test] public void ResetClearsPowerStateAndTimers()
        {
            var state = Egypt("ptah", "nephthys", "bastet", "horus");
            state.Resources[0] = new Purse(0f, 250f);
            foreach (var k in new[] { "ptah", "nephthys", "bastet" }) state.Gods[0].Grant(k);
            state.Powers.TryCast(0, "ptah", new Hex(0, 0));
            state.Powers.TryCast(0, "nephthys", new Hex(0, 0));
            state.Powers.TryCast(0, "bastet", new Hex(4, 10));
            var u = state.Units.Spawn(1, db.Unit("china", "ji"), new Hex(4, 4));
            u.DespawnAt = 99f;
            Assert.That(state.Powers.PtahCharge[0], Is.True);
            Assert.That(state.UnitContext.VeilUntil, Is.GreaterThan(0f));
            Assert.That(state.Buildings.All.Any(b => b.InvulnerableUntil > 0f), Is.True);
            state.Reset();
            Assert.That(state.Powers.PtahCharge, Is.EqualTo(new[] { false, false }));
            Assert.That(state.Powers.ChaosBuffUntil, Is.EqualTo(new[] { 0f, 0f }));
            Assert.That(state.UnitContext.VeilUntil, Is.EqualTo(0f));
            Assert.That(state.Buildings.All.All(b => b.InvulnerableUntil == 0f), Is.True, "Unity clears wards; the browser does not");
            Assert.That(state.Gods[0].Selected.All(s => !s.Unlocked && s.CooldownUntil == 0f), Is.True);
            Assert.That(state.Units.All, Is.Empty);
        }

        [Test] public void PtahDoublesTheNextHumanOnlyAndWaitsThroughIneligibleUnits()
        {
            var state = Egypt("ptah", "horus", "bastet", "osiris");
            state.Powers.PtahCharge[0] = true;
            Assert.That(state.Powers.ConsumePtahCharge(0, db.Unit("egypt", "doomedprince")), Is.False, "a hero does not spend it");
            Assert.That(state.Powers.ConsumePtahCharge(0, db.Unit("egypt", "ammit")), Is.False, "nor a myth");
            Assert.That(state.Powers.PtahCharge[0], Is.True);
            var extra = db.Unit("egypt", db.Powers.ptahExtraKey);
            Assert.That(extra, Is.Not.Null, "the exported exception is an Egyptian unit");
            var source = state.Buildings.Of(0, BuildingType.City);
            var exit = state.Buildings.AdjacentRoutableHexes(source, false, state.Terrain).First();
            state.Resources[0] = new Purse(120f, 0f);
            var def = db.Unit("egypt", "spear");
            Assert.That(state.Training.TryEnqueue(state.Resources[0], 0, def, exit, source, state.TrainingModifiersFor(0),
                state.Elapsed, out _, out var order, new[] { exit }), Is.True);
            Assert.That(state.Powers.ConsumePtahCharge(0, def), Is.True);
            order.SpawnCount = 2;
            Assert.That(state.Powers.PtahCharge[0], Is.False, "spent");
            var spawned = state.Training.Tick(order.ReadyAt, state.Units, state.Buildings);
            Assert.That(spawned.Count, Is.EqualTo(2));
            Assert.That(spawned[0].HasRoute || spawned[0].Route != null, Is.True, "the first copy has the drawn route");
            Assert.That(spawned[1].Route, Is.Null, "the second stands free");
            Assert.That(spawned.All(u => u.Key == "spear" && u.Hex == spawned[0].Hex), Is.True);
            Assert.That(state.Powers.ConsumePtahCharge(0, def), Is.False, "one charge, one double");
        }

        [Test] public void ForgeAndCreatorsWordAreSpentAsBodiesAreCreated()
        {
            Assert.That(fx.training.Length, Is.GreaterThan(3));
            foreach (var t in fx.training)
            {
                var pick = new[] { "hephaestus", "zeus", "ares", "athena" };
                var state = new MatchState(db,
                    gods: new[] { GodState.Create(0, db, "greek", pick), GodState.Default(1, db, "egypt") },
                    combatContext: new CombatContext { FactionForSide = s => s == 0 ? "greek" : "egypt" });
                state.Objectives.Clock.Advance(30f);
                state.Powers.Forge[0] = t.forge;
                state.Powers.PtahCharge[0] = t.ptah;
                var city = state.Buildings.Of(0, BuildingType.City);
                var exits = state.Buildings.AdjacentRoutableHexes(city, false, state.Terrain).ToList();
                state.Resources[0] = new Purse(db.FoodCap, db.FavorCap);
                string label = $"forge {t.forge} ptah {t.ptah}";
                for (int i = 0; i < t.keys.Length; i++)
                {
                    var def = db.Unit("greek", t.keys[i]);
                    Assert.That(state.Training.TryEnqueue(state.Resources[0], 0, def, exits[i], city,
                        state.TrainingModifiersFor(0), state.Elapsed, out var purse, out var order,
                        new[] { exits[i] }), Is.True, label + " " + t.keys[i]);
                    state.Resources[0] = purse;
                    // The Creator's Word is spent where the player commits the order, as DeploymentDraft does.
                    if (state.Powers.ConsumePtahCharge(0, def)) order.SpawnCount = 2;
                    Assert.That(order.SpawnCount, Is.EqualTo(t.queued[i].spawnCount), label + " queued " + t.keys[i]);
                }
                var spawned = state.Training.Tick(state.Training.Pending.Max(o => o.ReadyAt), state.Units, state.Buildings);
                Assert.That(spawned.Select(u => u.Key).ToArray(), Is.EqualTo(t.units.Select(w => w.key).ToArray()),
                    label + " spawn order");
                for (int i = 0; i < spawned.Count; i++)
                {
                    Assert.That(spawned[i].MaxHp, Is.EqualTo(t.units[i].maxhp).Within(1e-3), label + " max hp " + i);
                    Assert.That(spawned[i].Hp, Is.EqualTo(t.units[i].hp).Within(1e-3), label + " hp " + i);
                    Assert.That(spawned[i].Forged, Is.EqualTo(t.units[i].forged), label + " forged " + i);
                }
                Assert.That(state.Powers.Forge[0], Is.EqualTo(t.forgeAfter), label + " charges left");
                Assert.That(state.Powers.PtahCharge[0], Is.EqualTo(t.ptahAfter), label + " word left");
            }
        }

        [Test] public void AiSideTrainingSpendsItsOwnCharge()
        {
            var state = new MatchState(db, gods: new[] { GodState.None(0), GodState.Default(1, db, "china") });
            state.Powers.PtahCharge[1] = true;
            Assert.That(state.Powers.ConsumePtahCharge(0, db.Unit("china", "ji")), Is.False, "the player side has no charge");
            Assert.That(state.Powers.ConsumePtahCharge(1, db.Unit("china", "ji")), Is.True);
        }

        [TestCase(0f, 0.2f)] [TestCase(0.3f, 0.3f)] [TestCase(0.9f, 0.9f)] [TestCase(1f, 1f)]
        public void JudgmentIsDamageTakenWithAFloor(float missing, float expected)
        {
            var u = new Unit(1, 1, db.Unit("china", "ji"), new Hex(0, 0), HexLayout.Unit());
            u.TakeDamage(u.MaxHp * missing);
            Assert.That(EgyptPowers.JudgmentChance(u, db.Powers.Value("judgmentBase")), Is.EqualTo(expected).Within(1e-4));
        }

        [Test] public void OverlaysNeverTouchTheBaseMapAndReplanRoutes()
        {
            var state = new MatchState(db, gods: new[] { GodState.Default(0, db, "china"), GodState.Default(1, db, "egypt") });
            foreach (var h in Board.AllCells()) state.Terrain[h] = TerrainType.Plains;
            var hex = new Hex(4, 4);
            Assert.That(state.Terrain.RouteOk(hex, false, state.Buildings.RouteBlocker), Is.True);
            int version = state.Terrain.Version;
            var o = state.Terrain.AddOverlay(hex, TerrainType.Mountain, 38f);
            Assert.That(state.Terrain.Version, Is.GreaterThan(version), "presentation sees the change");
            Assert.That(state.Terrain[hex], Is.EqualTo(TerrainType.Mountain));
            Assert.That(state.Terrain.BaseAt(hex), Is.EqualTo(TerrainType.Plains), "the base map is never mutated");
            Assert.That(state.Terrain.RouteOk(hex, false, state.Buildings.RouteBlocker), Is.False, "rubble blocks ground routes");
            Assert.That(state.Terrain.RouteOk(hex, true, state.Buildings.RouteBlocker), Is.True, "but not flying ones");
            var path = state.Paths.FindPath(new Hex(4, 2), new[] { new Hex(4, 6) }, false);
            Assert.That(path, Is.Not.Null);
            Assert.That(path, Has.None.EqualTo(hex), "a new route plans around the rubble");
            Assert.That(state.Terrain.TryExpire(o, 37.9f), Is.False);
            Assert.That(state.Terrain.TryExpire(o, 38f), Is.True);
            Assert.That(state.Terrain[hex], Is.EqualTo(TerrainType.Plains), "expiry restores the exact base passability");
            state.Terrain.AddOverlay(hex, TerrainType.Forest, 99f, owner: 0);
            Assert.That(state.Terrain.InGrove(hex, 0), Is.True);
            Assert.That(state.Terrain.InGrove(hex, 1), Is.False, "the grove answers only to its owner");
            state.Reset();
            Assert.That(state.Terrain.Overlays, Is.Empty);
            Assert.That(state.Terrain[hex], Is.EqualTo(TerrainType.Plains), "reset clears every overlay");
        }

        [Test] public void TransformAndConscriptionChangeTheBodyInPlace()
        {
            var u = new Unit(1, 1, db.Unit("china", "ji"), new Hex(0, 0), HexLayout.Unit());
            u.TakeDamage(u.MaxHp / 2f);
            u.Conscript(0, 40f);
            Assert.That(u.Side, Is.EqualTo(0)); Assert.That(u.OriginalSide, Is.EqualTo(1)); Assert.That(u.Free, Is.True);
            u.ReleaseConscript();
            Assert.That(u.Side, Is.EqualTo(1)); Assert.That(u.ConscriptedUntil, Is.EqualTo(0f));
            var dragon = db.Unit("china", "dragon");
            Assert.That(u.Transform(dragon), Is.True);
            Assert.That(u.Key, Is.EqualTo("dragon")); Assert.That(u.MaxHp, Is.EqualTo(dragon.hp));
            Assert.That(u.Hp, Is.EqualTo(dragon.hp / 2f).Within(1e-3), "the health fraction carries over");
            Assert.That(u.Flying, Is.EqualTo(dragon.IsFlying));
            Assert.That(u.Transform(db.Unit("china", "ji")), Is.False, "once only");
        }

        [Test] public void GodEffectsRunInsideTheMatchLoopAfterTraining()
        {
            var state = Egypt("horus", "bastet", "osiris", "isis");
            var u = state.Units.Spawn(1, db.Unit("china", "ji"), new Hex(4, 1));
            u.Combat.PoisonUntil = 99f; u.Combat.PoisonDps = 10f;
            float before = u.Hp;
            new MatchLoop().Tick(state, 0.05f);
            Assert.That(u.Hp, Is.LessThan(before), "poison ticked in the loop");
        }
    }
}
