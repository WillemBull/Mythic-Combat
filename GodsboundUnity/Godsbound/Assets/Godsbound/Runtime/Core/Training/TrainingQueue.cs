using System;
using System.Collections.Generic;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Units;

namespace Godsbound.Core.Training
{
    /// <summary>One unit paid for and waiting to appear.</summary>
    public sealed class TrainingOrder
    {
        public int Side { get; }
        public UnitData Def { get; }

        /// <summary>The hex the drag preview promised the unit would emerge on.</summary>
        public Hex ExitHex { get; }

        /// <summary>
        /// The building that trained it. May be destroyed later, which
        /// <see cref="SpawnPlacement"/> handles.
        /// </summary>
        public Building Source { get; }
        public Building Goal { get; }
        public string Duty { get; }

        public IReadOnlyList<Hex> Route { get; }

        /// <summary>Elapsed match time at which this unit is due.</summary>
        public float ReadyAt { get; private set; }

        /// <summary>
        /// Bodies this order produces. 2 when Ptah's Creator's Word was spent on it; the extra
        /// copy stands free on the same hex with the same duty and goal.
        /// </summary>
        public int SpawnCount { get; set; } = 1;

        /// <summary>How many times placement has been deferred. Diagnostic.</summary>
        public int Deferrals { get; private set; }

        public TrainingOrder(int side, UnitData def, Hex exitHex, Building source, float readyAt,
                             IEnumerable<Hex> route = null, Building goal = null, string duty = null)
        {
            Side = side;
            Def = def ?? throw new System.ArgumentNullException(nameof(def));
            ExitHex = exitHex;
            Source = source;
            Goal = goal;
            Duty = duty;
            ReadyAt = readyAt;
            Route = route == null ? null : new List<Hex>(route).AsReadOnly();
        }

        internal void Defer(float seconds)
        {
            ReadyAt += seconds;
            Deferrals++;
        }

        public override string ToString() =>
            $"side {Side} {Def.key} -> {ExitHex} at t={ReadyAt:0.##}" +
            (Deferrals > 0 ? $" (deferred x{Deferrals})" : "");
    }

    /// <summary>
    /// The training queue, ported from <c>queueTrain</c> and <c>tickTraining</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>The load-bearing rule: training must never stall while the owner has any
    /// standing building.</b> A queue that blocked on a crowded doorway would let a player
    /// wall in their own barracks and freeze their own production. So when the exit hex is
    /// occupied the unit emerges on a standing building instead
    /// (<see cref="SpawnPlacement"/>), and only when nothing at all is standing is the order
    /// deferred — by <see cref="TrainingRates.blockedExitRetrySeconds"/>, never dropped.</para>
    /// <para>Payment happens at ENQUEUE, matching the browser game: the drag is released,
    /// the resources leave the purse, and the order is committed. A deferred order stays
    /// paid for.</para>
    /// </remarks>
    public sealed class TrainingQueue
    {
        private readonly List<TrainingOrder> _orders = new List<TrainingOrder>();
        private readonly TrainingRates _rates;

        public TrainingQueue(TrainingRates rates)
        {
            _rates = rates ?? throw new System.ArgumentNullException(nameof(rates));
        }

        public IReadOnlyList<TrainingOrder> Pending => _orders;

        /// <summary>
        /// Raised for each body as it appears, before the caller sees the list. Hephaestus's Forge
        /// subscribes: its charge is spent at CREATION, so a reinforced unit stays reinforced.
        /// </summary>
        public event System.Action<Unit> Spawned;

        /// <summary>Raised when an order is paid for, with the side and what it cost. U34's stats read it.</summary>
        public event System.Action<int, float, float> Paid;

        public int PendingCount => _orders.Count;

        public IEnumerable<TrainingOrder> PendingFor(int side)
        {
            foreach (var o in _orders) if (o.Side == side) yield return o;
        }

        /// <summary>
        /// Pay for a unit and queue it. Returns false and leaves the purse untouched when
        /// the side cannot afford it.
        /// </summary>
        public bool TryEnqueue(Purse purse, int side, UnitData def, Hex exitHex, Building source,
                               TrainingModifiers mods, float elapsed,
                               out Purse remaining, out TrainingOrder order, IEnumerable<Hex> route = null,
                               Building goal = null, float? startedAt = null, string duty = null)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (!Board.InBounds(exitHex))
                throw new System.ArgumentOutOfRangeException(nameof(exitHex), $"{exitHex} is off the board");

            remaining = purse;
            order = null;

            if (!UnitPrice.CanAfford(purse, def, mods, _rates)) return false;

            remaining = UnitPrice.Pay(purse, def, mods, _rates);
            Paid?.Invoke(side, purse.Food - remaining.Food, purse.Favor - remaining.Favor);
            order = new TrainingOrder(side, def, exitHex, source,
                                      elapsed + System.Math.Max(0f, UnitPrice.TrainSeconds(def, mods, _rates) -
                                          (startedAt.HasValue ? System.Math.Max(0f, elapsed - startedAt.Value) : 0f)), route, goal, duty);
            _orders.Add(order);
            return true;
        }

        /// <summary>
        /// Queue a body nobody paid for, at an exact time: Mictlantecuhtli's Bargain, whose price was
        /// the cast. <c>queueTrain</c> with an explicit <c>readyAt</c>, which also bypasses Hermes's
        /// delivery discount — a caller stating when it wants the unit is left alone.
        /// </summary>
        public TrainingOrder Enqueue(int side, UnitData def, Hex exitHex, Building source, float readyAt,
                                    IEnumerable<Hex> route = null, Building goal = null, string duty = null)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (!Board.InBounds(exitHex))
                throw new System.ArgumentOutOfRangeException(nameof(exitHex), $"{exitHex} is off the board");
            var order = new TrainingOrder(side, def, exitHex, source, readyAt, route, goal, duty);
            _orders.Add(order);
            return order;
        }

        /// <summary>
        /// Complete every order whose time has come, spawning units.
        /// </summary>
        /// <returns>The units in creation order, matching the browser's backwards queue walk.</returns>
        public List<Unit> Tick(float elapsed, UnitField units, BuildingMap buildings)
        {
            if (units == null) throw new System.ArgumentNullException(nameof(units));
            if (buildings == null) throw new System.ArgumentNullException(nameof(buildings));

            var spawned = new List<Unit>();

            // Iterate backwards: completed orders are removed as we go.
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                var o = _orders[i];
                if (elapsed < o.ReadyAt) continue;

                var at = SpawnPlacement.Resolve(o.ExitHex, o.Side, o.Source,
                                                o.Def.IsFlying, units, buildings);

                if (at == null)
                {
                    // Nothing standing to emerge on. Wait and try again — the unit is
                    // already paid for and must not be lost.
                    o.Defer(_rates.blockedExitRetrySeconds);
                    continue;
                }

                for (int k = 0; k < Math.Max(1, o.SpawnCount); k++)
                {
                    var u = units.Spawn(o.Side, o.Def, at.Value, free: true, bornAt: elapsed);
                    if (k == 0 && o.Route != null) u.SetRoute(o.Route);
                    u.LockedGoal = o.Goal;
                    u.Duty = o.Duty;
                    Spawned?.Invoke(u);
                    spawned.Add(u);
                }
                _orders.RemoveAt(i);
            }

            return spawned;
        }

        /// <summary>Drop every pending order. Used by match reset.</summary>
        public void Clear() => _orders.Clear();
    }
}
