using System;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Match;
using Godsbound.Core.Units;

namespace Godsbound.Core.Gods
{
    /// <summary>
    /// Per-frame god upkeep, ported from the first and last loops of <c>tickGodEffects</c>.
    /// </summary>
    /// <remarks>
    /// <para>In U26: poison ticks (stopped by invulnerability), Apollo's Lyre, Isis's hero regen,
    /// favor/food-generating units, conjured units vanishing at <c>despawnAt</c> (no death
    /// rewards — it is not a death), and the damage aura (Huo Shu).</para>
    /// <para>U27 adds Jade Emperor's conscription expiry, <c>tickFloods</c> (fire damage and
    /// overlay expiry) and <c>tickThundersWarning</c>, run in that browser order by
    /// <see cref="Tick"/>. U28 adds the generic hero sweep (<see cref="TickHeroes"/>) between the
    /// upkeep and aura loops, exactly where the browser runs it.</para>
    /// </remarks>
    public sealed class GodEffects
    {
        private readonly MatchState _state;

        public GodEffects(MatchState state) { _state = state ?? throw new ArgumentNullException(nameof(state)); }

        private float R(string key) => _state.Database.Powers.Value(key);

        /// <summary><c>tickGodEffects</c>, then <c>tickFloods</c>, then <c>tickThundersWarning</c>.</summary>
        public void Tick(float dt, float elapsed)
        {
            TickUpkeep(dt, elapsed);
            TickFloods(dt, elapsed);
            TickThundersWarning(elapsed);
        }

        /// <summary><c>tickFloods</c>: burning tiles hurt whoever stands there, then due tiles revert.</summary>
        public void TickFloods(float dt, float elapsed)
        {
            var overlays = _state.Terrain.Overlays;
            var units = _state.Units.All;
            for (int i = overlays.Count - 1; i >= 0; i--)
            {
                var o = overlays[i];
                if (o.DamagePerSecond != 0f)
                    for (int j = 0; j < units.Count; j++)
                    {
                        var u = units[j];
                        if (u.Dead || u.Hex != o.Hex || elapsed < u.Combat.InvulnerableUntil) continue;
                        float dmg = o.DamagePerSecond * dt;
                        if (dmg >= u.Hp) _state.Combat.KillUnit(u, elapsed);
                        else u.TakeDamage(dmg);
                    }
                _state.Terrain.TryExpire(o, elapsed);
            }
        }

        /// <summary>Lei Gong's Thunder's Warning: enemies near a standing building of the passive's owner are slowed.</summary>
        public void TickThundersWarning(float elapsed)
        {
            for (int side = 0; side < 2; side++)
            {
                if (!_state.HasPassive(side, "thundersWarning")) continue;
                var mine = _state.Buildings.Of(side).Where(b => !b.Dead).ToList();
                if (mine.Count == 0) continue;
                foreach (var u in _state.Units.All)
                {
                    if (u.Dead || u.Side != 1 - side) continue;
                    if (mine.Any(b => b.Hexes().Min(h => Hex.Distance(u.Hex, h)) <= R("warningRange")))
                        u.SlowUntil = elapsed + R("warningSlow");
                }
            }
        }

        /// <summary>
        /// The generic hero-ability sweep of <c>tickGodEffects</c>, between the upkeep and aura loops.
        /// </summary>
        /// <remarks>
        /// Every block is driven by a DEF FLAG, never by a unit's name, so a future unit gets the
        /// behaviour by carrying the flag. The loop re-reads the list length each step, as the
        /// browser's <c>for…of</c> does, so a body appended here (Orpheus's shade, Ariadne's twin) is
        /// visited in the same frame — which is what keeps the twin from splitting again.
        /// </remarks>
        public void TickHeroes(float dt, float elapsed)
        {
            var units = _state.Units.All;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Dead) continue;
                // Ajax: Last Stand is TRIGGERED at the death sites; this only runs the clock out.
                if (u.Combat.LastStandEndsAt != 0f && elapsed >= u.Combat.LastStandEndsAt)
                {
                    u.Combat.LastStandEndsAt = 0f;
                    u.Combat.InvulnerableUntil = 0f;
                    _state.Combat.KillUnit(u, elapsed);
                    continue;
                }
                // Theseus: while the Clew pulls him home he cannot be killed. It ends the moment the
                // route runs out; the deadline is only a backstop, so a blocked path can never leave
                // a unit permanently untouchable.
                if (u.Combat.Retreating)
                {
                    if (u.HasRoute && elapsed < u.Combat.RetreatDeadline)
                        u.Combat.InvulnerableUntil = Math.Max(u.Combat.InvulnerableUntil, elapsed + R("retreatRefresh"));
                    else
                    {
                        u.Combat.Retreating = false;
                        u.Combat.InvulnerableUntil = 0f;
                        u.Combat.RetreatDeadline = 0f;
                    }
                }
                // Orpheus: Song of the Dead. A shade joins the procession on a timer while he lives.
                float summonsEvery = u.Def.GetFloat("summonsEvery");
                if (summonsEvery != 0f)
                {
                    u.SummonTimer += dt;
                    if (u.SummonTimer >= summonsEvery)
                    {
                        u.SummonTimer = 0f;
                        var spot = Beside(u, false);
                        var def = _state.Database.ResolveUnit(_state.FactionFor(u.Side), u.Def.GetString("summonKey"));
                        if (spot.HasValue && def != null)
                            _state.Units.Spawn(u.Side, def, spot.Value, free: true, bornAt: elapsed);
                    }
                }
                // Perseus: Gorgon's Head. The first enemy to come into his reach, and everything in
                // the line beyond it, turns to stone. Once per life.
                float gorgon = u.Def.GetFloat("gorgonStun");
                if (gorgon != 0f && !u.GorgonUsed && u.Engagement != null && !u.Engagement.IsBuilding &&
                    u.Engagement.Distance(u.Hex) <= Math.Max(u.Def.range, 1))
                {
                    u.GorgonUsed = true;
                    var aim = u.Engagement.Unit.Hex;
                    int dx = Math.Sign(aim.C - u.Hex.C), dy = Math.Sign(aim.R - u.Hex.R);
                    foreach (var e in units)
                    {
                        if (e.Dead || e.Side == u.Side || e.Def.GetBool("allyTargetOnly")) continue;
                        if (Hex.Distance(u.Hex, e.Hex) > R("gorgonReach")) continue; // the line behind it, not the board
                        if (Math.Sign(e.Hex.C - u.Hex.C) != dx || Math.Sign(e.Hex.R - u.Hex.R) != dy) continue;
                        e.StunUntil = elapsed + gorgon;
                    }
                }
                // Odysseus: Nobody. Unseen until he is at a wall, then one strike and he is just a man.
                // The veil is hung ONCE, at birth: a swing of his own breaks it (dealDamage clears
                // Invisible), and breaking cover to fight therefore COSTS him the ambush.
                if (u.Def.GetBool("stalks") && !u.StalkSpent)
                {
                    if (!u.StalkHung) { u.StalkHung = true; u.Combat.Invisible = true; }
                    var wall = !u.Combat.Invisible ? null : _state.Buildings.All
                        .Where(b => b.Side != u.Side && !b.Dead &&
                                    b.Hexes().Min(h => Hex.Distance(u.Hex, h)) <= Math.Max(u.Def.range, 1))
                        .OrderBy(b => b.Hexes().Min(h => Hex.Distance(u.Hex, h))).FirstOrDefault();
                    if (wall != null)
                    {
                        u.StalkSpent = true;
                        u.Combat.Invisible = false;
                        _state.Combat.RazeBuilding(u.Side, wall,
                            u.Def.GetFloat("ambushBuildingDmg", R("ambushDefault")), elapsed);
                    }
                    else if (!u.Combat.Invisible) u.StalkSpent = true; // he swung on the way: no ambush, no second veil
                }
                // Heracles: Divine Rage on Hera's clock, not the player's.
                float rages = u.Def.GetFloat("ragesEvery");
                if (rages != 0f)
                {
                    u.RageTimer += dt;
                    if (u.RageTimer >= rages)
                    {
                        u.RageTimer = 0f;
                        u.Combat.FrenzyUntil = elapsed + u.Def.GetFloat("rageDur", R("rageDefault"));
                        u.Engagement = null; // he stops caring what he was doing
                    }
                }
                // Ariadne: Labyrinthine Split. At the end of her drawn route she becomes two, each at
                // a fraction of the health — more bodies, less staying power.
                float frac = u.Def.GetFloat("splitsAtRouteEnd");
                if (frac != 0f && !u.Split && u.Free)
                {
                    u.Split = true;
                    var spot = Beside(u, u.Flying);
                    if (spot.HasValue)
                    {
                        float half = GodRules.JsRound(u.MaxHp * (double)frac);
                        var twin = _state.Units.Spawn(u.Side, u.Def, spot.Value, free: true, bornAt: elapsed);
                        twin.Split = true;
                        twin.SetMaxHp(half, Math.Min(half, u.Hp));
                        u.SetMaxHp(half, Math.Min(u.Hp, half));
                    }
                }
            }
        }

        /// <summary>The first neighbouring hex a new body may legally stand on, in neighbour order.</summary>
        private Hex? Beside(Unit u, bool flying)
        {
            foreach (var h in u.Hex.Neighbors())
                if (_state.Terrain.RouteOk(h, flying, _state.Buildings.RouteBlocker) &&
                    _state.Units.HasCapacity(h, flying)) return h;
            return null;
        }
        /// <summary>The per-unit and aura loops of <c>tickGodEffects</c>.</summary>
        public void TickUpkeep(float dt, float elapsed)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            var units = _state.Units.All;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Dead) continue;
                if (elapsed < u.Combat.PoisonUntil && elapsed >= u.Combat.InvulnerableUntil)
                {
                    float dmg = u.Combat.PoisonDps * dt;
                    if (dmg >= u.Hp) { _state.Combat.KillUnit(u, elapsed); continue; }
                    u.TakeDamage(dmg);
                }
                if (_state.HasPassive(u.Side, PassiveKeys.Lyre) && u.Hp < u.MaxHp)
                {
                    var temple = _state.Buildings.Of(u.Side, BuildingType.Temple);
                    if (temple != null && !temple.Dead && temple.Hexes().Min(h => Hex.Distance(u.Hex, h)) <= R("lyreRange"))
                        u.Heal(R("lyreHeal") * dt);
                }
                if (u.Def.cat == "hero" && _state.HasPassive(u.Side, "heroRegen") && u.Hp < u.MaxHp)
                    u.Heal(u.MaxHp * R("heroRegen") * dt);
                float favor = u.Def.GetFloat("generatesFavor"), food = u.Def.GetFloat("generatesFood");
                if (favor != 0f || food != 0f) _state.Combat.Grant(u.Side, food * dt, favor * dt);
                if (u.DespawnAt != 0f && elapsed >= u.DespawnAt) u.Kill();
                if (u.ConscriptedUntil != 0f && elapsed >= u.ConscriptedUntil) u.ReleaseConscript();
            }
            TickHeroes(dt, elapsed);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                float aura = u.Def.GetFloat("auraDps");
                if (u.Dead || aura == 0f) continue;
                float range = u.Def.GetFloat("auraRange", R("auraRange"));
                for (int j = 0; j < units.Count; j++)
                {
                    var e = units[j];
                    if (e.Dead || e == u || e.Side == u.Side) continue;
                    if (Hex.Distance(u.Hex, e.Hex) > range) continue;
                    if (elapsed < e.Combat.InvulnerableUntil) continue;
                    float dmg = aura * dt;
                    if (dmg >= e.Hp) _state.Combat.KillUnit(e, elapsed);
                    else e.TakeDamage(dmg);
                }
            }
        }
    }
}
