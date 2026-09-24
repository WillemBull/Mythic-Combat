using System;

namespace Godsbound.Core.Data
{
    /// <summary>
    /// <c>findPath</c>'s two tuning numbers, extracted from the function's own source text
    /// by the exporter.
    /// </summary>
    /// <remarks>
    /// They are inline literals inside the cost expression, so the exporter parses them out
    /// and fails loudly if the formula is reshaped — rather than letting a stale constant
    /// ship quietly into Unity.
    /// </remarks>
    [Serializable]
    public class PathfindingRates
    {
        /// <summary>
        /// Random cost added per edge. Deliberate: it stops every unit funnelling down the
        /// single cheapest lane, so a repeated query may return a different near-equal path.
        /// </summary>
        public float edgeJitter;

        /// <summary>
        /// Floor applied to terrain speed before inverting it, so a very slow tile cannot
        /// produce an unbounded edge cost.
        /// </summary>
        public float speedFloor;
    }
}
