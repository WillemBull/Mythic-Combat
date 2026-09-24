using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;

namespace Godsbound.Core.Units
{
    /// <summary>
    /// Every live unit, and the occupancy rule that governs where they can stand.
    /// </summary>
    /// <remarks>
    /// <para><b>Capacity is one unit per hex PER LAYER.</b> A ground unit and a flying unit
    /// may share a hex; two ground units or two flying units may not. Ports
    /// <c>hexLoad(c, r, except, flying)</c> against <c>HEX_CAPACITY</c>, which
    /// <see cref="Board.HexCapacity"/> carries.</para>
    /// <para>Dead units never count toward capacity, so a corpse does not block the hex it
    /// fell on.</para>
    /// <para>The <c>except</c> parameter matters more than it looks: a unit testing whether
    /// it can step somewhere must not count itself, or a unit already standing in a hex
    /// would find its own cell full.</para>
    /// </remarks>
    public sealed class UnitField
    {
        private readonly List<Unit> _units = new List<Unit>();
        private readonly HexLayout _layout;
        private int _nextId;

        public UnitField(HexLayout layout = null)
        {
            _layout = layout ?? HexLayout.Unit();
        }

        public HexLayout Layout => _layout;

        public IReadOnlyList<Unit> All => _units;

        public IEnumerable<Unit> Alive => _units.Where(u => !u.Dead);

        public IEnumerable<Unit> AliveOf(int side) => _units.Where(u => !u.Dead && u.Side == side);

        public int AliveCount => _units.Count(u => !u.Dead);

        /// <summary>Create a unit and add it to the field. Does NOT check capacity —
        /// emergency placement is allowed to stack, so the caller decides.</summary>
        public Unit Spawn(int side, UnitData def, Hex hex, bool free = true, float bornAt = 0f)
        {
            var u = new Unit(++_nextId, side, def, hex, _layout, free, bornAt);
            _units.Add(u);
            return u;
        }

        public void Add(Unit u)
        {
            if (u == null) throw new System.ArgumentNullException(nameof(u));
            _units.Add(u);
        }

        /// <summary>Drop dead units from the field. Call after death bookkeeping.</summary>
        public int RemoveDead() => _units.RemoveAll(u => u.Dead);

        public void Clear()
        {
            _units.Clear();
            _nextId = 0;
        }

        /// <summary>
        /// How many living units occupy this hex on the given layer. Ports <c>hexLoad</c>.
        /// </summary>
        /// <param name="except">A unit to disregard — normally the one asking.</param>
        public int HexLoad(Hex hex, Unit except, bool flying)
        {
            int load = 0;
            for (int i = 0; i < _units.Count; i++)
            {
                var u = _units[i];
                if (u.Dead || u == except) continue;
                if (u.Hex != hex) continue;
                if (u.Flying != flying) continue;
                load++;
            }
            return load;
        }

        public int HexLoad(Hex hex, bool flying) => HexLoad(hex, null, flying);

        /// <summary>True when another unit of this layer could stand here.</summary>
        public bool HasCapacity(Hex hex, Unit except, bool flying) =>
            HexLoad(hex, except, flying) < Board.HexCapacity;

        public bool HasCapacity(Hex hex, bool flying) => HasCapacity(hex, null, flying);

        /// <summary>The living unit occupying this hex on the given layer, or null.</summary>
        public Unit At(Hex hex, bool flying) =>
            _units.FirstOrDefault(u => !u.Dead && u.Hex == hex && u.Flying == flying);

        /// <summary>Every living unit on this hex, both layers.</summary>
        public IEnumerable<Unit> AllAt(Hex hex) => _units.Where(u => !u.Dead && u.Hex == hex);

        /// <summary>
        /// Living enemies of <paramref name="side"/> within <paramref name="range"/> hexes
        /// of <paramref name="from"/>, nearest first.
        /// </summary>
        public IEnumerable<Unit> EnemiesWithin(Hex from, int side, int range) =>
            _units.Where(u => !u.Dead && u.Side != side && Hex.Distance(u.Hex, from) <= range)
                  .OrderBy(u => Hex.Distance(u.Hex, from));
    }
}
