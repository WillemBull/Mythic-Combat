using System;

namespace Godsbound.Core.Data
{
    /// <summary>Movement constants exported from the browser, never tuned in C#.</summary>
    [Serializable]
    public sealed class MovementRates
    {
        public float baseSpeedMultiplier;
        public float slowMultiplier;
        public float moveSpeedMultiplier;
        public float moonlightMultiplier;
        public float fastWaterMultiplier;
        public float nearWaterMultiplier;
        public float deviateDelay;
        public float congestionDelay;
        public float congestionRetry;
        // Browser snap pixels converted to hex radii using the reference viewport.
        public float snapDistanceInHexes;
    }
}
