using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Units;

namespace Godsbound.Core.Gods.Powers
{
    /// <summary>
    /// China's ten powers, ported case for case from <c>applyGodPower</c>. China's grammar: gods
    /// hold office over domains and reshape space. Terrain effects are ALWAYS temporary overlays
    /// (Willem) — the base map is never touched. Sun Wukong is the earned rule-breaker.
    /// </summary>
    public static class ChinaPowers
    {
        public static IEnumerable<PowerDefinition> All()
        {
            yield return new PowerDefinition("jade", PowerTarget.Hex, Jade);
            yield return new PowerDefinition("sunwukong", PowerTarget.Hex, SunWukong);
            yield return new PowerDefinition("nuwa", PowerTarget.Hex, NuWa);
            yield return new PowerDefinition("longwang", PowerTarget.Hex, LongWang);
            yield return new PowerDefinition("gonggong", PowerTarget.Hex, GongGong);
            yield return new PowerDefinition("erlangshen", PowerTarget.Hex, ErlangShen);
            yield return new PowerDefinition("xiwangmu", PowerTarget.Hex, XiWangmu);
            yield return new PowerDefinition("zhurong", PowerTarget.Hex, ZhuRong);
            yield return new PowerDefinition("chang", PowerTarget.Hex, Chang);
            yield return new PowerDefinition("leigong", PowerTarget.Hex, LeiGong);
        }

        /// <summary><c>hexesInRadius</c>, in the browser's column-then-row order.</summary>
        public static IEnumerable<Hex> HexesInRadius(Hex center, int radius)
        {
            for (int c = center.C - radius; c <= center.C + radius; c++)
                for (int r = center.R - radius; r <= center.R + radius; r++)
                    if (Board.InBounds(c, r) && Hex.Distance(center, new Hex(c, r)) <= radius)
                        yield return new Hex(c, r);
        }

        private static bool StandingBuilding(PowerCast c, Hex h) => c.State.Buildings.StandingAt(h) != null;

        /// <summary>Celestial Decree: the nearest enemy fights for the caster for a while.</summary>
        public static bool Jade(PowerCast c)
        {
            var target = c.State.Units.All
                .Where(u => !u.Dead && u.Side == c.EnemySide && !c.Ignores(u) && Hex.Distance(u.Hex, c.At) <= c.R("jadeRadius"))
                .OrderBy(u => Hex.Distance(u.Hex, c.At)).FirstOrDefault();
            if (target == null) return false;
            target.Conscript(c.Side, c.Elapsed + c.R("jadeConscript"));
            return true;
        }

        /// <summary>72 Transformations: one of your own units near the target becomes a random unlocked myth, for good.</summary>
        public static bool SunWukong(PowerCast c)
        {
            var pool = c.State.Gods[c.Side].UnlockedMythsInRosterOrder().ToList();
            if (pool.Count == 0) return false;
            var target = c.State.Units.All
                .Where(u => !u.Dead && u.Side == c.Side && !u.Transformed && u.Def.cat != "form" &&
                            Hex.Distance(u.Hex, c.At) <= c.R("wukongRadius"))
                .OrderBy(u => Hex.Distance(u.Hex, c.At)).FirstOrDefault();
            if (target == null) return false;
            var key = pool[(int)(c.Random() * pool.Count)];
            return target.Transform(c.State.Database.ResolveUnit(c.State.FactionFor(c.Side), key));
        }

        /// <summary>Five Colored Stones: the most damaged own building is repaired to full.</summary>
        public static bool NuWa(PowerCast c)
        {
            var hurt = c.State.Buildings.All.Where(b => b.Side == c.Side && !b.Dead && b.Hp < b.MaxHp)
                .OrderBy(b => b.Hp / b.MaxHp).FirstOrDefault();
            if (hurt == null) return false;
            hurt.RepairFull();
            return true;
        }

        /// <summary>
        /// Dragon Rain: open ground floods for a while, and everything in the deluge — both sides —
        /// takes a burst of damage. The damage lands even when nothing could flood.
        /// </summary>
        public static bool LongWang(PowerCast c)
        {
            var terrain = c.State.Terrain;
            int n = 0;
            foreach (var h in HexesInRadius(c.At, (int)c.R("floodRadius")))
            {
                var t = terrain[h];
                if (t == TerrainType.Water || t == TerrainType.Mountain || StandingBuilding(c, h)) continue;
                terrain.AddOverlay(h, TerrainType.Water, c.Elapsed + c.R("floodDuration"));
                n++;
            }
            foreach (var u in c.State.Units.All.ToList())
            {
                if (u.Dead || c.Ignores(u) || Hex.Distance(u.Hex, c.At) > c.R("floodHitRadius")) continue;
                if (c.Elapsed < u.Combat.InvulnerableUntil) continue;
                RawDamage(c, u, c.R("floodDamage"));
            }
            return n > 0;
        }

        /// <summary>Raw power damage with an ordinary death (no revivals): the Dragon Rain and fire path.</summary>
        public static void RawDamage(PowerCast c, Unit u, float damage)
        {
            if (damage >= u.Hp) c.State.Combat.KillUnit(u, c.Elapsed);
            else u.TakeDamage(damage);
        }

        /// <summary>Broken Pillar: open ground collapses into impassable rubble for a while.</summary>
        public static bool GongGong(PowerCast c)
        {
            var terrain = c.State.Terrain;
            int n = 0;
            foreach (var h in HexesInRadius(c.At, (int)c.R("rubbleRadius")))
            {
                var t = terrain[h];
                if (t == TerrainType.Mountain || t == TerrainType.Water || StandingBuilding(c, h)) continue;
                terrain.AddOverlay(h, TerrainType.Mountain, c.Elapsed + c.R("rubbleDuration"));
                n++;
            }
            return n > 0;
        }

        /// <summary>Third Eye: enemies are revealed, conjured ones cease, conscripts go home, and the enemy cannot hide for a while.</summary>
        public static bool ErlangShen(PowerCast c)
        {
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || u.Side != c.EnemySide) continue;
                u.Combat.Invisible = false;
                if (u.DespawnAt != 0f) { u.Kill(); continue; } // unmaking is not dying: no death rewards
                if (u.ConscriptedUntil != 0f) u.ReleaseConscript();
            }
            c.State.UnitContext.RevealedUntil[c.EnemySide] = c.Elapsed + c.R("thirdEyeBar");
            return true;
        }

        /// <summary>Garden of Kunlun: bamboo grows over OPEN ground only, owned by the caster, for a while.</summary>
        public static bool XiWangmu(PowerCast c)
        {
            var terrain = c.State.Terrain;
            int n = 0;
            int radius = (int)c.God.GetFloat("groveRadius", c.R("groveRadius"));
            float until = c.Elapsed + c.God.GetFloat("groveDur", c.R("groveDuration"));
            foreach (var h in HexesInRadius(c.At, radius))
            {
                var t = terrain[h];
                if ((t != TerrainType.Plains && t != TerrainType.Desert) || StandingBuilding(c, h)) continue;
                terrain.AddOverlay(h, TerrainType.Forest, until, owner: c.Side);
                n++;
            }
            return n > 0;
        }

        /// <summary>Primordial Fire: a zone that burns anyone standing in it, both sides, for a while. Terrain is unchanged.</summary>
        public static bool ZhuRong(PowerCast c)
        {
            var terrain = c.State.Terrain;
            int n = 0;
            foreach (var h in HexesInRadius(c.At, (int)c.R("fireRadius")))
            {
                if (StandingBuilding(c, h)) continue;
                terrain.AddOverlay(h, terrain[h], c.Elapsed + c.R("fireDuration"), damagePerSecond: c.R("fireDps"));
                n++;
            }
            return n > 0;
        }

        /// <summary>Moonfall: every unit on the field, both sides, is slowed and revealed.</summary>
        public static bool Chang(PowerCast c)
        {
            int n = 0;
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || c.Ignores(u)) continue;
                u.SlowUntil = c.Elapsed + c.R("moonfallSlow");
                u.Combat.Invisible = false;
                n++;
            }
            return n > 0;
        }

        /// <summary>Heaven's Judgment: enemies near the target are stunned and revealed; the fear-immune are not.</summary>
        public static bool LeiGong(PowerCast c)
        {
            int n = 0;
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || u.Side != c.EnemySide || c.Ignores(u) || Hex.Distance(u.Hex, c.At) > c.R("thunderRadius") ||
                    u.Def.GetBool("fearImmune")) continue;
                u.StunUntil = c.Elapsed + c.R("thunderStun");
                u.Combat.Invisible = false;
                n++;
            }
            return n > 0;
        }
    }
}
