using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;

namespace Godsbound.Core.Combat
{
    public sealed class CombatContext
    {
        public Func<int, string> FactionForSide = side => "";
        public Func<int, string, bool> HasPassive = (side, key) => false;
        public Func<Unit, bool> InGrove = unit => false;
        public Func<int, float> GardenDamage = side => 1f;
        public Func<int, float> ChaosBuffUntil = side => 0f;
        // Shared with the match's economy and training systems.
        public Purse[] Resources = { Purse.Empty, Purse.Empty };
    }

    public readonly struct HumanDeath
    {
        public readonly string Key;
        public readonly int Side;
        public readonly Hex Hex;
        public readonly float At;
        public HumanDeath(Unit unit, float elapsed)
        { Key = unit.Key; Side = unit.Side; Hex = unit.Hex; At = elapsed; }
        public HumanDeath(string key, int side, Hex hex, float at)
        { Key = key; Side = side; Hex = hex; At = at; }
    }

    /// <summary>One landed blow, for anything that wants to SHOW it. Presentation only.</summary>
    public readonly struct HitReport
    {
        public readonly Unit Attacker;
        /// <summary>Named Victim, not Target: a test type already owns that name (see AGENTS.md).</summary>
        public readonly Unit Victim;
        public readonly Buildings.Building Building;
        public readonly float Amount;
        public readonly float At;
        public HitReport(Unit attacker, MovementContact target, float amount, float at)
        { Attacker = attacker; Victim = target.Unit; Building = target.Building; Amount = amount; At = at; }

        /// <summary>Where the number belongs: the body that was hit, or the wall.</summary>
        public bool OnBuilding => Building != null;
    }

    /// <summary>One instance per match. Pure simulation of calcDmg/dealDamage and their death helpers.</summary>
    public sealed class CombatSystem
    {
        private readonly UnitField _units;
        private readonly BuildingMap _buildings;
        private readonly TerrainMap _terrain;
        private readonly GameDatabase _db;
        private readonly CombatContext _context;
        private readonly List<HumanDeath> _deadHumans = new List<HumanDeath>();
        private readonly HashSet<Unit> _recordedDeaths = new HashSet<Unit>();
        private readonly float[] _buildingDamage = new float[2];
        public IReadOnlyList<HumanDeath> DeadHumans => _deadHumans;
        public IReadOnlyList<float> BuildingDamage => _buildingDamage;
        // Presentation and U13's objective checker subscribe without entering the core.
        public event Action<Unit> UnitDied;
        public event Action<Building> BuildingDestroyed;

        /// <summary>
        /// Every landed ATTACK, with what it took off. Raised for units and buildings alike, on every
        /// hit and with no damage floor (roadmap 6.5, Willem's call over a 15-damage threshold).
        /// Deliberately NOT raised for splash, poison, auras or god powers: the browser pushes its
        /// floating number from <c>dealDamage</c> only, and a number for damage nobody swung for
        /// would read as a second hit.
        /// </summary>
        public event Action<HitReport> Hit;
        private CombatRates Rates => _db.Combat;
        private float R(string key) => Rates.Value(key);
        private bool Passive(int side, string key) => _context.HasPassive(side, key);

        public CombatSystem(UnitField units, BuildingMap buildings, TerrainMap terrain,
                            GameDatabase database, CombatContext context = null)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _db = database ?? throw new ArgumentNullException(nameof(database));
            _context = context ?? new CombatContext();
        }

        /// <summary>
        /// Back to the start of a match: no deaths recorded, no building damage dealt.
        /// </summary>
        /// <remarks>
        /// <c>MatchObjectives</c> holds <see cref="BuildingDamage"/> BY REFERENCE as the
        /// at-time tiebreaker, so without this a restarted match would be decided partly by the
        /// previous match's damage. <c>MatchState.Reset</c> calls it; the array instance
        /// is kept so that reference stays valid.
        /// </remarks>
        public void Reset()
        {
            _deadHumans.Clear();
            _recordedDeaths.Clear();
            Array.Clear(_buildingDamage, 0, _buildingDamage.Length);
        }

        private bool NearWater(Unit u) => _terrain[u.Hex] == TerrainType.Water ||
            u.Hex.Neighbors().Any(h => _terrain[h] == TerrainType.Water);
        private bool NearBuilding(Unit u, Building b, float range) => b != null && !b.Dead &&
            b.Hexes().Any(h => Hex.Distance(h, u.Hex) <= range);
        private bool Bamboo(Unit u) => _context.InGrove(u) ||
            (_context.FactionForSide(u.Side) == "china" && _terrain[u.Hex] == TerrainType.Forest &&
             (int)Board.SideForRow(u.Hex.R) == u.Side);
        private bool Lake(Unit u) => _context.FactionForSide(u.Side) == "aztec" &&
            _terrain[u.Hex] == TerrainType.Water && _buildings.Of(u.Side).Any(b =>
                !b.Dead && b.Hexes().Any(h => Hex.Distance(h, u.Hex) == 1));
        public string EffectiveDamageType(Unit attacker, MovementContact target) =>
            attacker.Def.GetFloat("meleeWithin") > 0f &&
            target.Distance(attacker.Hex) <= attacker.Def.GetFloat("meleeWithin") ? "melee" : attacker.Def.dtype;

        public float CalculateDamage(Unit attacker, Unit target, float elapsed) =>
            CalculateDamage(attacker, new MovementContact(target), elapsed);
        public float CalculateDamage(Unit attacker, Building target, float elapsed) =>
            CalculateDamage(attacker, new MovementContact(target), elapsed);

        public float CalculateDamage(Unit a, MovementContact target, float elapsed)
        {
            var t = target.Unit;
            var b = target.Building;
            if (elapsed < (t != null ? t.Combat.InvulnerableUntil : b.InvulnerableUntil)) return 0f;
            float d = a.Def.dmg;
            string type = EffectiveDamageType(a, target);
            if (elapsed < a.BuffUntil) d *= a.Combat.BuffDamage;
            if (a.Def.GetFloat("rageDmg") != 0f && elapsed < a.Combat.FrenzyUntil) d *= a.Def.GetFloat("rageDmg");
            if (t != null && type == "melee" && t.Def.GetFloat("meleeResist") != 0f) d *= t.Def.GetFloat("meleeResist");
            if (t != null && elapsed < t.Combat.AegisUntil) d *= R("aegis");
            if (elapsed < a.StunUntil) d *= R("stunned");
            if (t != null && elapsed < t.Combat.BloodlustUntil) d *= R("bloodlust");
            if (elapsed < a.Combat.WeakenUntil) d *= R("weakened");
            if (type == "ranged" && Passive(a.Side, "rangedDmg10")) d *= R("rangedBonus");
            var mentor = a.Def.cat == "human" ? _units.AliveOf(a.Side).FirstOrDefault(o =>
                o != a && o.Def.GetFloat("teachRange") > 0f && Hex.Distance(o.Hex, a.Hex) <= o.Def.GetFloat("teachRange")) : null;
            if (mentor != null) d *= mentor.Def.GetFloat("teachDmg", R("mentorFallback"));
            if (Passive(a.Side, "kingsAuthority") && _buildings.Of(a.Side).Any(o => NearBuilding(a, o, R("authorityRange")))) d *= R("authority");
            var half = Board.SideForRow(a.Hex.R);
            if (a.Def.GetFloat("abroadDmg") != 0f && half != BoardSide.Neutral && (int)half != a.Side) d *= a.Def.GetFloat("abroadDmg");
            if (Passive(a.Side, "openTileDmg") && _terrain[a.Hex] != TerrainType.Forest && _terrain[a.Hex] != TerrainType.Mountain) d *= R("openGround");
            if (Passive(a.Side, "gardenOfKunlun") && Bamboo(a)) d *= _context.GardenDamage(a.Side);
            if (Passive(a.Side, "chaosDamageBoost") && elapsed < _context.ChaosBuffUntil(a.Side)) d *= R("chaosBoost");
            if (Passive(a.Side, "moonlight")) d *= R("moonlight");
            if (a.Def.GetFloat("surpriseAttack") != 0f && !a.Combat.AmbushUsed) d *= a.Def.GetFloat("surpriseAttack");
            if (Passive(a.Side, "razorWings") && a.Free && !a.Combat.AmbushUsed) d *= R("razorWings");
            if (a.Def.GetBool("ambushInWater") && _terrain[a.Hex] == TerrainType.Water) d *= R("waterAmbush");
            if (Passive(a.Side, "waterDamageBonus") && NearWater(a)) d *= R("waterDamage");
            if (a.Def.GetBool("strongerAsBuildingsFall"))
            {
                var own = _buildings.Of(a.Side).ToArray();
                float average = own.Length == 0 ? 1f : own.Average(o => o.Dead ? 0f : o.Hp / o.MaxHp);
                d *= 1f + (1f - average) * R("fallenBuildings");
            }
            if (b != null)
            {
                d *= R(type == "crushing" ? "buildingCrushing" : type == "melee" ? "buildingMelee" : "buildingRanged");
                if (a.Def.IsSiege) d *= R("siege");
                if (a.Def.IsSiege && Passive(a.Side, "automata")) d *= R("automata");
                if (Passive(a.Side, "earthshaker")) d *= R("earthshaker");
                return d;
            }
            if (Passive(t.Side, "fortressDefense") && NearBuilding(t, _buildings.Of(t.Side, BuildingType.Fortress), R("fortressRange"))) d *= R("fortressDefense");
            if (Passive(a.Side, "warcry"))
            {
                int mine = 0, theirs = 1;
                foreach (var other in _units.Alive)
                {
                    if (other == t || Hex.Distance(other.Hex, t.Hex) > 1) continue;
                    if (other.Side == a.Side) mine++; else theirs++;
                }
                if (mine > theirs) d *= R("warcry");
            }
            if (CombatRates.Matches(Rates.categoryBeats, a.Def.cat, t.Def.cat)) d *= R("category");
            if (a.Def.GetFloat("antimyth") != 0f && t.Def.cat == "myth") d *= a.Def.GetFloat("antimyth");
            if (a.Def.GetFloat("bonusVsHero") != 0f && t.Def.cat == "hero") d *= a.Def.GetFloat("bonusVsHero");
            if (a.Def.GetFloat("bonusVsSerpent") != 0f && t.Def.GetBool("serpent")) d *= a.Def.GetFloat("bonusVsSerpent");
            if (Passive(a.Side, "fireDefeatsWater") && NearWater(t)) d *= R("fireDefeatsWater");
            if (Rates.armorDamageMultipliers)
            {
                if (CombatRates.Matches(Rates.damageStrong, type, t.Def.armor)) d *= R("armorStrong");
                else if (CombatRates.Matches(Rates.damageWeak, type, t.Def.armor)) d *= R("armorWeak");
            }
            if (type == "ranged" && _terrain[t.Hex] == TerrainType.Forest) d *= R("forestCover");
            if (Lake(t)) d *= R("lakeDefense");
            else if (_terrain[t.Hex] == TerrainType.Water && _context.FactionForSide(t.Side) != "egypt") d *= R("waterVulnerability");
            return d;
        }

        /// <summary>
        /// <c>onBuildingDestroyed</c>: every consequence of a building falling, in ONE place, so an
        /// alternate path (Poseidon's Earthquake, Odysseus's ambush) cannot miss one.
        /// </summary>
        private void OnBuildingDestroyed(Building b)
        {
            if (Passive(b.Side, "deathFavorSurge")) Grant(b.Side, favor: R("buildingDeathFavor"));
            for (int side = 0; side < 2; side++)
                if (Passive(side, "newGrowth")) Grant(side, food: R("newGrowthFood"));
            BuildingDestroyed?.Invoke(b);
        }

        /// <summary>
        /// <c>razeBuilding</c>: damage to a building from a POWER or an ambush rather than an attack.
        /// No <c>calcDmg</c> — there is no attacker to read multipliers from — but the ward is honoured,
        /// the damage is tallied for the tiebreaker, <c>lastHitAt</c> is stamped, and destruction runs the
        /// shared path. Returns the damage dealt; 0 means "not hit" (dead or warded), which is what
        /// Poseidon's cast counts.
        /// </summary>
        public float RazeBuilding(int side, Building b, float amount, float elapsed)
        {
            if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount))
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (b == null || b.Dead || elapsed < b.InvulnerableUntil) return 0f;
            float d = amount;
            if (Passive(side, "earthshaker")) d *= _db.Powers.Value("earthshaker"); // Poseidon's passive compounds with his power
            _buildingDamage[side] += b.TakeDamage(d);
            b.LastHitAt = elapsed;
            if (b.Dead) OnBuildingDestroyed(b);
            return d;
        }

        /// <summary>Add a remembered human death directly — a scripted scenario or a replayed fixture.</summary>
        public void RememberDeath(HumanDeath death) => _deadHumans.Add(death);

        /// <summary>
        /// Osiris's Field of Reeds: take the last <paramref name="max"/> human deaths of a side
        /// (or of BOTH sides, for Hades's Realm of the Dead, when <paramref name="side"/> is negative)
        /// within <paramref name="window"/> seconds, oldest first, and forget them.
        /// </summary>
        public List<HumanDeath> TakeRecentDeaths(int side, float elapsed, float window, int max)
        {
            var eligible = _deadHumans.Where(d => (side < 0 || d.Side == side) && elapsed - d.At < window).ToList();
            var pool = eligible.Skip(Math.Max(0, eligible.Count - max)).ToList();
            foreach (var d in pool) _deadHumans.Remove(d);
            return pool;
        }

        /// <summary>
        /// Raw god-power damage with the power death rules (Ra/Horus): Theseus retraces, Ajax
        /// makes his stand, a once-reviving unit — or a myth under Dionysus's Resurrection —
        /// comes back at the revive fraction, and anything else dies with no attacker rewards.
        /// Invulnerability is the CALLER's check, as in the browser branch.
        /// </summary>
        public void DealPowerDamage(Unit unit, float damage, float elapsed)
        {
            if (unit == null || unit.Dead) return;
            if (damage < unit.Hp) { unit.TakeDamage(damage); return; }
            if (unit.Def.GetBool("retracesOnDeath") && !unit.Combat.HasRevived) BeginRetreat(unit, elapsed);
            else if (unit.Def.GetBool("lastStandOnDeath") && !unit.Combat.LastStandUsed) BeginLastStand(unit, elapsed);
            else if ((unit.Def.GetBool("revivesOnce") || (unit.Def.cat == "myth" && Passive(unit.Side, "resurrection")))
                     && !unit.Combat.HasRevived)
            {
                unit.Combat.HasRevived = true;
                unit.RestoreHealth(unit.MaxHp * R("reviveHealth"));
            }
            else KillUnit(unit, elapsed);
        }

        /// <summary>Add to a side's purse, clamped to the caps. Negative amounts drain, floored at zero.</summary>
        public void Grant(int side, float food = 0f, float favor = 0f) => GrantInternal(side, food, favor);

        private void GrantInternal(int side, float food = 0f, float favor = 0f)
        {
            var purse = _context.Resources[side];
            _context.Resources[side] = new Purse(Math.Min(_db.FoodCap, Math.Max(0f, purse.Food + food)),
                Math.Min(_db.FavorCap, Math.Max(0f, purse.Favor + favor)));
        }

        // A faction's favorPerDeath economy trait, parsed once per faction id rather than on every
        // death. 0 for a faction without the trait (or an unparsable value, as before).
        private readonly Dictionary<string, float> _favorPerDeath = new Dictionary<string, float>();

        private float FavorPerDeath(string factionId)
        {
            factionId = factionId ?? "";
            if (_favorPerDeath.TryGetValue(factionId, out var cached)) return cached;
            var faction = _db.Faction(factionId);
            var value = faction?.economy?.Find(e => e.key == "favorPerDeath");
            float per = 0f;
            if (value != null && !float.TryParse(value.value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out per)) per = 0f;
            _favorPerDeath[factionId] = per;
            return per;
        }

        private void RecordDeath(Unit unit, float elapsed)
        {
            if (!_recordedDeaths.Add(unit)) return;
            if (unit.Def.cat == "human") _deadHumans.Add(new HumanDeath(unit, elapsed));
            int opponent = 1 - unit.Side;
            if (Passive(opponent, "inevitable")) Grant(opponent, favor: R("inevitableFavor"));
            for (int side = 0; side < 2; side++)
            {
                float per = FavorPerDeath(_context.FactionForSide(side));
                if (per != 0f) Grant(side, favor: per);
            }
            UnitDied?.Invoke(unit);
        }

        public bool KillUnit(Unit unit, float elapsed)
        {
            if (unit == null || unit.Dead) return false;
            unit.Kill();
            RecordDeath(unit, elapsed);
            return true;
        }

        /// <summary>Fortress-style raw damage: no attack modifiers, revivals or attacker rewards.</summary>
        public float DealRawDamage(Unit unit, float damage, float elapsed)
        {
            if (damage < 0f || float.IsNaN(damage) || float.IsInfinity(damage))
                throw new ArgumentOutOfRangeException(nameof(damage));
            if (unit == null || unit.Dead || elapsed < unit.Combat.InvulnerableUntil) return 0f;
            float absorbed = Math.Min(unit.Hp, damage);
            // Kill before setting HP to zero: KillUnit's duplicate-death guard reads Dead from HP.
            if (damage >= unit.Hp) KillUnit(unit, elapsed);
            else unit.TakeDamage(damage);
            return absorbed;
        }

        public void BeginRetreat(Unit unit, float elapsed)
        {
            unit.Combat.HasRevived = true;
            unit.RestoreHealth(unit.MaxHp * R("reviveHealth"));
            var walked = unit.Route?.Take(unit.RouteIndex).Reverse().ToArray();
            unit.SetRoute(walked != null && walked.Length > 0 ? walked : null);
            unit.Engagement = null;
            unit.Hold = false;
            if (unit.Def.GetBool("retreatInvuln") && unit.HasRoute)
            {
                unit.Combat.Retreating = true;
                unit.Combat.RetreatDeadline = elapsed + R("retreatMax");
                unit.Combat.InvulnerableUntil = elapsed + R("retreatWard");
            }
        }

        public void BeginLastStand(Unit unit, float elapsed)
        {
            unit.Combat.LastStandUsed = true;
            float until = elapsed + unit.Def.GetFloat("lastStandDur", R("lastStandDuration"));
            unit.RestoreHealth(Math.Max(1f, unit.MaxHp * R("lastStandHealth")));
            unit.Combat.LastStandEndsAt = until;
            unit.Combat.InvulnerableUntil = unit.RootUntil = unit.Combat.FrenzyUntil = until;
        }

        private void DirectDeath(Unit attacker, Unit target, float elapsed)
        {
            if (target.Def.GetBool("retracesOnDeath") && !target.Combat.HasRevived) { BeginRetreat(target, elapsed); return; }
            if (target.Def.GetBool("lastStandOnDeath") && !target.Combat.LastStandUsed) { BeginLastStand(target, elapsed); return; }
            if (target.Def.GetBool("revivesOnce") && !target.Combat.HasRevived)
            {
                target.Combat.HasRevived = true;
                target.RestoreHealth(target.MaxHp * R("reviveHealth"));
                return;
            }
            if (Passive(target.Side, "deathFavorNearTemple") && NearBuilding(target,
                _buildings.Of(target.Side, BuildingType.Temple), R("templeRange"))) Grant(target.Side, favor: R("templeFavor"));
            if (attacker.Side != target.Side && Passive(attacker.Side, "bloodFavor")) Grant(attacker.Side, favor: R("bloodFavor"));
            if (Passive(target.Side, "ferrymansToll")) Grant(target.Side, favor: R("ferrymanFavor"));
            if (Passive(target.Side, "earthMother")) Grant(target.Side, food: R("earthFood"));
            RecordDeath(target, elapsed);
            if (attacker.Def.GetBool("spawnsOnKill") && attacker.Side != target.Side &&
                _units.AliveOf(attacker.Side).Count(u => u.Key == attacker.Key) < R("cloneCap"))
            {
                // Browser's conjured-copy fallback checks occupancy only, including flying layer.
                foreach (var h in attacker.Hex.Neighbors())
                    if (_units.HasCapacity(h, attacker.Flying))
                    { _units.Spawn(attacker.Side, attacker.Def, h, bornAt: elapsed); break; }
            }
        }

        public float DealDamage(Unit attacker, Unit target, float elapsed) =>
            DealDamage(attacker, new MovementContact(target), elapsed);
        public float DealDamage(Unit attacker, Building target, float elapsed) =>
            DealDamage(attacker, new MovementContact(target), elapsed);

        /// <summary>Resolve one landed hit; attack scheduling and target selection belong to the caller.</summary>
        public float DealDamage(Unit attacker, MovementContact target, float elapsed)
        {
            if (attacker.Dead || target.Dead) return 0f;
            attacker.Combat.Invisible = false;
            if (target.IsBuilding && elapsed < target.Building.InvulnerableUntil) return 0f;
            var t = target.Unit;
            if (t != null && attacker.Def.GetFloat("drainsFavor") != 0f)
                Grant(t.Side, favor: -attacker.Def.GetFloat("drainsFavor"));
            float damage = CalculateDamage(attacker, target, elapsed);
            Hit?.Invoke(new HitReport(attacker, target, damage, elapsed));
            attacker.Combat.AmbushUsed = true;
            if (target.IsBuilding)
            {
                var b = target.Building;
                _buildingDamage[attacker.Side] += b.TakeDamage(damage);
                b.LastHitAt = elapsed;
                if (b.Dead) OnBuildingDestroyed(b);
                return damage;
            }
            t.TakeDamage(damage);
            if (attacker.Def.GetBool("slows")) t.SlowUntil = elapsed + R("slowDuration");
            float intoxicates = attacker.Def.GetFloat("intoxicates");
            if (intoxicates != 0f)
            {
                t.SlowUntil = Math.Max(t.SlowUntil, elapsed + intoxicates);
                t.Combat.WeakenUntil = Math.Max(t.Combat.WeakenUntil, elapsed + intoxicates);
            }
            if (attacker.Def.GetBool("poisons"))
            {
                t.Combat.PoisonUntil = elapsed + attacker.Def.GetFloat("poisonDur", R("poisonDuration"));
                t.Combat.PoisonDps = attacker.Def.GetFloat("poisonDps", R("poisonDps"));
            }
            if (Passive(attacker.Side, "pestilence")) t.Combat.WeakenUntil = elapsed + R("pestilenceDuration");
            if (attacker.Def.GetBool("disruptsRoute")) { t.SetRoute(null); t.Engagement = null; }
            if (t.Dead) DirectDeath(attacker, t, elapsed);
            float threshold = attacker.Def.GetFloat("executesBelow");
            if (!t.Dead && threshold != 0f && t.Hp < t.MaxHp * threshold) KillUnit(t, elapsed);

            // Cleave uses the attacker's visible logical position; chaos uses the victim's HEX.
            // Neither splash path triggers direct-hit revival or killer-only rewards.
            float splash = attacker.Def.GetFloat("splash");
            if (splash != 0f && attacker.Side != t.Side)
            {
                float radius = _units.Layout.Hex * HexLayout.Sqrt3 * R("splashRadius");
                foreach (var other in _units.All)
                {
                    if (other.Dead || other == t || other == attacker || other.Side != t.Side) continue;
                    float dx = other.Position.X - attacker.Position.X, dy = other.Position.Y - attacker.Position.Y;
                    bool near = radius > 0f ? dx * dx + dy * dy <= radius * radius : Hex.Distance(other.Hex, attacker.Hex) <= 1;
                    if (near) SplashHit(other, damage * splash, elapsed);
                }
            }
            float chaos = attacker.Def.GetFloat("chaosSplash");
            if (chaos != 0f)
                foreach (var other in _units.All)
                    if (!other.Dead && other != t && other != attacker && Hex.Distance(other.Hex, t.Hex) <= 1)
                        SplashHit(other, damage * chaos, elapsed);
            return damage;
        }

        private void SplashHit(Unit unit, float damage, float elapsed)
        {
            if (elapsed < unit.Combat.InvulnerableUntil) return;
            unit.TakeDamage(damage);
            if (unit.Dead) RecordDeath(unit, elapsed);
        }
    }
}
