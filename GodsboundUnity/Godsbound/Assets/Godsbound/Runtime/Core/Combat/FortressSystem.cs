using System;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Units;

namespace Godsbound.Core.Combat
{
    public readonly struct FortressShot
    {
        public readonly Building Source;
        public readonly BoardPoint Destination;
        public readonly float At, Life;
        public FortressShot(Building source, BoardPoint target, float at, float life)
        { Source = source; Destination = target; At = at; Life = life; }
    }

    /// <summary>Browser tickFortresses, including stable lowest-HP selection and ready retries.</summary>
    public sealed class FortressSystem
    {
        private readonly BuildingMap buildings;
        private readonly UnitField units;
        private readonly CombatSystem combat;
        private readonly FortressRates rates;
        public event Action<FortressShot> Fired;
        public FortressSystem(BuildingMap buildings, UnitField units, CombatSystem combat, FortressRates rates)
        { this.buildings = buildings; this.units = units; this.combat = combat; this.rates = rates; }

        public void Tick(float dt, float elapsed)
        {
            foreach (var b in buildings.All)
            {
                if (b.Type != BuildingType.Fortress || b.Dead) continue;
                b.AttackTimer -= dt;
                if (b.AttackTimer > 0f) continue;
                Unit target = null;
                foreach (var u in units.All)
                {
                    if (u.Dead || u.Side == b.Side || u.Def.GetBool("allyTargetOnly") ||
                        elapsed < u.Combat.InvulnerableUntil ||
                        Math.Min(Hex.Distance(u.Hex, b.HexA), Hex.Distance(u.Hex, b.HexB)) > rates.range) continue;
                    // Browser sort is stable: ties retain original unit-list order.
                    // Browser guns intentionally do not consult concealment or line of sight.
                    if (target == null || u.Hp < target.Hp) target = u;
                }
                if (target == null) continue;
                b.AttackTimer = rates.cooldown;
                combat.DealRawDamage(target, rates.damage, elapsed);
                Fired?.Invoke(new FortressShot(b, target.Position, elapsed, rates.tracerLife));
            }
        }
    }
}
