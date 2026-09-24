using System;
using Godsbound.Core.Match;

namespace Godsbound.Core.Gods
{
    public enum GodTapResult
    {
        /// <summary>First tap on a locked god: show its passive. Nothing is paid.</summary>
        Previewed,
        /// <summary>Confirming tap: the god was unlocked and paid for.</summary>
        Unlocked,
        /// <summary>Confirming tap without enough favor. The preview is cleared.</summary>
        CannotAffordUnlock,
        /// <summary>An unlocked god whose power is recharging.</summary>
        Recharging,
        /// <summary>An unlocked god whose power Isis has locked.</summary>
        PowerLocked,
        /// <summary>An unlocked god whose power costs more favor than the side holds.</summary>
        CannotAffordPower,
        /// <summary>An unlocked, ready god: arming is requested. U30 fills the arming seam.</summary>
        ArmRequested,
        /// <summary>Not one of this side's gods, or the match is over.</summary>
        Ignored
    }

    /// <summary>
    /// The player's god-tile tap, ported from <c>onGodTap</c>: two taps to unlock (preview, then
    /// confirm), one tap to arm an unlocked god.
    /// </summary>
    /// <remarks>
    /// Pure state machine over <see cref="MatchState"/>; the HUD only renders what it
    /// returns. The confirming tap applies ONLY to unlocking — Willem, 2026-07-29: seeing the
    /// passive is the first tap, spending favor you cannot get back is the second.
    /// </remarks>
    public sealed class GodTapFlow
    {
        /// <summary>The god whose passive is being previewed (<c>S.godPreview</c>), or null.</summary>
        public string Preview { get; private set; }

        public int Side { get; }

        public GodTapFlow(int side = 0) { Side = side; }

        public void Clear() => Preview = null;

        public GodTapResult Tap(MatchState state, string key)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state.Objectives.Resolved) return GodTapResult.Ignored;
            var gods = state.Gods[Side];
            var slot = gods.Slot(key);
            if (slot == null) return GodTapResult.Ignored;

            if (!slot.Unlocked && Preview != key)
            {
                Preview = key;
                return GodTapResult.Previewed;
            }
            Preview = null;
            if (!slot.Unlocked)
                return state.TryUnlockGod(Side, key) == UnlockOutcome.Unlocked
                    ? GodTapResult.Unlocked
                    : GodTapResult.CannotAffordUnlock;

            float elapsed = state.Elapsed;
            if (elapsed < slot.CooldownUntil) return GodTapResult.Recharging;
            if (elapsed < slot.LockedUntil) return GodTapResult.PowerLocked;
            if (state.Resources[Side].Favor < state.GodPowerCost(Side, slot.Def)) return GodTapResult.CannotAffordPower;
            return GodTapResult.ArmRequested;
        }
    }
}
