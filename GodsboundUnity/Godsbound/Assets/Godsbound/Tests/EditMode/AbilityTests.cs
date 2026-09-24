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
using Godsbound.Core.Match;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// U28: the named unit abilities — Hou Yi's snipe, the Plague Bearer's fan-out, a healer's
    /// cadence, Theseus's retreat, Ajax's stand, Perseus, Odysseus, Orpheus, Heracles, Ariadne, the
    /// form chains and Tepoztecatl's swallow — replayed FRAME BY FRAME against small browser matches
    /// in <c>abilities_reference.json</c> (<c>node tools/export_unity_abilities.js</c>).
    /// </summary>
    /// <remarks>
    /// The browser driver runs units only: <c>tickGodEffects</c>, then every unit in ARRAY ORDER,
    /// then <c>tickFormChains</c>, then the dead are dropped. Economy, training, fortress guns and
    /// the AI are deliberately not ticked — each has its own fixture — so a failure here is an
    /// ability, not a neighbouring system. Note the order is FORWARD, where
    /// <see cref="MatchLoop"/> iterates in reverse; that difference is recorded in HANDOFF.
    /// </remarks>
    public class AbilityTests
    {
        [Serializable] public class Cell { public int c, r; public Hex Hex => new Hex(c, r); }
        [Serializable] public class Seed
        {
            public int side, c, r, index; public string key, faction; public float hp, frenzy;
            public bool hold, invisible; public Cell[] route;
        }
        [Serializable] public class GodOn { public int side; public string key; }
        [Serializable] public class HuntRow { public int unit; public float until; }
        [Serializable] public class BuildingRow { public int side, c0, r0, c1, r1; public string type; public float hp; }
        [Serializable] public class UnitFrame
        {
            public string key; public int side, c, r, target; public float hp, invuln, frenzy, stun, lastStand;
            public bool dead, retreating, sniping, invisible, revived;
        }
        [Serializable] public class Frame { public UnitFrame[] units; public float[] bldHp, favor; public int swallowed; }
        [Serializable] public class AbilityCase
        {
            public string name, player, ai; public float dt, elapsed;
            public Seed[] units; public GodOn[] gods; public HuntRow[] hunt; public BuildingRow[] buildings; public Frame[] frames;
        }
        [Serializable] public class Fixture { public string source; public AbilityCase[] cases; }

        /// <summary>The browser driver pins <c>Math.random</c> to zero, so every roll must be zero here too.</summary>
        private sealed class ZeroRandom : System.Random
        {
            public override double NextDouble() => 0d;
            public override int Next(int maxValue) => 0;
        }

        private GameDatabase db;
        private Fixture fx;

        [OneTimeSetUp] public void Load()
        {
            GameDataLoader.Invalidate();
            db = GameDataLoader.Load();
            fx = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/abilities_reference.json")));
        }

        private MatchState Build(AbilityCase c)
        {
            var map = new BuildingMap(c.buildings.Select(r => new Building(r.side, Building.ParseType(r.type), r.type,
                new Hex(r.c0, r.r0), new Hex(r.c1, r.r1), r.hp)));
            Func<int, string> faction = s => s == 0 ? c.player : c.ai;
            var gods = new[] { GodState.Default(0, db, c.player), GodState.Default(1, db, c.ai) };
            var state = new MatchState(db, buildings: map,
                combatContext: new CombatContext { FactionForSide = faction },
                movementContext: new MovementContext { FactionForSide = faction },
                gods: gods, rng: new ZeroRandom(), pathJitter: false);
            for (int r = 0; r < Board.Rows; r++)
                for (int col = 0; col < Board.Cols; col++)
                    state.Terrain[col, r] = TerrainType.Plains; // the driver flattens the board
            state.Objectives.Clock.Advance(c.elapsed);
            foreach (var g in c.gods) Assert.That(state.Gods[g.side].Grant(g.key), Is.True, c.name + " " + g.key);
            foreach (var s in c.units)
            {
                var def = db.ResolveUnit(s.faction, s.key);
                Assert.That(def, Is.Not.Null, s.key);
                var body = state.Units.Spawn(s.side, def, new Hex(s.c, s.r), free: s.route == null || s.route.Length == 0,
                                             bornAt: c.elapsed);
                if (s.hp < body.Hp) body.TakeDamage(body.Hp - s.hp);
                if (s.route != null && s.route.Length > 0) body.SetRoute(s.route.Select(h => h.Hex), s.index);
                body.Hold = s.hold;
                body.Combat.FrenzyUntil = s.frenzy;
                body.Combat.Invisible = s.invisible;
            }
            for (int side = 0; side < 2; side++)
                state.Powers.Hunt[side] = c.hunt[side].unit < 0 ? null
                    : new HuntMark { Unit = state.Units.All[c.hunt[side].unit], Until = c.hunt[side].until };
            state.Resources[0] = state.Resources[1] = Purse.Empty;
            return state;
        }

        /// <summary>
        /// What the browser calls <c>u.target</c>: an index into the live unit list, or
        /// <c>-2 - buildingIndex</c> for a siege target, or -1 for nothing.
        /// </summary>
        private static int TargetCode(MatchState state, Unit u)
        {
            var t = u.Engagement;
            if (t == null) return -1;
            if (t.IsBuilding) return -2 - state.Buildings.All.ToList().IndexOf(t.Building);
            return state.Units.All.ToList().IndexOf(t.Unit);
        }

        [Test] public void BrowserAbilitiesReplayFrameByFrame()
        {
            Assert.That(fx.cases.Length, Is.GreaterThan(15), "every named ability has a scenario");
            var update = new UnitUpdate(new ZeroRandom());
            foreach (var c in fx.cases)
            {
                var state = Build(c);
                for (int f = 0; f < c.frames.Length; f++)
                {
                    state.Objectives.Clock.Advance(c.dt);
                    float elapsed = state.Elapsed;
                    state.GodEffects.Tick(c.dt, elapsed);
                    // FORWARD, and re-reading the count, exactly as the driver's indexed loop does:
                    // a body appended this frame acts this frame.
                    var acting = state.Units.All;
                    for (int i = 0; i < acting.Count; i++)
                        if (!acting[i].Dead) update.Update(state, acting[i], c.dt, elapsed);
                    state.FormChains.Tick(elapsed);
                    state.Units.RemoveDead();

                    var want = c.frames[f];
                    string label = $"{c.name} frame {f}";
                    var units = state.Units.All;
                    Assert.That(units.Select(u => u.Key).ToArray(), Is.EqualTo(want.units.Select(w => w.key).ToArray()),
                        label + " bodies");
                    for (int i = 0; i < units.Count; i++)
                    {
                        var u = units[i]; var w = want.units[i]; string l = $"{label} unit {i} {w.key}";
                        Assert.That(u.Side, Is.EqualTo(w.side), l + " side");
                        Assert.That(u.Hex, Is.EqualTo(new Hex(w.c, w.r)), l + " hex");
                        Assert.That(u.Hp, Is.EqualTo(w.hp).Within(1e-3), l + " hp");
                        Assert.That(u.Dead, Is.EqualTo(w.dead), l + " dead");
                        Assert.That(u.Combat.InvulnerableUntil, Is.EqualTo(w.invuln).Within(1e-3), l + " invuln");
                        Assert.That(u.Combat.FrenzyUntil, Is.EqualTo(w.frenzy).Within(1e-3), l + " frenzy");
                        Assert.That(u.StunUntil, Is.EqualTo(w.stun).Within(1e-3), l + " stun");
                        Assert.That(u.Combat.Retreating, Is.EqualTo(w.retreating), l + " retreating");
                        Assert.That(u.Combat.LastStandEndsAt, Is.EqualTo(w.lastStand).Within(1e-3), l + " last stand");
                        Assert.That(u.Sniping, Is.EqualTo(w.sniping), l + " sniping");
                        Assert.That(u.Combat.Invisible, Is.EqualTo(w.invisible), l + " invisible");
                        Assert.That(u.Combat.HasRevived, Is.EqualTo(w.revived), l + " revived");
                        Assert.That(TargetCode(state, u), Is.EqualTo(w.target), l + " target");
                    }
                    var blds = state.Buildings.All;
                    for (int i = 0; i < blds.Count; i++)
                        if (!blds[i].Dead)
                            Assert.That(blds[i].Hp, Is.EqualTo(want.bldHp[i]).Within(1e-3), label + " building " + i);
                    for (int side = 0; side < 2; side++)
                        Assert.That(state.Resources[side].Favor, Is.EqualTo(want.favor[side]).Within(1e-3),
                            label + " favor " + side);
                    Assert.That(state.FormChains.Swallowed.Count, Is.EqualTo(want.swallowed), label + " swallowed");
                }
            }
        }

        [Test] public void EveryAbilityFlagInTheExportIsReadSomewhere()
        {
            // A flag nobody reads is an ability that silently does nothing. These are the U28 set.
            foreach (var field in new[] { "snipeEvery", "snipeWindup", "multiTarget", "heal", "summonsEvery",
                "summonKey", "gorgonStun", "stalks", "ragesEvery", "splitsAtRouteEnd", "swallowedByMyth",
                "becomesOnDeath", "becomesAfter", "inert", "allyTargetOnly" })
                Assert.That(db.AllUnits.Any(u => u.Has(field)), Is.True, field + " is carried by no unit");
            foreach (var key in new[] { "healInterval", "retreatRefresh", "gorgonReach", "ambushDefault",
                "rageDefault", "quarryScore", "swallowRadius", "swallowDefault", "swallowReturnHealth", "forgeHp" })
                Assert.That(db.Powers.Value(key), Is.GreaterThan(0f), key);
        }
    }
}
