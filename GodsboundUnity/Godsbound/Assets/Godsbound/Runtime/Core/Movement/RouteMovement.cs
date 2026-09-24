using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Pathfinding;
using Godsbound.Core.Units;

namespace Godsbound.Core.Movement
{
    /// <summary>A live engagement chosen by combat. Movement never chooses who to attack.</summary>
    public sealed class MovementContact
    {
        public Unit Unit { get; }
        public Building Building { get; }
        public bool Dead => Unit != null ? Unit.Dead : Building.Dead;
        public bool IsBuilding => Building != null;
        public IEnumerable<Hex> Hexes => Unit != null ? new[] { Unit.Hex } : Building.Hexes();

        /// <summary>Hex distance to the nearest cell of the contact. Allocation-free: this is asked
        /// several times per unit per frame, and the LINQ form allocated an array and a closure each time.</summary>
        public int Distance(Hex from)
        {
            if (Unit != null) return Hex.Distance(from, Unit.Hex);
            return Math.Min(Hex.Distance(from, Building.HexA), Hex.Distance(from, Building.HexB));
        }
        public MovementContact(Unit unit) { Unit = unit ?? throw new ArgumentNullException(nameof(unit)); }
        public MovementContact(Building building) { Building = building ?? throw new ArgumentNullException(nameof(building)); }
    }

    /// <summary>Match-owned faction, passive and temporary-grove lookups. Defaults mean no passives.</summary>
    public sealed class MovementContext
    {
        public Func<int, string> FactionForSide = side => "";
        public Func<int, string, bool> HasPassive = (side, key) => false;
        public Func<Unit, bool> InGrove = unit => false;
        public Func<int, float> GardenSpeed = side => 1f;
    }

    /// <summary>Pure simulation port of moveToward, followPath and the movement part of updateUnit.</summary>
    public sealed class RouteMovement
    {
        private readonly UnitField _units;
        private readonly BuildingMap _buildings;
        private readonly TerrainMap _terrain;
        private readonly PathFinder _paths;
        private readonly MovementRates _rates;
        private readonly MovementContext _context;
        private HexLayout Layout => _units.Layout;

        public RouteMovement(UnitField units, BuildingMap buildings, TerrainMap terrain,
                             PathFinder paths, MovementRates rates, MovementContext context = null)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _rates = rates ?? throw new ArgumentNullException(nameof(rates));
            _context = context ?? new MovementContext();
        }

        private bool Passive(Unit u, string key) => _context.HasPassive(u.Side, key);
        private bool Legal(Hex h, Unit u) => _terrain.RouteOk(h, u.Flying, _buildings.RouteBlocker);
        private bool Open(Hex h, Unit u) => Legal(h, u) && _units.HasCapacity(h, u, u.Flying);

        public float EffectiveSpeed(Unit u, float elapsed)
        {
            if (elapsed < u.StunUntil || elapsed < u.RootUntil) return 0f;
            float speed = u.Def.speed * _rates.baseSpeedMultiplier;
            if (elapsed < u.BuffUntil) speed *= u.BuffSpeed;
            if (elapsed < u.SlowUntil) speed *= _rates.slowMultiplier;
            if (Passive(u, "moveSpeed10")) speed *= _rates.moveSpeedMultiplier;
            if (Passive(u, "moonsGlow")) speed *= _rates.moonlightMultiplier;
            return speed;
        }

        public float TerrainMultiplier(Unit u)
        {
            var terrain = _terrain[u.Hex];
            bool water = terrain == TerrainType.Water;
            string faction = _context.FactionForSide(u.Side);
            bool bamboo = (terrain == TerrainType.Forest && faction == "china" &&
                           (int)Board.SideForRow(u.Hex.R) == u.Side) || _context.InGrove(u);
            bool lake = water && faction == "aztec" && _buildings.Of(u.Side)
                .Any(b => !b.Dead && b.Hexes().Any(h => Hex.Distance(h, u.Hex) == 1));
            bool exempt = u.Flying || (water && (faction == "egypt" || Passive(u, "tamedRivers"))) ||
                bamboo || lake || Passive(u, "clearingWind") ||
                (u.Def.cat == "hero" && Passive(u, "cloudSomersault"));
            float multiplier = exempt ? 1f : _terrain.SpeedAt(u.Hex.C, u.Hex.R);
            if (multiplier == 0f) multiplier = 1f; // browser's TINFO.speed || 1
            if (bamboo && Passive(u, "gardenOfKunlun")) multiplier *= _context.GardenSpeed(u.Side);
            if (water && u.Def.GetBool("fastInWater")) multiplier = _rates.fastWaterMultiplier;
            if ((Passive(u, "lordOfFourSeas") || Passive(u, "floodCurrent")) &&
                (water || u.Hex.Neighbors().Any(h => _terrain[h] == TerrainType.Water)))
                multiplier *= _rates.nearWaterMultiplier;
            return multiplier;
        }

        public bool MoveToward(Unit u, BoardPoint point, float dt, float elapsed)
        {
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                throw new ArgumentOutOfRangeException(nameof(dt));
            if (u.Dead || dt == 0f) return false;
            float speed = EffectiveSpeed(u, elapsed) * Layout.Hex * HexLayout.Sqrt3;
            if (speed <= 0f) return false;
            float dx = point.X - u.Position.X, dy = point.Y - u.Position.Y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float step = speed * TerrainMultiplier(u) * dt;
            if (distance <= Math.Max(step, _rates.snapDistanceInHexes * Layout.Hex))
            {
                u.MoveTo(point);
                return true;
            }
            u.MoveTo(new BoardPoint(u.Position.X + dx / distance * step,
                                    u.Position.Y + dy / distance * step));
            return false;
        }

        /// <returns>True only when an engagement is in range and the unit has finished arriving.
        /// The combat system may attack then. Receiving damage never changes this movement decision.</returns>
        public bool Tick(Unit u, float dt, float elapsed)
        {
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0f || u.Dead || u.Def.GetBool("inert")) return false;
            // A committed sidestep owns its destination immediately, just like route movement.
            // Finish it before another decision so two movers cannot claim the same hex.
            if (u.FinishingSidestep)
            {
                if (MoveToward(u, Layout.Center(u.Hex), dt, elapsed))
                { u.Arrived = true; u.FinishingSidestep = false; u.DeviateSeconds = 0f; }
                return false;
            }
            var target = u.Engagement;
            if (target != null && (target.Dead || (u.Def.GetBool("targetsBuildings") && !target.IsBuilding)))
                u.Engagement = target = null;
            if (target != null)
            {
                int distance = target.Distance(u.Hex), range = Math.Max(u.Def.range, 1);
                if (distance <= range)
                {
                    u.DeviateSeconds = 0f;
                    if (!u.Arrived)
                    {
                        if (MoveToward(u, Layout.Center(u.Hex), dt, elapsed)) u.Arrived = true;
                        return false;
                    }
                    return elapsed >= u.StunUntil;
                }
                bool blocker = !target.IsBuilding && u.HasRoute && target.Distance(u.RoutePath[u.RouteIndex]) <= 1;
                bool eligible = u.Def.range <= 1 && distance <= range + 1 && !target.IsBuilding &&
                                ((blocker && !u.Sidestepped) || u.Free);
                if (eligible)
                {
                    bool deviating = blocker && !u.Free;
                    u.DeviateSeconds = deviating ? u.DeviateSeconds + dt : 0f;
                    if (!deviating || u.DeviateSeconds >= _rates.deviateDelay)
                    {
                        var step = u.Hex.Neighbors().Where(h => Open(h, u))
                            .OrderBy(h => target.Distance(h)).Select(h => (Hex?)h).FirstOrDefault();
                        if (step.HasValue && target.Distance(step.Value) < distance)
                        {
                            u.Sidestepped = blocker;
                            StepAside(u, step.Value, dt, elapsed);
                            return false;
                        }
                    }
                }
                else u.DeviateSeconds = 0f;
                if (u.Free && target.IsBuilding)
                {
                    FollowPath(u, dt, elapsed);
                    return false;
                }
                u.Engagement = null;
            }
            else u.DeviateSeconds = 0f;
            if (!u.Hold && elapsed >= u.StunUntil && (!u.Free || u.HasRoute))
                FollowPath(u, dt, elapsed);
            return false;
        }

        private void StepAside(Unit u, Hex hex, float dt, float elapsed)
        {
            u.EnterHex(hex);
            u.Arrived = false;
            u.FinishingSidestep = true;
            if (MoveToward(u, Layout.Center(hex), dt, elapsed))
            { u.Arrived = true; u.FinishingSidestep = false; u.DeviateSeconds = 0f; }
        }

        public void FollowPath(Unit u, float dt, float elapsed)
        {
            if (!u.HasRoute) { u.RoutePath = null; u.Free = true; return; }
            Hex next = u.RoutePath[u.RouteIndex];
            bool entering = u.Hex != next;
            if (entering && (Hex.Distance(u.Hex, next) > 1 || !Legal(next, u)))
            {
                var back = _paths.FindPath(u.Hex, next, u.Flying);
                if (back != null && back.Count > 2)
                    u.RoutePath.InsertRange(u.RouteIndex, back.Skip(1).Take(back.Count - 2));
                else u.RouteIndex++;
                u.Sidestepped = false;
                return;
            }
            u.Sidestepped = false;
            if (entering && !_units.HasCapacity(next, u, u.Flying))
            {
                var blocker = _units.All.FirstOrDefault(o => !o.Dead && o != u && o.Hex == next &&
                                                           o.Side == u.Side && o.Flying == u.Flying);
                var joint = blocker?.Engagement;
                if (joint != null && !joint.Dead && !(u.Def.GetBool("targetsBuildings") && !joint.IsBuilding))
                {
                    if (joint.Distance(u.Hex) <= Math.Max(u.Def.range, 1))
                    { u.Engagement = joint; u.WaitSeconds = 0f; u.AssistFrom = null; return; }
                    var step = u.Hex.Neighbors().Where(h => Open(h, u) && h != u.AssistFrom)
                        .OrderBy(h => joint.Distance(h)).Select(h => (Hex?)h).FirstOrDefault();
                    if (step.HasValue && joint.Distance(step.Value) <= joint.Distance(u.Hex))
                    {
                        u.Engagement = joint;
                        u.AssistFrom = u.Hex;
                        StepAside(u, step.Value, dt, elapsed);
                        u.WaitSeconds = 0f;
                        return;
                    }
                }
                // Do not detour around a friendly that is itself following a route.
                if (blocker != null && blocker.HasRoute)
                { u.AssistFrom = null; u.WaitSeconds = 0f; return; }
                u.AssistFrom = null;
                u.WaitSeconds += dt;
                if (u.WaitSeconds > _rates.congestionDelay)
                {
                    var alternate = u.Hex.Neighbors().Where(h => Open(h, u) && Hex.Distance(h, next) <= 1 &&
                                                                 h != u.LastHex).Select(h => (Hex?)h).FirstOrDefault();
                    if (alternate.HasValue)
                    { u.RoutePath.Insert(u.RouteIndex, alternate.Value); u.WaitSeconds = 0f; }
                    else u.WaitSeconds = _rates.congestionRetry;
                }
                return;
            }
            u.WaitSeconds = 0f;
            u.AssistFrom = null;
            if (entering) { u.LastHex = u.Hex; u.EnterHex(next); u.Arrived = false; }
            if (MoveToward(u, Layout.Center(next), dt, elapsed)) { u.RouteIndex++; u.Arrived = true; }
        }
    }
}
