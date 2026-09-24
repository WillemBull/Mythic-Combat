using System.Linq;
using Godsbound.Core.Buildings;

namespace Godsbound.Core.Units
{
    /// <summary>
    /// Where a newly trained unit actually appears when its exit hex is taken.
    /// </summary>
    /// <remarks>
    /// <para>Ports the emergency-placement branch of <c>tickTraining</c>. The rule exists to
    /// honour a hard requirement from HANDOFF: <b>training must never stall while the owner
    /// has any standing building</b>, even when the exit hex is occupied. A queue that
    /// blocked on a crowded doorway would let a player wall in their own barracks.</para>
    /// <para>So when the exit hex is at capacity, the unit emerges ON a standing friendly
    /// building — its source if that still stands, otherwise the owner's nearest surviving
    /// one — choosing whichever of that building's two hexes is closest to the intended
    /// exit. That deliberately ignores capacity: stacking on a building is the escape valve,
    /// and refusing it here would reintroduce the stall.</para>
    /// <para>Only when the side has NO standing building at all does placement fail, and the
    /// caller then retries shortly rather than dropping the unit.</para>
    /// </remarks>
    public static class SpawnPlacement
    {
        /// <summary>
        /// The hex a unit should appear on, or <c>null</c> to retry later.
        /// </summary>
        /// <param name="intended">The exit hex the training preview promised.</param>
        /// <param name="side">Owner of the unit.</param>
        /// <param name="source">
        /// The building that trained it, or null. Ignored if it has been destroyed.
        /// </param>
        /// <param name="flying">Which capacity layer the unit belongs to.</param>
        public static Hex? Resolve(Hex intended, int side, Building source, bool flying,
                                   UnitField units, BuildingMap buildings)
        {
            if (units == null) throw new System.ArgumentNullException(nameof(units));
            if (buildings == null) throw new System.ArgumentNullException(nameof(buildings));
            if (!Board.InBounds(intended))
                throw new System.ArgumentOutOfRangeException(nameof(intended), $"{intended} is off the board");

            // The normal case: the doorway is clear.
            if (units.HasCapacity(intended, flying)) return intended;

            // Blocked. Fall back to a standing building — the source if it survives.
            var host = source != null && !source.Dead
                ? source
                : buildings.Of(side)
                           .Where(b => !b.Dead)
                           .OrderBy(b => Hex.Distance(b.HexA, intended))
                           .FirstOrDefault();

            // No standing building: the caller must retry, never drop the unit.
            if (host == null) return null;

            // Whichever of the host's two hexes is nearer the intended exit.
            return host.Hexes()
                       .OrderBy(h => Hex.Distance(h, intended))
                       .First();
        }

        /// <summary>
        /// True when placement will succeed for this side right now — i.e. it has any
        /// standing building, or the exit hex is free.
        /// </summary>
        public static bool CanPlace(Hex intended, int side, bool flying,
                                    UnitField units, BuildingMap buildings) =>
            units.HasCapacity(intended, flying) || buildings.StandingCount(side) > 0;
    }
}
