using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;

namespace Godsbound.Core.Gods.Powers
{
    /// <summary>
    /// The Aztecs' ten powers, one method each, ported case for case from <c>applyGodPower</c>.
    /// Their grammar: a god gives and takes in the same breath — a buff bought with your own units'
    /// blood, a gift of favor that buries a myth by the enemy's wall, a kill that leaves two halves
    /// fighting each other.
    /// </summary>
    /// <remarks>
    /// Ported AS THEY STAND TODAY. Willem ruled 2026-09-16 that the F-series redesign lands in Unity
    /// after U39, so nothing here anticipates it.
    /// </remarks>
    public static class AztecPowers
    {
        public static IEnumerable<PowerDefinition> All()
        {
            yield return new PowerDefinition("huitzilopochtli", PowerTarget.Hex, Huitzilopochtli);
            yield return new PowerDefinition("quetzalcoatl", PowerTarget.Hex, Quetzalcoatl);
            yield return new PowerDefinition("tlaloc", PowerTarget.Hex, Tlaloc);
            yield return new PowerDefinition("tezcatlipoca", PowerTarget.Hex, Tezcatlipoca);
            yield return new PowerDefinition("xipetotec", PowerTarget.Hex, XipeTotec);
            yield return new PowerDefinition("coatlicue", PowerTarget.Hex, Coatlicue);
            yield return new PowerDefinition("mictlantecuhtli", PowerTarget.Hex, Mictlantecuhtli);
            yield return new PowerDefinition("coyolxauhqui", PowerTarget.Hex, Coyolxauhqui);
            yield return new PowerDefinition("ehecatl", PowerTarget.Hex, Ehecatl);
            yield return new PowerDefinition("itzpapalotl", PowerTarget.Hex, Itzpapalotl);
        }

        /// <summary>
        /// Solar War: every one of the caster's units hits harder and moves faster, and each pays a
        /// slice of its OWN current health for it. The sun demands payment in blood; a weak unit dying
        /// to the price is the intended risk, not a bug.
        /// </summary>
        public static bool Huitzilopochtli(PowerCast c) => Own(c, u =>
        {
            u.BuffUntil = c.Elapsed + c.R("solarDuration");
            u.BuffSpeed = c.R("solarSpeed");
            u.Combat.BuffDamage = c.R("solarDamage");
            float cost = u.Hp * c.R("bloodPrice");
            if (cost >= u.Hp) c.State.Combat.KillUnit(u, c.Elapsed);
            else u.TakeDamage(cost);
        });

        /// <summary>First Wind: the caster's units move dramatically faster for a short burst.</summary>
        public static bool Ehecatl(PowerCast c) => Own(c, u =>
        {
            u.BuffUntil = c.Elapsed + c.R("windDuration");
            u.BuffSpeed = c.R("windSpeed");
        });

        /// <summary>One effect over every living unit of the caster's own side, board-wide.</summary>
        private static bool Own(PowerCast c, System.Action<Unit> effect)
        {
            int n = 0;
            foreach (var u in c.State.Units.All.ToList())
            {
                if (u.Dead || u.Side != c.Side) continue;
                effect(u);
                n++;
            }
            return n > 0;
        }

        /// <summary>
        /// Wind Serpent: a rushing wind resets EVERY enemy unit's route, so each must re-path from
        /// scratch. Deliberately with no ally-only exemption, as the browser case has none.
        /// </summary>
        public static bool Quetzalcoatl(PowerCast c)
        {
            int n = 0;
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || u.Side == c.Side) continue;
                u.SetRoute(null);
                u.Engagement = null;
                n++;
            }
            return n > 0;
        }

        /// <summary>
        /// The Weeping Rain: every open tile near the target becomes real water for a while, which is
        /// what gives Storm Lord's "in or beside water" passive terrain to hook into. An overlay, so
        /// the base map is untouched.
        /// </summary>
        public static bool Tlaloc(PowerCast c)
        {
            var terrain = c.State.Terrain;
            int n = 0;
            foreach (var h in ChinaPowers.HexesInRadius(c.At, (int)c.R("rainRadius")))
            {
                var t = terrain[h];
                if (t == TerrainType.Water || t == TerrainType.Mountain || c.State.Buildings.StandingAt(h) != null) continue;
                terrain.AddOverlay(h, TerrainType.Water, c.Elapsed + c.R("rainDuration"));
                n++;
            }
            return n > 0;
        }

        private static Unit NearestEnemy(PowerCast c, float radius) =>
            c.State.Units.All.Where(u => !u.Dead && u.Side == c.EnemySide && !c.Ignores(u) &&
                                         Hex.Distance(u.Hex, c.At) <= radius)
                .OrderBy(u => Hex.Distance(u.Hex, c.At)).FirstOrDefault();

        /// <summary>
        /// The target's own definition, resolved against ITS faction's table. Building a copy the other
        /// way round crashes the moment caster and target play different factions — an Aztec player
        /// mirroring a Chinese "ji", which does not exist in the Aztec table.
        /// </summary>
        private static Data.UnitData CopyOf(PowerCast c, Unit target) =>
            c.State.Database.ResolveUnit(c.State.FactionFor(target.Side), target.Key);

        /// <summary>
        /// Smoking Mirror: a reflection of the nearest enemy unit, fighting for the caster and set on
        /// the original at once. The stronger the unit, the worse the reflection — it copies its stats.
        /// Temporary.
        /// </summary>
        public static bool Tezcatlipoca(PowerCast c)
        {
            var target = NearestEnemy(c, c.R("mirrorRadius"));
            if (target == null) return false;
            var def = CopyOf(c, target);
            if (def == null) return false;
            var spot = target.Hex.Neighbors().Where(h => c.State.Units.HasCapacity(h, target.Flying))
                .Select(h => (Hex?)h).FirstOrDefault() ?? target.Hex;
            var mirror = c.State.Units.Spawn(c.Side, def, spot, free: false, bornAt: c.Elapsed);
            mirror.Engagement = new MovementContact(target);
            mirror.DespawnAt = c.Elapsed + c.R("mirrorDespawn");
            return true;
        }

        /// <summary>The Flaying: every passive of the enemy's gods stops answering for a while.</summary>
        public static bool XipeTotec(PowerCast c)
        {
            c.State.Gods[c.EnemySide].PassivesStrippedUntil = c.Elapsed + c.R("flayDuration");
            return true;
        }

        /// <summary>Serpent Skirt: a wave of serpents poisons every enemy unit on the field.</summary>
        public static bool Coatlicue(PowerCast c)
        {
            int n = 0;
            foreach (var u in c.State.Units.All)
            {
                if (u.Dead || u.Side == c.Side || c.Ignores(u)) continue;
                u.Combat.PoisonUntil = c.Elapsed + c.R("serpentPoison");
                u.Combat.PoisonDps = c.R("serpentDps");
                n++;
            }
            return n > 0;
        }

        /// <summary>
        /// The Bargain: the enemy is handed a real favor burst now — the gift is the trap — and a myth
        /// the caster has unlocked digs its way out beside their own wall a few seconds later. The
        /// favor is granted BEFORE the wall is looked for, so a cast with no enemy building still pays
        /// them, exactly as the browser case does. The delayed body is queued unpaid: the cast was its
        /// price.
        /// </summary>
        public static bool Mictlantecuhtli(PowerCast c)
        {
            var state = c.State;
            state.Combat.Grant(c.EnemySide, favor: c.R("bargainFavor"));
            var wall = state.Buildings.All.Where(b => b.Side == c.EnemySide && !b.Dead)
                .OrderBy(b => b.Hexes().Min(h => Hex.Distance(h, c.At))).FirstOrDefault();
            if (wall == null) return false;
            var spot = state.Buildings.AdjacentRoutableHexes(wall, false, state.Terrain)
                .Where(h => state.Units.HasCapacity(h, false)).Select(h => (Hex?)h).FirstOrDefault() ?? wall.HexA;
            // You strike a deal with the lord of Mictlan and do not get to choose what walks back out.
            var pool = state.Gods[c.Side].UnlockedMythsInRosterOrder().ToList();
            string key = pool.Count > 0 ? pool[(int)(c.Random() * pool.Count)] : c.God.myth;
            var def = state.Database.ResolveUnit(state.FactionFor(c.Side), key);
            if (def == null) return false;
            state.Training.Enqueue(c.Side, def, spot, null, c.Elapsed + c.R("bargainDelay"), new[] { spot });
            return true;
        }

        /// <summary>
        /// Dismemberment: the nearest enemy unit dies, and two weakened copies of it stand up where it
        /// fell — one theirs, one the caster's — set on each other and expiring shortly after.
        /// </summary>
        public static bool Coyolxauhqui(PowerCast c)
        {
            var target = NearestEnemy(c, c.R("dismemberRadius"));
            if (target == null) return false;
            var def = CopyOf(c, target);
            if (def == null) return false;
            var here = target.Hex;
            var beside = target.Hex.Neighbors().Select(h => (Hex?)h).FirstOrDefault() ?? target.Hex;
            c.State.Combat.KillUnit(target, c.Elapsed);
            float health = def.hp * c.R("dismemberHealth");
            // Their half stands where it fell, the caster's beside it — the browser's push order.
            var theirHalf = c.State.Units.Spawn(target.Side, def, here, free: false, bornAt: c.Elapsed);
            var myHalf = c.State.Units.Spawn(c.Side, def, beside, free: false, bornAt: c.Elapsed);
            foreach (var half in new[] { theirHalf, myHalf })
            {
                half.SetMaxHp(health, health);
                half.DespawnAt = c.Elapsed + c.R("dismemberDespawn");
            }
            theirHalf.Engagement = new MovementContact(myHalf);
            myHalf.Engagement = new MovementContact(theirHalf);
            return true;
        }

        /// <summary>
        /// Obsidian Wings: a burst of damage and a lingering slow over the enemies near the target —
        /// deliberately a radius, where Horus and Ra sweep a whole rank or lane.
        /// </summary>
        public static bool Itzpapalotl(PowerCast c)
        {
            int n = 0;
            foreach (var u in c.State.Units.All.ToList())
            {
                if (u.Dead || u.Side == c.Side || c.Ignores(u)) continue;
                if (Hex.Distance(u.Hex, c.At) > c.R("obsidianRadius")) continue;
                if (c.Elapsed < u.Combat.InvulnerableUntil) continue; // invulnerable is COMPLETE
                u.SlowUntil = c.Elapsed + c.R("obsidianSlow");
                ChinaPowers.RawDamage(c, u, c.R("obsidianDamage"));
                n++;
            }
            return n > 0;
        }
    }
}
