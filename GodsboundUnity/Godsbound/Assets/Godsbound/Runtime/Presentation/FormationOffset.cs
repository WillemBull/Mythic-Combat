using System;
using Godsbound.Core;
using Godsbound.Core.Units;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Roadmap 9.3: a render-only nudge so two engaged units lean into each other instead of sitting
    /// squarely on their hex centres. Unit positions are untouched — this is added at draw time.
    /// </summary>
    /// <remarks>
    /// The step also specified a three-slot spread for idle units sharing a hex. That half is dead:
    /// a hex has held exactly one unit of a layer since the capacity change (see HISTORY.md), so
    /// there is never a second idle unit to spread. Only the engaged offset is shipped, as 9.3's own
    /// note instructed.
    /// </remarks>
    public static class FormationOffset
    {
        /// <summary>Fraction of a hex radius, from 9.3.</summary>
        public const float Lean = 0.28f;

        public static BoardPoint For(Unit unit, HexLayout layout)
        {
            var none = new BoardPoint(0f, 0f);
            if (unit == null || unit.Dead || layout == null) return none;
            var contact = unit.Engagement;
            if (contact == null || contact.Dead) return none;
            BoardPoint to;
            if (contact.Unit != null) to = contact.Unit.Position;
            else
            {
                var a = layout.Center(contact.Building.HexA);
                var b = layout.Center(contact.Building.HexB);
                to = new BoardPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
            }
            float dx = to.X - unit.Position.X, dy = to.Y - unit.Position.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            // Standing exactly on the target (a swallowed host, a building hex) has no direction.
            if (length < 1e-4f) return none;
            float step = Lean * layout.Hex;
            return new BoardPoint(dx / length * step, dy / length * step);
        }
    }
}
