using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Units;

namespace Godsbound.Core.Gods.Powers
{
    /// <summary>
    /// Egypt's eleven powers, one method each, ported case for case from <c>applyGodPower</c>.
    /// Egypt's grammar: gods act on the cosmos — rows, columns, cycles, resurrection, ma'at.
    /// </summary>
    public static class EgyptPowers
    {
        public static IEnumerable<PowerDefinition> All()
        {
            yield return new PowerDefinition("horus", PowerTarget.Hex, Horus);
            yield return new PowerDefinition("ra", PowerTarget.Hex, Ra);
            yield return new PowerDefinition("bastet", PowerTarget.Hex, Bastet);
            yield return new PowerDefinition("osiris", PowerTarget.Hex, Osiris);
            yield return new PowerDefinition("isis", PowerTarget.EnemyGod, Isis);
            yield return new PowerDefinition("anubis", PowerTarget.Hex, Anubis);
            yield return new PowerDefinition("set", PowerTarget.Hex, Set);
            yield return new PowerDefinition("thoth", PowerTarget.Hex, Thoth);
            yield return new PowerDefinition("sekhmet", PowerTarget.Hex, Sekhmet);
            yield return new PowerDefinition("nephthys", PowerTarget.Hex, Nephthys);
            yield return new PowerDefinition("ptah", PowerTarget.Hex, Ptah);
        }

        /// <summary>Falcon's Wrath: flat damage across the target's ROW.</summary>
        public static bool Horus(PowerCast c) => Sweep(c, c.R("horusDamage"), u => u.Hex.R == c.At.R);

        /// <summary>Solar Barque: damage by time of day down the target's COLUMN.</summary>
        public static bool Ra(PowerCast c) => Sweep(c, c.Rates.RaDamage(c.Elapsed), u => u.Hex.C == c.At.C);

        private static bool Sweep(PowerCast c, float damage, System.Func<Unit, bool> inShape)
        {
            foreach (var u in c.State.Units.All.ToList())
            {
                if (u.Dead || u.Side == c.Side || c.Ignores(u) || !inShape(u)) continue;
                if (c.Elapsed < u.Combat.InvulnerableUntil) continue; // invulnerable is COMPLETE
                c.State.Combat.DealPowerDamage(u, damage, c.Elapsed);
            }
            return true;
        }

        private static int Reach(Building b, Hex h) => b.Hexes().Min(x => Hex.Distance(x, h));

        /// <summary>Ties keep the building list's order, as the browser's stable sort and strict &lt; do.</summary>
        private static Building NearestOwn(PowerCast c) =>
            c.State.Buildings.All.Where(b => b.Side == c.Side && !b.Dead).OrderBy(b => Reach(b, c.At)).FirstOrDefault();

        /// <summary>Guardian's Grace: the nearest own building cannot be damaged for a while.</summary>
        public static bool Bastet(PowerCast c)
        {
            var b = NearestOwn(c);
            if (b == null) return false;
            b.InvulnerableUntil = c.Elapsed + c.R("bastetWard");
            return true;
        }

        /// <summary>Field of Reeds: the side's most recent fallen humans return, briefly, beside the nearest own building.</summary>
        public static bool Osiris(PowerCast c)
        {
            var b = NearestOwn(c);
            if (b == null) return false;
            var state = c.State;
            var spots = state.Buildings.AdjacentRoutableHexes(b, false, state.Terrain)
                .Where(h => state.Units.HasCapacity(h, false)).ToList();
            float window = c.HasPassive(c.Side, "corpseWindow") ? c.R("osirisWindowAnubis") : c.R("osirisWindow");
            var pool = state.Combat.TakeRecentDeaths(c.Side, c.Elapsed, window, (int)c.R("osirisCount"));
            string faction = state.FactionFor(c.Side);
            for (int n = 0; n < pool.Count; n++)
            {
                if (spots.Count == 0) break;
                var h = n < spots.Count ? spots[n] : spots[0];
                var def = state.Database.ResolveUnit(faction, pool[n].Key);
                if (def == null) continue;
                var u = state.Units.Spawn(c.Side, def, h, free: true, bornAt: c.Elapsed);
                u.DespawnAt = c.Elapsed + c.R("osirisDespawn");
            }
            return true;
        }

        /// <summary>Secret Name: lock one of the enemy's unlocked gods — the picked one, else the one closest to ready.</summary>
        public static bool Isis(PowerCast c)
        {
            var eligible = c.State.Gods[c.EnemySide].Selected.Where(s => s.Unlocked).ToList();
            var target = eligible.FirstOrDefault(s => s.Key == c.PickedGod)
                         ?? eligible.OrderBy(s => s.CooldownUntil).FirstOrDefault();
            if (target == null) return false;
            target.LockedUntil = c.Elapsed + c.R("isisLock");
            return true;
        }

        private static Unit NearestEnemy(PowerCast c, float radius) =>
            c.State.Units.All.Where(u => !u.Dead && u.Side == c.EnemySide && !c.Ignores(u) && Hex.Distance(u.Hex, c.At) <= radius)
                .OrderBy(u => Hex.Distance(u.Hex, c.At)).FirstOrDefault();

        /// <summary><c>judgmentDeathChance</c>: the damage taken, never less than the floor.</summary>
        public static float JudgmentChance(Unit u, float floor)
        {
            float frac = u.MaxHp > 0f ? u.Hp / u.MaxHp : 0f;
            frac = frac < 0f ? 0f : frac > 1f ? 1f : frac;
            return System.Math.Max(floor, 1f - frac);
        }

        /// <summary>Judgment: the nearest enemy dies on a chance equal to the damage it has taken.</summary>
        public static bool Anubis(PowerCast c)
        {
            var target = NearestEnemy(c, c.R("anubisRadius"));
            if (target == null) return false;
            if (c.Random() < JudgmentChance(target, c.R("judgmentBase"))) c.State.Combat.KillUnit(target, c.Elapsed);
            return true;
        }

        /// <summary>Desert Storm: every unit near the target, BOTH sides, is slowed.</summary>
        public static bool Set(PowerCast c) => Zone(c, c.R("setRadius"), u => u.SlowUntil = c.Elapsed + c.R("setSlow"));

        /// <summary>Eye Unleashed: every unit near the target, both sides, attacks anyone.</summary>
        public static bool Sekhmet(PowerCast c) =>
            Zone(c, c.R("sekhmetRadius"), u => u.Combat.FrenzyUntil = c.Elapsed + c.R("sekhmetFrenzy"));

        private static bool Zone(PowerCast c, float radius, System.Action<Unit> effect)
        {
            int n = 0;
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || c.Ignores(u) || Hex.Distance(u.Hex, c.At) > radius) continue;
                effect(u);
                n++;
            }
            return n > 0;
        }

        /// <summary>Rewrite: the nearest enemy walks its already-travelled hexes back the way it came.</summary>
        public static bool Thoth(PowerCast c)
        {
            var target = NearestEnemy(c, c.R("thothRadius"));
            if (target == null) return false;
            var walked = (target.Route ?? new List<Hex>()).Take(target.RouteIndex).Reverse().ToList();
            target.SetRoute(walked);
            target.Engagement = null;
            return true;
        }

        /// <summary>Twilight Veil: no-man's land hides both armies; refused while this side is barred from hiding.</summary>
        public static bool Nephthys(PowerCast c)
        {
            var ctx = c.State.UnitContext;
            if (c.Elapsed < ctx.RevealedUntil[c.Side]) return false;
            ctx.VeilUntil = c.Elapsed + c.God.GetFloat("veilDur", c.R("nephthysVeil"));
            return true;
        }

        /// <summary>Creator's Word: arm a charge; the next shapeable unit trained is created twice.</summary>
        public static bool Ptah(PowerCast c)
        {
            var charge = c.State.Powers.PtahCharge;
            if (charge[c.Side]) return false;
            charge[c.Side] = true;
            return true;
        }
    }
}
