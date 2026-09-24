using System;
using System.Collections.Generic;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Match;
using Godsbound.Core.Units;

namespace Godsbound.Core.Gods
{
    /// <summary>How a power is aimed. Every browser power is cast on a board hex; Isis also takes an enemy god.</summary>
    public enum PowerTarget
    {
        Hex,
        EnemyGod
    }

    /// <summary>Why a cast may or may not go ahead. Only <see cref="Ready"/> pays.</summary>
    public enum PowerCheck
    {
        Ready,
        NotSelected,
        NotUnlocked,
        Recharging,
        PowerLocked,
        CannotAfford,
        NotPorted
    }

    /// <summary>What a cast did.</summary>
    public readonly struct CastResult
    {
        public readonly PowerCheck Check;

        /// <summary>True when the power's effect took hold. A cast that passed the check
        /// is PAID even when this is false (the browser's fizzle still costs).</summary>
        public readonly bool Applied;

        public CastResult(PowerCheck check, bool applied) { Check = check; Applied = applied; }
        public bool Paid => Check == PowerCheck.Ready;
        public override string ToString() => $"{Check}{(Applied ? " applied" : "")}";
    }

    /// <summary>Everything one power's effect may read.</summary>
    public sealed class PowerCast
    {
        public MatchState State;
        public int Side;
        public GodData God;
        public Hex At;
        public string PickedGod;
        public float Elapsed;
        public Func<double> Random;
        public PowerRates Rates;
        public int EnemySide => 1 - Side;
        public float R(string key) => Rates.Value(key);
        public bool HasPassive(int side, string key) => State.HasPassive(side, key);

        /// <summary><c>enemyIgnores(u, caster)</c>: an ally-only body is invisible to the other side.</summary>
        public bool Ignores(Unit u) => u.Def.GetBool("allyTargetOnly") && u.Side != Side;
    }

    /// <summary>One power's effect. Returns false when it found nothing to act on.</summary>
    public delegate bool PowerEffect(PowerCast cast);

    /// <summary>A registered power: its effect and how it is aimed.</summary>
    public sealed class PowerDefinition
    {
        public readonly string Key;
        public readonly PowerTarget Aim;
        public readonly PowerEffect Effect;
        public PowerDefinition(string key, PowerTarget target, PowerEffect effect)
        { Key = key; Aim = target; Effect = effect; }
    }

    /// <summary>Artemis's quarry for one side: the marked body, and when the mark lapses (<c>S.hunt</c>).</summary>
    public sealed class HuntMark
    {
        public Unit Unit;
        public float Until;
    }
    /// <summary>
    /// The god-power framework, ported from <c>castPlayerPower</c> and <c>applyGodPower</c>'s dispatch.
    /// </summary>
    /// <remarks>
    /// <para>One entry point for both sides. Order is the browser's: validate (unlocked, off
    /// cooldown, not Isis-locked, affordable at the Ledger price), pay, start the cooldown, apply,
    /// then open Set's Lord of Storms window for the OTHER side. Validation failure pays nothing;
    /// a validated cast whose effect finds no target is still paid, exactly as in the browser.</para>
    /// <para>Effects live one method per power in a faction file (Powers/*.cs) and are looked up by
    /// god key. Nothing else branches on a power's name.</para>
    /// </remarks>
    public sealed class PowerSystem
    {
        private readonly MatchState _state;
        private readonly Dictionary<string, PowerDefinition> _powers = new Dictionary<string, PowerDefinition>();

        /// <summary>Set's Lord of Storms: this side hits harder until this time (<c>S.chaosBuffUntil</c>).</summary>
        public float[] ChaosBuffUntil { get; } = new float[2];

        /// <summary>Ptah's Creator's Word: the side's next shapeable unit is created twice (<c>S.ptahCharge</c>).</summary>
        public bool[] PtahCharge { get; } = new bool[2];

        /// <summary>Artemis's Actaeon's End: who each side hunts (<c>S.hunt</c>). Null when that side hunts nothing.</summary>
        public HuntMark[] Hunt { get; } = new HuntMark[2];

        /// <summary>Demeter's Blight: that side grows nothing until this time (<c>S.blightUntil</c>).</summary>
        public float[] BlightUntil { get; } = new float[2];

        /// <summary>Hephaestus's Forge: charges in hand, spent one trained body at a time (<c>S.forge</c>).</summary>
        public int[] Forge { get; } = new int[2];

        /// <summary>The randomness powers use (Anubis's judgment). Tests replace it.</summary>
        public Func<double> Random { get; set; }

        /// <summary>
        /// Raised after a cast that took hold, with the casting side, the power and where it landed.
        /// Presentation subscribes so a cast neither side's UI initiated — the AI's — is still seen.
        /// Core never listens.
        /// </summary>
        public event Action<int, string, Hex> Cast;

        public PowerSystem(MatchState state, System.Random rng = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            var r = rng ?? new System.Random();
            Random = r.NextDouble;
            foreach (var p in Powers.EgyptPowers.All()) Register(p);
            foreach (var p in Powers.ChinaPowers.All()) Register(p);
            foreach (var p in Powers.GreecePowers.All()) Register(p);
            foreach (var p in Powers.AztecPowers.All()) Register(p);
        }

        public void Register(PowerDefinition power)
        {
            if (power == null) throw new ArgumentNullException(nameof(power));
            _powers[power.Key] = power;
        }

        public bool IsPorted(string key) => key != null && _powers.ContainsKey(key);

        public PowerDefinition Definition(string key) => _powers.TryGetValue(key ?? "", out var p) ? p : null;

        public PowerCheck Check(int side, string key)
        {
            var slot = _state.Gods[side].Slot(key);
            if (slot == null) return PowerCheck.NotSelected;
            if (!IsPorted(key)) return PowerCheck.NotPorted;
            if (!slot.Unlocked) return PowerCheck.NotUnlocked;
            float elapsed = _state.Elapsed;
            if (elapsed < slot.CooldownUntil) return PowerCheck.Recharging;
            if (elapsed < slot.LockedUntil) return PowerCheck.PowerLocked;
            if (_state.Resources[side].Favor < _state.GodPowerCost(side, slot.Def)) return PowerCheck.CannotAfford;
            return PowerCheck.Ready;
        }

        /// <summary>Validate, pay, start the cooldown, apply, open the opponent's chaos window.</summary>
        public CastResult TryCast(int side, string key, Hex target, string pickedGod = null)
        {
            var check = Check(side, key);
            if (check != PowerCheck.Ready) return new CastResult(check, false);
            var slot = _state.Gods[side].Slot(key);
            float elapsed = _state.Elapsed;
            var purse = _state.Resources[side];
            int cost = _state.GodPowerCost(side, slot.Def);
            _state.Resources[side] = new Purse(purse.Food, purse.Favor - cost);
            _state.Stats.FavorOnPowers[side] += cost;
            slot.CooldownUntil = elapsed + slot.Def.cd;
            bool applied = Apply(side, slot.Def, target, pickedGod);
            ChaosBuffUntil[1 - side] = elapsed + _state.Database.Powers.Value("chaosWindow");
            if (applied) Cast?.Invoke(side, key, target);
            return new CastResult(check, applied);
        }

        /// <summary><c>applyGodPower</c>: the effect alone — no validation, payment or cooldown.</summary>
        public bool Apply(int side, GodData god, Hex target, string pickedGod = null)
        {
            if (god == null) throw new ArgumentNullException(nameof(god));
            if (!Board.InBounds(target)) throw new ArgumentOutOfRangeException(nameof(target));
            if (!_powers.TryGetValue(god.key, out var power)) return false;
            return power.Effect(new PowerCast
            {
                State = _state, Side = side, God = god, At = target, PickedGod = pickedGod,
                Elapsed = _state.Elapsed, Random = Random, Rates = _state.Database.Powers
            });
        }

        /// <summary>
        /// Spend a side's Creator's Word on a unit it just trained, when that unit can be shaped
        /// (<c>ptahCanShape</c>: humans, and the one exported exception). An ineligible unit leaves
        /// the charge waiting rather than wasting it.
        /// </summary>
        public bool ConsumePtahCharge(int side, UnitData def)
        {
            if (def == null || !PtahCharge[side]) return false;
            if (def.cat != "human" && def.key != _state.Database.Powers.ptahExtraKey) return false;
            PtahCharge[side] = false;
            return true;
        }

        /// <summary><c>isHunted(u, side)</c>: is this body the quarry of <paramref name="side"/>?</summary>
        public bool IsHunted(Unit u, int side, float elapsed)
        {
            var h = Hunt[side];
            return h != null && h.Unit == u && elapsed < h.Until;
        }

        /// <summary>
        /// <c>isQuarry(u)</c>: marked by the side OPPOSING its own, which is the only way a mark is
        /// ever set — and what lets a unit's own allies turn on it.
        /// </summary>
        public bool IsQuarry(Unit u, float elapsed) => u != null && IsHunted(u, 1 - u.Side, elapsed);

        /// <summary>
        /// Spend one Forge charge on a body as it is CREATED, not as a timed buff: a reinforced unit
        /// stays reinforced. Rounds as the browser's <c>Math.round</c> does.
        /// </summary>
        public bool ApplyForge(Unit u)
        {
            if (u == null || Forge[u.Side] <= 0) return false;
            Forge[u.Side]--;
            float max = GodRules.JsRound(u.MaxHp * (double)_state.Database.Powers.Value("forgeHp"));
            u.SetMaxHp(max, max);
            u.Forged = true;
            return true;
        }

        public void Reset()
        {
            Array.Clear(ChaosBuffUntil, 0, ChaosBuffUntil.Length);
            Array.Clear(Hunt, 0, Hunt.Length);
            Array.Clear(BlightUntil, 0, BlightUntil.Length);
            Array.Clear(Forge, 0, Forge.Length);
            Array.Clear(PtahCharge, 0, PtahCharge.Length);
        }
    }
}
