using Godsbound.Core.Data;

namespace Godsbound.Core.Training
{
    /// <summary>What a unit actually costs, after discounts.</summary>
    public readonly struct Price
    {
        public readonly float Food;
        public readonly float Favor;

        public Price(float food, float favor)
        {
            Food = food;
            Favor = favor;
        }

        public override string ToString() => $"{Food:0.##} food, {Favor:0.##} favor";
    }

    /// <summary>
    /// Passives that change what a side pays or how fast it trains.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than looked up, so pricing stays pure. The god
    /// registry that would answer <c>hasPassive</c> is a later step.
    /// </remarks>
    public readonly struct TrainingModifiers
    {
        /// <summary>Thoth: every cost reduced.</summary>
        public readonly bool ScribesLedger;

        /// <summary>Nu Wa: human food costs reduced.</summary>
        public readonly bool MotherOfHumanity;

        /// <summary>Hermes: training completes sooner.</summary>
        public readonly bool Messenger;

        public TrainingModifiers(bool scribesLedger = false, bool motherOfHumanity = false,
                                 bool messenger = false)
        {
            ScribesLedger = scribesLedger;
            MotherOfHumanity = motherOfHumanity;
            Messenger = messenger;
        }

        public static readonly TrainingModifiers None = new TrainingModifiers();
    }

    /// <summary>
    /// The ONE place a unit's real price is computed, ported from <c>unitCost</c>.
    /// </summary>
    /// <remarks>
    /// <para>Singular on purpose. The browser game carries a comment explaining that
    /// <c>canAfford</c> and <c>pay</c> once disagreed — one ignored Nu Wa's discount while
    /// the other applied it, so a side was blocked at the full price and then charged the
    /// reduced one. Both now read this. <b>Any future discount belongs here and nowhere
    /// else</b>, and <see cref="CanAfford"/> and <see cref="Pay"/> below must never compute
    /// a price of their own.</para>
    /// <para>Thoth's discount applies to both currencies; Nu Wa's applies only to the food
    /// component of human units. They multiply together.</para>
    /// </remarks>
    public static class UnitPrice
    {
        public static Price For(UnitData def, TrainingModifiers mods, TrainingRates rates)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (rates == null) throw new System.ArgumentNullException(nameof(rates));

            float ledger = mods.ScribesLedger ? rates.thothDiscount : 1f;
            bool isHuman = def.cat == "human";
            float nuWa = (isHuman && mods.MotherOfHumanity) ? rates.nuWaHumanFoodDiscount : 1f;

            return new Price(def.costFood * ledger * nuWa, def.costFavor * ledger);
        }

        /// <summary>Whether a purse covers this unit. Both currencies must suffice.</summary>
        public static bool CanAfford(Economy.Purse purse, UnitData def,
                                     TrainingModifiers mods, TrainingRates rates)
        {
            var price = For(def, mods, rates);
            return purse.Food >= price.Food && purse.Favor >= price.Favor;
        }

        /// <summary>
        /// Deduct the price, returning the remaining purse. Throws if unaffordable rather
        /// than going negative — a silent overdraft would be far harder to notice.
        /// </summary>
        public static Economy.Purse Pay(Economy.Purse purse, UnitData def,
                                        TrainingModifiers mods, TrainingRates rates)
        {
            var price = For(def, mods, rates);
            if (purse.Food < price.Food || purse.Favor < price.Favor)
                throw new System.InvalidOperationException(
                    $"cannot pay {price} from {purse} — check CanAfford first");

            return new Economy.Purse(purse.Food - price.Food, purse.Favor - price.Favor);
        }

        /// <summary>Seconds this unit takes to train, after the Messenger passive.</summary>
        public static float TrainSeconds(UnitData def, TrainingModifiers mods, TrainingRates rates)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            return def.train * (mods.Messenger ? rates.messengerTrainMultiplier : 1f);
        }
    }
}
