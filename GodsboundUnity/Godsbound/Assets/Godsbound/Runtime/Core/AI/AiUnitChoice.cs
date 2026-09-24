using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;
using Godsbound.Core.Match;
using Godsbound.Core.Training;

namespace Godsbound.Core.AI
{
    /// <summary>Browser pickAiUnit, including deliberately bounded saving rather than free purchases.</summary>
    public sealed class AiUnitChoice
    {
        public AiProfile Profile { get; }
        public AiRules Rules { get; }
        public string[] Heroes { get; }
        public float HeroAt { get; set; }
        public float? SaveStart { get; set; }
        // AiController points this at the AI side's GodState (U23); empty means no myths unlocked.
        public Func<IEnumerable<string>> UnlockedMyths { get; set; } = () => Array.Empty<string>();
        private readonly Func<double> random;
        public AiUnitChoice(AiProfile profile, AiRules rules, IEnumerable<string> heroes, Func<double> random)
        {
            Profile = profile; Rules = rules; Heroes = heroes.ToArray(); this.random = random;
            if (Heroes.Length == 0) throw new ArgumentException("AI needs a hero roster");
            Reset();
        }
        public void Reset() { HeroAt = Rules.initialHeroAt; SaveStart = null; }
        private bool Affordable(MatchState state, UnitData def) => UnitPrice.CanAfford(state.Resources[1], def, state.TrainingModifiersFor(1), state.Database.Training);
        private UnitData Def(MatchState state, string key) => state.Database.Unit(Profile.faction, key);
        private string PoolPick(MatchState state, Func<UnitData, bool> predicate) => Profile.pool.Distinct()
            .Where(k => Def(state, k) != null && predicate(Def(state, k)) && Affordable(state, Def(state, k)))
            .OrderBy(k => Def(state, k).costFood + Def(state, k).costFavor).FirstOrDefault();
        public string Pick(MatchState state)
        {
            var foe = state.Units.AliveOf(0).Where(u => !u.Def.GetBool("allyTargetOnly")).ToArray();
            bool heroAlive = state.Units.AliveOf(1).Any(u => u.Def.GetBool("hero"));
            string hero = Heroes[(int)(random() * Heroes.Length)];
            if (foe.Count(u => u.Def.cat == "myth") >= Rules.counterMyths && !heroAlive && Affordable(state, Def(state, hero)))
            { HeroAt = state.Elapsed + Rules.heroRepeat; return hero; }
            if (foe.Any(u => u.Def.GetBool("hero")) && random() < Rules.heroCounterChance)
            { var melee = PoolPick(state, d => d.cat == "human" && d.range <= 1); if (melee != null) return melee; }
            if (foe.Length >= Rules.rangedMinimum && (float)foe.Count(u => u.Def.range > 1) / foe.Length >= Rules.rangedRatio)
            { var shield = PoolPick(state, d => d.armor == "shielded"); if (shield != null) return shield; }
            string siege = Profile.pool.FirstOrDefault(k => Def(state, k)?.GetBool("targetsBuildings") == true);
            if (siege != null)
            {
                bool have = state.Units.AliveOf(1).Any(u => u.Key == siege) || state.Training.PendingFor(1).Any(o => o.Def.key == siege);
                if (have) SaveStart = null;
                else if (Affordable(state, Def(state, siege))) { SaveStart = null; return siege; }
                else
                {
                    if (!state.Units.AliveOf(0).Any(u => u.Hex.R < Board.AiRows))
                    {
                        if (!SaveStart.HasValue) SaveStart = state.Elapsed;
                        if (state.Elapsed - SaveStart.Value < Rules.saveMax) return null;
                    }
                    SaveStart = null;
                }
            }
            var myths = UnlockedMyths().Where(k => state.Resources[1].Favor >= Def(state, k).costFavor + Rules.mythReserve).ToArray();
            if (state.Elapsed >= HeroAt && Affordable(state, Def(state, hero)) && !heroAlive)
            { HeroAt = state.Elapsed + Rules.heroRepeat; return hero; }
            if (myths.Length > 0 && random() < Profile.mythChance) return myths[(int)(random() * myths.Length)];
            var pool = Profile.pool.Where(k => Affordable(state, Def(state, k))).ToArray();
            return pool.Length > 0 ? pool[(int)(random() * pool.Length)] : null;
        }
    }
}
