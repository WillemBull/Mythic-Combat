using System;

namespace Godsbound.Core.Data
{
    /// <summary>
    /// Price and queue modifiers, read out of the browser game's own function sources by
    /// the exporter.
    /// </summary>
    [Serializable]
    public class TrainingRates
    {
        /// <summary>
        /// Thoth's Scribe's Ledger. Multiplies EVERY cost — food and favor, units and gods.
        /// </summary>
        public float thothDiscount;

        /// <summary>
        /// Nu Wa's Mother of Humanity. Food only, human units only — a narrower discount
        /// than Thoth's, and the two stack.
        /// </summary>
        public float nuWaHumanFoodDiscount;

        /// <summary>Hermes the Messenger: training finishes sooner.</summary>
        public float messengerTrainMultiplier;

        /// <summary>
        /// How far the ready time is pushed out when the exit hex is blocked and no
        /// building is standing to emerge on. The unit is never dropped.
        /// </summary>
        public float blockedExitRetrySeconds;

        /// <summary>Exit columns produced by the browser for three orders due together.</summary>
        public int[] simultaneousCompletionOrder;
    }
}
