using System;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Training;

namespace Godsbound.Core.Gods
{
    /// <summary>Passive keys Core reads by name. The value is the browser's <c>passiveKey</c>.</summary>
    public static class PassiveKeys
    {
        public const string Automata = "automata";
        public const string Bounty = "bounty";
        public const string CityFoodBoost = "cityFoodBoost";
        public const string Earthshaker = "earthshaker";
        public const string Inevitable = "inevitable";
        public const string LordOfFourSeas = "lordOfFourSeas";
        public const string Lyre = "lyre";
        public const string MandateOfHeaven = "mandateOfHeaven";
        public const string Messenger = "messenger";
        public const string Moonlight = "moonlight";
        public const string MorningStar = "morningStar";
        public const string MotherOfHumanity = "motherOfHumanity";
        public const string Pestilence = "pestilence";
        public const string Resurrection = "resurrection";
        public const string ScribesLedger = "scribesLedger";
        public const string Warcry = "warcry";
    }

    public enum UnlockOutcome
    {
        Unlocked,
        AlreadyUnlocked,
        NotSelected,
        CannotAfford
    }

    /// <summary>
    /// The rules half of the god system: <c>hasPassive</c>, the Scribe's Ledger discount on god
    /// prices, and the unlock transaction shared by the player's confirm tap and the AI.
    /// </summary>
    /// <remarks>
    /// <para>Every passive in the port is answered HERE and consumed through the hook each
    /// system already exposes (<c>HasPassive</c> delegates on the combat, movement and
    /// unit-update contexts, <see cref="EconomyConditions"/> and <see cref="TrainingModifiers"/>).
    /// No system branches on a god's name.</para>
    /// <para>Two browser unlock side effects: Ptah's building boost is ported (deterministic,
    /// measured). Tezcatlipoca's Lord of Discord reverses a RANDOM enemy unit's route at unlock
    /// and is left for the Aztec step (U29), which ports unit-touching god effects.</para>
    /// </remarks>
    public static class GodRules
    {
        /// <summary>Port of <c>hasPassive(side,key)</c>.</summary>
        public static bool HasPassive(GodState gods, string key, float elapsed)
        {
            if (gods == null || string.IsNullOrEmpty(key)) return false;
            if (elapsed < gods.PassivesStrippedUntil) return false;
            foreach (var s in gods.Selected)
                if (s.Unlocked && s.Def.passiveKey == key) return true;
            return false;
        }

        /// <summary>JavaScript's <c>Math.round</c>: halves round UP, unlike <see cref="Math.Round(double)"/>.</summary>
        public static int JsRound(double value) => (int)Math.Floor(value + 0.5);

        /// <summary><c>ledger(side)</c>: Thoth's multiplier on everything a side buys.</summary>
        public static float Ledger(Func<string, bool> hasPassive, TrainingRates rates) =>
            hasPassive(PassiveKeys.ScribesLedger) ? rates.thothDiscount : 1f;

        /// <summary><c>godUnlockCost(g,side)</c>.</summary>
        public static int UnlockCost(GodData god, Func<string, bool> hasPassive, TrainingRates rates) =>
            JsRound(god.cost * (double)Ledger(hasPassive, rates));

        /// <summary><c>godPowerCost(g,side)</c>.</summary>
        public static int PowerCost(GodData god, Func<string, bool> hasPassive, TrainingRates rates) =>
            JsRound(god.pcost * (double)Ledger(hasPassive, rates));

        /// <summary>The three pricing passives, as the struct <see cref="UnitPrice"/> reads.</summary>
        public static TrainingModifiers TrainingModifiersFor(Func<string, bool> hasPassive) =>
            new TrainingModifiers(
                scribesLedger: hasPassive(PassiveKeys.ScribesLedger),
                motherOfHumanity: hasPassive(PassiveKeys.MotherOfHumanity),
                messenger: hasPassive(PassiveKeys.Messenger));

        /// <summary>
        /// Pay for and unlock one god. The purse is untouched unless the unlock happens.
        /// </summary>
        /// <param name="hasPassive">The side's live passive lookup, read BEFORE the unlock so a
        /// god never discounts itself (Thoth pays full price, as in the browser).</param>
        /// <param name="buildings">Receives Ptah's boost; may be null in isolated tests.</param>
        public static UnlockOutcome TryUnlock(GodState gods, string key, ref Purse purse,
                                              Func<string, bool> hasPassive, TrainingRates rates,
                                              GodRates godRates, BuildingMap buildings)
        {
            if (gods == null) throw new ArgumentNullException(nameof(gods));
            var slot = gods.Slot(key);
            if (slot == null) return UnlockOutcome.NotSelected;
            if (slot.Unlocked) return UnlockOutcome.AlreadyUnlocked;

            int cost = UnlockCost(slot.Def, hasPassive, rates);
            if (purse.Favor < cost) return UnlockOutcome.CannotAfford;

            purse = new Purse(purse.Food, purse.Favor - cost);
            if (buildings != null && godRates != null && !string.IsNullOrEmpty(godRates.ptahPassiveKey) &&
                slot.Def.passiveKey == godRates.ptahPassiveKey)
            {
                foreach (var b in buildings.Of(gods.Side).Where(b => !b.Dead).ToList())
                    b.ScaleMaxHp(godRates.ptahBuildingHpMultiplier);
            }
            gods.MarkUnlocked(slot);
            return UnlockOutcome.Unlocked;
        }
    }
}
