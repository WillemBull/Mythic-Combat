namespace Godsbound.Core.Buildings
{
    /// <summary>
    /// Where a building may legally stand, ported from <c>canPlaceBuilding</c>.
    /// </summary>
    /// <remarks>
    /// <para>The rule: two hexes, both on the board, adjacent by <see cref="Hex.Distance"/>,
    /// neither mountain nor water, and neither already occupied by a DIFFERENT building.
    /// Occupied-by-itself is legal so a building can be re-placed onto its own hexes.</para>
    /// <para><b>Note the browser game does NOT check side ownership here.</b> Half
    /// restriction is enforced by the setup UI, which only ever edits the player's own
    /// six-row half. <see cref="IsInHalf"/> is provided separately rather than folded in, so
    /// the ported rule stays faithful and the extra constraint is opt-in.</para>
    /// </remarks>
    public static class BuildingPlacement
    {
        /// <summary>
        /// Whether <paramref name="a"/> and <paramref name="b"/> are a legal footprint.
        /// </summary>
        /// <param name="ignoring">
        /// A building allowed to already occupy these hexes — pass the one being moved.
        /// </param>
        public static bool CanPlace(Hex a, Hex b, TerrainMap terrain, BuildingMap buildings,
                                    Building ignoring = null)
        {
            if (terrain == null) throw new System.ArgumentNullException(nameof(terrain));

            if (!Board.InBounds(a) || !Board.InBounds(b)) return false;
            if (Hex.Distance(a, b) != 1) return false;

            foreach (var h in new[] { a, b })
            {
                var t = terrain[h];
                if (t == TerrainType.Mountain || t == TerrainType.Water) return false;

                var occupant = buildings?.AnyAt(h);
                if (occupant != null && occupant != ignoring) return false;
            }

            return true;
        }

        /// <summary>Both hexes lie in the given side's half — no-man's land does not count.</summary>
        public static bool IsInHalf(Hex a, Hex b, int side)
        {
            var want = side == 0 ? BoardSide.Player : BoardSide.Ai;
            return Board.SideForRow(a.R) == want && Board.SideForRow(b.R) == want;
        }

        /// <summary>
        /// Every legal footprint for a side, as ordered hex pairs. Each unordered pair
        /// appears once. Useful for setup UI and for exhaustive tests.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<(Hex a, Hex b)> LegalFootprints(
            int side, TerrainMap terrain, BuildingMap buildings, Building ignoring = null)
        {
            foreach (var a in Board.AllCells())
                foreach (var b in a.Neighbors())
                {
                    // Each pair once: only emit when a sorts before b.
                    if (Board.Index(b) < Board.Index(a)) continue;
                    if (!IsInHalf(a, b, side)) continue;
                    if (CanPlace(a, b, terrain, buildings, ignoring))
                        yield return (a, b);
                }
        }
    }
}
