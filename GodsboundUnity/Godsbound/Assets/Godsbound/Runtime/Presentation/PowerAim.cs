using System.Collections.Generic;
using System.Linq;
using Godsbound.Core;
using Godsbound.Core.Gods;
using Godsbound.Core.Gods.Powers;
using Godsbound.Core.Match;

namespace Godsbound.Presentation
{
    /// <summary>
    /// The player's power-aiming state: tap an unlocked god to arm, tap it again to cancel, tap the
    /// board to cast. Every cast goes through <see cref="PowerSystem.TryCast"/> and nothing else.
    /// </summary>
    /// <remarks>
    /// Plain C# so EditMode tests drive it without a scene. Isis is the one power aimed at an enemy
    /// GOD; a board tap still casts her with the automatic pick, as in the browser.
    /// </remarks>
    public sealed class PowerAim
    {
        public string Armed { get; private set; }
        public bool IsArmed => Armed != null;

        /// <summary>Toggle: arming the armed god cancels it; arming another switches.</summary>
        public void Toggle(string key) => Armed = Armed == key ? null : key;

        public void Cancel() => Armed = null;

        /// <summary>
        /// Cast the armed power at a board hex. Always disarms, whatever the outcome — the browser's
        /// <c>castPlayerPower</c> clears <c>S.armedPower</c> before it validates.
        /// </summary>
        public CastResult? CastAt(MatchState state, Hex target)
        {
            if (Armed == null) return null;
            var key = Armed;
            Armed = null;
            return state.Powers.TryCast(0, key, target);
        }

        /// <summary>Isis armed and an enemy god chosen: lock that one. The temple is the nominal cast point.</summary>
        public CastResult? PickGod(MatchState state, string enemyGod)
        {
            if (Armed == null || state.Powers.Definition(Armed)?.Aim != PowerTarget.EnemyGod) return null;
            var slot = state.Gods[1].Slot(enemyGod);
            if (slot == null || !slot.Unlocked || state.Elapsed < slot.LockedUntil) return null; // isisCanLock
            var temple = state.Buildings.Of(0, Core.Buildings.BuildingType.Temple);
            var at = temple != null ? temple.HexA : new Hex(0, 0);
            var key = Armed;
            Armed = null;
            return state.Powers.TryCast(0, key, at, enemyGod);
        }

        /// <summary>Enemy gods Isis could lock right now, in roster order.</summary>
        public static IEnumerable<GodSlot> LockableEnemyGods(MatchState state) =>
            state.Gods[1].Selected.Where(s => s.Unlocked && state.Elapsed >= s.LockedUntil);

        /// <summary>
        /// The hexes a power would reach from <paramref name="at"/>, for the aiming highlight. Shapes
        /// come from the same exported coefficients the effects read.
        /// </summary>
        public static IEnumerable<Hex> Footprint(MatchState state, string key, Hex at)
        {
            var r = state.Database.Powers;
            int Radius(string rate) => (int)r.Value(rate);
            switch (key)
            {
                case "horus": return Enumerable.Range(0, Board.Cols).Select(c => new Hex(c, at.R));
                case "ra": return Enumerable.Range(0, Board.Rows).Select(row => new Hex(at.C, row));
                case "nephthys":
                    return Enumerable.Range(0, Board.Cols).SelectMany(c =>
                        Enumerable.Range(Board.AiRows, Board.NeutralRows).Select(row => new Hex(c, row)));
                case "set": return ChinaPowers.HexesInRadius(at, Radius("setRadius"));
                case "sekhmet": return ChinaPowers.HexesInRadius(at, Radius("sekhmetRadius"));
                case "anubis": return ChinaPowers.HexesInRadius(at, Radius("anubisRadius"));
                case "thoth": return ChinaPowers.HexesInRadius(at, Radius("thothRadius"));
                case "jade": return ChinaPowers.HexesInRadius(at, Radius("jadeRadius"));
                case "sunwukong": return ChinaPowers.HexesInRadius(at, Radius("wukongRadius"));
                case "longwang": return ChinaPowers.HexesInRadius(at, Radius("floodRadius"));
                case "gonggong": return ChinaPowers.HexesInRadius(at, Radius("rubbleRadius"));
                case "xiwangmu": return ChinaPowers.HexesInRadius(at, Radius("groveRadius"));
                case "zhurong": return ChinaPowers.HexesInRadius(at, Radius("fireRadius"));
                case "leigong": return ChinaPowers.HexesInRadius(at, Radius("thunderRadius"));
                // U34: the other two pantheons, now that a player can field them. Poseidon aims at
                // BUILDINGS, so his footprint is the reach his quake has from the tapped hex.
                case "zeus": return ChinaPowers.HexesInRadius(at, Radius("zeusRadius"));
                case "poseidon": return ChinaPowers.HexesInRadius(at, Radius("quakeRadius"));
                case "athena": return ChinaPowers.HexesInRadius(at, Radius("aegisRadius"));
                case "ares": return ChinaPowers.HexesInRadius(at, Radius("aresRadius"));
                case "apollo": return ChinaPowers.HexesInRadius(at, Radius("apolloRadius"));
                case "dionysus": return ChinaPowers.HexesInRadius(at, Radius("vineRadius"));
                case "artemis": return ChinaPowers.HexesInRadius(at, Radius("huntRadius"));
                case "tlaloc": return ChinaPowers.HexesInRadius(at, Radius("rainRadius"));
                case "tezcatlipoca": return ChinaPowers.HexesInRadius(at, Radius("mirrorRadius"));
                case "coyolxauhqui": return ChinaPowers.HexesInRadius(at, Radius("dismemberRadius"));
                case "itzpapalotl": return ChinaPowers.HexesInRadius(at, Radius("obsidianRadius"));
                default: return new[] { at }; // untargeted or self-targeting: the tap point only
            }
        }
    }
}
