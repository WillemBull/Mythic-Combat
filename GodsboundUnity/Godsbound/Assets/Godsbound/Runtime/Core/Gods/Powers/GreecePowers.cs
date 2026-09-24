using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Units;

namespace Godsbound.Core.Gods.Powers
{
    /// <summary>
    /// Greece's eleven powers, one method each, ported case for case from <c>applyGodPower</c>.
    /// Greece's grammar: gods intervene in a SMALL AREA around a point — a bolt and its arc, a
    /// blessing on the units standing here — and two of them arm a charge instead of acting now.
    /// </summary>
    public static class GreecePowers
    {
        public static IEnumerable<PowerDefinition> All()
        {
            yield return new PowerDefinition("zeus", PowerTarget.Hex, Zeus);
            yield return new PowerDefinition("poseidon", PowerTarget.Hex, Poseidon);
            yield return new PowerDefinition("athena", PowerTarget.Hex, Athena);
            yield return new PowerDefinition("ares", PowerTarget.Hex, Ares);
            yield return new PowerDefinition("apollo", PowerTarget.Hex, Apollo);
            yield return new PowerDefinition("hermes", PowerTarget.Hex, Hermes);
            yield return new PowerDefinition("demeter", PowerTarget.Hex, Demeter);
            yield return new PowerDefinition("hades", PowerTarget.Hex, Hades);
            yield return new PowerDefinition("dionysus", PowerTarget.Hex, Dionysus);
            yield return new PowerDefinition("hephaestus", PowerTarget.Hex, Hephaestus);
            yield return new PowerDefinition("artemis", PowerTarget.Hex, Artemis);
        }

        /// <summary>Living enemies of the caster, nearest to <paramref name="from"/> first; ties keep list order.</summary>
        private static IEnumerable<Unit> EnemiesNear(PowerCast c, Hex from, float radius) =>
            c.State.Units.All.Where(u => !u.Dead && u.Side == c.EnemySide && !c.Ignores(u) &&
                                         Hex.Distance(u.Hex, from) <= radius)
                .OrderBy(u => Hex.Distance(u.Hex, from));

        /// <summary>Thunderbolt: the nearest enemy, then the nearest enemy to THAT one, for less.</summary>
        public static bool Zeus(PowerCast c)
        {
            var first = EnemiesNear(c, c.At, c.R("zeusRadius")).FirstOrDefault();
            if (first == null) return false;
            var arc = EnemiesNear(c, first.Hex, c.R("zeusArcRadius")).FirstOrDefault(u => u != first);
            Strike(c, first, c.R("zeusDamage"));
            Strike(c, arc, c.R("zeusArcDamage"));
            return true;
        }

        /// <summary>Raw damage with an ordinary death: the bolt and the sun's arrows kill outright.</summary>
        private static void Strike(PowerCast c, Unit u, float damage)
        {
            if (u == null || u.Dead) return;
            if (c.Elapsed < u.Combat.InvulnerableUntil) return; // invulnerable is COMPLETE
            ChinaPowers.RawDamage(c, u, damage);
        }

        /// <summary>Earthquake: the only power that damages enemy BUILDINGS. A warded building is skipped, not hit.</summary>
        public static bool Poseidon(PowerCast c)
        {
            int hit = 0;
            foreach (var b in c.State.Buildings.All.ToList())
            {
                if (b.Dead || b.Side == c.Side) continue;
                if (b.Hexes().Min(h => Hex.Distance(h, c.At)) > c.R("quakeRadius")) continue;
                if (c.State.Combat.RazeBuilding(c.Side, b, c.R("quakeDamage"), c.Elapsed) == 0f) continue;
                hit++;
            }
            return hit > 0;
        }

        /// <summary>Aegis: the caster's own units here take less damage for a while.</summary>
        public static bool Athena(PowerCast c) =>
            Bless(c, c.R("aegisRadius"), u => u.Combat.AegisUntil = c.Elapsed + c.R("aegisDuration"));

        /// <summary>Bloodlust: the caster's own units here hit harder — and are hit harder.</summary>
        public static bool Ares(PowerCast c) => Bless(c, c.R("aresRadius"), u =>
        {
            u.BuffUntil = c.Elapsed + c.R("aresDuration");
            u.Combat.BuffDamage = c.R("aresDamage");
            u.BuffSpeed = c.R("aresSpeed");
            u.Combat.BloodlustUntil = c.Elapsed + c.R("aresDuration");
        });

        /// <summary>One effect over the caster's OWN living units within a radius of the cast point.</summary>
        private static bool Bless(PowerCast c, float radius, System.Action<Unit> effect)
        {
            int n = 0;
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || u.Side != c.Side || Hex.Distance(u.Hex, c.At) > radius) continue;
                effect(u);
                n++;
            }
            return n > 0;
        }

        /// <summary>
        /// One effect over the caster's ENEMY living units within a radius; ally-only bodies are
        /// invisible to it. The effect answers whether that unit COUNTED — a power that skips the
        /// warded (Apollo) must not be reported as having found a target.
        /// </summary>
        private static bool Afflict(PowerCast c, float radius, System.Func<Unit, bool> effect)
        {
            int n = 0;
            foreach (var u in c.State.Units.All.ToList())
            {
                if (u.Dead || u.Side == c.Side || c.Ignores(u) || Hex.Distance(u.Hex, c.At) > radius) continue;
                if (effect(u)) n++;
            }
            return n > 0;
        }

        /// <summary>The Sun's Arrows: damage and a weakening over the enemies standing here.</summary>
        public static bool Apollo(PowerCast c) => Afflict(c, c.R("apolloRadius"), u =>
        {
            if (c.Elapsed < u.Combat.InvulnerableUntil) return false; // invulnerable is COMPLETE
            u.Combat.WeakenUntil = c.Elapsed + c.R("apolloWeaken");
            ChinaPowers.RawDamage(c, u, c.R("apolloDamage"));
            return true;
        });

        /// <summary>Thief of the Gods: take from the enemy's purse, up to a cap of each resource.</summary>
        public static bool Hermes(PowerCast c)
        {
            var purse = c.State.Resources[c.EnemySide];
            float cap = c.R("hermesTake");
            float favor = System.Math.Min(cap, purse.Favor), food = System.Math.Min(cap, purse.Food);
            if (favor <= 0f && food <= 0f) return false;
            c.State.Combat.Grant(c.EnemySide, -food, -favor);
            c.State.Combat.Grant(c.Side, food, favor);
            return true;
        }

        /// <summary>Blight: the enemy's fields grow nothing for a while.</summary>
        public static bool Demeter(PowerCast c)
        {
            c.State.Powers.BlightUntil[c.EnemySide] = c.Elapsed + c.R("blightDuration");
            return true;
        }

        /// <summary>
        /// Realm of the Dead: Osiris's plumbing with the one difference that IS the god — the pool
        /// is BOTH sides' fallen, not only the caster's. Paid even when nobody answers.
        /// </summary>
        public static bool Hades(PowerCast c)
        {
            var state = c.State;
            var b = state.Buildings.All.Where(x => x.Side == c.Side && !x.Dead)
                .OrderBy(x => x.Hexes().Min(h => Hex.Distance(h, c.At))).FirstOrDefault();
            if (b == null) return false;
            var spots = state.Buildings.AdjacentRoutableHexes(b, false, state.Terrain)
                .Where(h => state.Units.HasCapacity(h, false)).ToList();
            float window = c.HasPassive(c.Side, "corpseWindow") ? c.R("osirisWindowAnubis") : c.R("osirisWindow");
            var pool = state.Combat.TakeRecentDeaths(-1, c.Elapsed, window, (int)c.R("hadesCount"));
            string faction = state.FactionFor(c.Side);
            for (int n = 0; n < pool.Count; n++)
            {
                if (spots.Count == 0) break;
                var h = n < spots.Count ? spots[n] : spots[0];
                var def = state.Database.ResolveUnit(faction, pool[n].Key);
                if (def == null) continue;
                var u = state.Units.Spawn(c.Side, def, h, free: true, bornAt: c.Elapsed);
                u.DespawnAt = c.Elapsed + c.R("hadesDespawn");
            }
            return true;
        }

        /// <summary>The Vine: rooted, not stunned — an enemy here still swings, it simply cannot move.</summary>
        public static bool Dionysus(PowerCast c) =>
            Afflict(c, c.R("vineRadius"), u => { u.RootUntil = c.Elapsed + c.R("vineRoot"); return true; });

        /// <summary>The Forge: arm charges; the next units trained come out reinforced. Refused while lit.</summary>
        public static bool Hephaestus(PowerCast c)
        {
            var forge = c.State.Powers.Forge;
            if (forge[c.Side] > 0) return false;
            forge[c.Side] = (int)c.R("forgeCharges");
            return true;
        }

        /// <summary>
        /// Actaeon's End: the nearest enemy becomes the caster's quarry — and its OWN side turns on
        /// it. Nothing is created and nothing is stolen; their real army hunts one of its own.
        /// </summary>
        public static bool Artemis(PowerCast c)
        {
            var quarry = EnemiesNear(c, c.At, c.R("huntRadius")).FirstOrDefault();
            if (quarry == null) return false;
            c.State.Powers.Hunt[c.Side] = new HuntMark { Unit = quarry, Until = c.Elapsed + c.R("huntDuration") };
            return true;
        }
    }
}
