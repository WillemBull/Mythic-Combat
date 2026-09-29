using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Match;
using Godsbound.Core.Units;

namespace Godsbound.Core.AI
{
    /// <summary>
    /// Where the AI would aim each of its gods, and whether casting it there would accomplish
    /// anything. Ports <c>pickAiPowerHex</c>, <c>aiPowerHits</c>, <c>aiPowerTriggerOK</c> and
    /// <c>aiSupportTarget</c>.
    /// </summary>
    /// <remarks>
    /// <para>The shape is the browser's and load-bearing: every unlocked god is evaluated every
    /// tick, and each must clear FOUR gates — a target hex from <see cref="PickHex"/>, its trigger
    /// condition, the shared cast guard, and then <c>PowerSystem</c>'s own validation (unlocked, off
    /// cooldown, affordable). The situation decides when to cast, not a timer.</para>
    /// <para>"situational" gods have no second gate on purpose (Willem, 2026-07-29): their picker
    /// already returns nothing when the power would be wasted — no myth to transform, no corpses to
    /// raise, no damaged building, nobody deep in our half — and a second gate would only make them
    /// cast less without making them smarter.</para>
    /// <para>Only the gods the AI can actually field have a picker. Everything else answers null and
    /// is therefore never cast, which is how the browser leaves Greece and the Aztecs to the player.</para>
    /// </remarks>
    public static class AiPowers
    {
        private const int AiSide = 1, PlayerSide = 0;

        private static List<Unit> Living(MatchState state, int side) =>
            state.Units.All.Where(u => !u.Dead && u.Side == side).ToList();

        /// <summary>The first standing building of the AI, in board order — any own hex will do for a global power.</summary>
        private static Hex? OwnHex(MatchState state) =>
            state.Buildings.All.Where(b => b.Side == AiSide && !b.Dead).Select(b => (Hex?)b.HexA).FirstOrDefault();

        /// <summary>The middle unit of the list, the browser's cheap "centre of the crowd" pick.</summary>
        private static Hex Middle(List<Unit> units) => units[units.Count / 2].Hex;

        private static int Reach(Building b, Hex h) => b.Hexes().Min(x => Hex.Distance(x, h));

        /// <summary>
        /// <c>aiSupportTarget</c>: the building most recently hit inside the support window, else the
        /// worst-off one. A building that has never been hit has no <c>lastHitAt</c> in the browser;
        /// here that is <see cref="Building.LastHitAt"/> still at zero, and it must read as "not under
        /// attack" — which is why the fallback exists and why the support TRIGGER refuses it.
        /// </summary>
        public static Building SupportTarget(MatchState state, AiRules rules, IEnumerable<Building> pool)
        {
            var list = pool.ToList();
            var recent = list.Where(b => b.LastHitAt > 0f && state.Elapsed - b.LastHitAt <= rules.supportWindow)
                .OrderByDescending(b => b.LastHitAt).FirstOrDefault();
            return recent ?? list.OrderBy(b => b.Hp / b.MaxHp).FirstOrDefault();
        }

        /// <summary>Where the AI would cast this god, or null when it would be wasted. <c>pickAiPowerHex</c>.</summary>
        public static Hex? PickHex(MatchState state, AiRules rules, string key)
        {
            var mine = Living(state, AiSide);
            var foe = Living(state, PlayerSide);
            switch (key)
            {
                case "jade": // conscript the strongest body they have on the board
                    return foe.Count == 0 ? (Hex?)null : foe.OrderByDescending(u => u.Hp).First().Hex;
                case "sunwukong": // transforms one of its OWN units, so it aims at its own spearhead
                    if (!state.Gods[AiSide].UnlockedMythsInRosterOrder().Any()) return null;
                    var shapeable = mine.Where(u => !u.Transformed && u.Def.cat != "form").ToList();
                    return shapeable.Count == 0 ? (Hex?)null : shapeable.OrderBy(u => u.Hex.R).Last().Hex;
                case "longwang":
                case "zhurong":
                case "horus":
                case "ra":
                    return foe.Count < 2 ? (Hex?)null : Middle(foe);
                case "leigong":
                    return foe.Count == 0 ? (Hex?)null : Middle(foe);
                case "gonggong": // block whoever has pushed deepest into the AI's own half
                    return foe.Count == 0 ? (Hex?)null : foe.OrderBy(u => u.Hex.R).First().Hex;
                case "erlangshen": // global effects: any own hex works as the cast location
                case "chang":
                case "isis":
                    return OwnHex(state);
                case "xiwangmu":
                    return Garden(state, rules, mine);
                case "anubis": // the weakest enemy overall, Judgment being a probabilistic execute
                    return foe.Count == 0 ? (Hex?)null : foe.OrderBy(u => u.Hp / u.MaxHp).First().Hex;
                case "bastet":
                {
                    var ward = SupportTarget(state, rules, state.Buildings.All.Where(b => b.Side == AiSide && !b.Dead));
                    return ward == null ? (Hex?)null : ward.HexA;
                }
                case "nuwa":
                {
                    // The damaged-only filter stays: a heal on a building at full health is never valid,
                    // even when that building is the one being hit right now.
                    var heal = SupportTarget(state, rules,
                        state.Buildings.All.Where(b => b.Side == AiSide && !b.Dead && b.Hp < b.MaxHp));
                    return heal == null ? (Hex?)null : heal.HexA;
                }
                case "osiris":
                {
                    var powers = state.Database.Powers;
                    float window = state.HasPassive(AiSide, "corpseWindow")
                        ? powers.Value("osirisWindowAnubis") : powers.Value("osirisWindow");
                    if (!state.Combat.DeadHumans.Any(d => d.Side == AiSide && state.Elapsed - d.At < window)) return null;
                    return OwnHex(state);
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Garden of Kunlun needs OPEN GROUND, so aiming at a unit standing on rock or water burns the
        /// cast for nothing. Aim at the AI's most advanced unit — the garden is cover to attack behind
        /// — and fall back through the rest of the army until one has soil in range.
        /// </summary>
        private static Hex? Garden(MatchState state, AiRules rules, List<Unit> mine)
        {
            if (mine.Count == 0) return null;
            var spearhead = mine.OrderByDescending(u => u.Hex.R).First().Hex;
            if (HasSoil(state, rules, spearhead)) return spearhead;
            foreach (var u in mine) if (HasSoil(state, rules, u.Hex)) return u.Hex;
            return null; // no soil anywhere near the army: hold the power rather than waste it
        }

        private static bool HasSoil(MatchState state, AiRules rules, Hex hex) =>
            Gods.Powers.ChinaPowers.HexesInRadius(hex, rules.soilRadius).Any(h =>
                state.Terrain[h] == TerrainType.Plains && // Desert was the other soil code until 2026-09-28
                state.Buildings.StandingAt(h) == null);

        /// <summary>
        /// <c>aiPowerHits</c>: how many enemies this power would actually land on, BY FOOTPRINT — a
        /// radius around the hex would badly undercount Horus's rank and Ra's lane.
        /// </summary>
        public static int Hits(MatchState state, AiRules rules, string key, Hex hex)
        {
            var foe = state.Units.All.Where(u => !u.Dead && u.Side == PlayerSide && !u.Def.GetBool("allyTargetOnly"));
            if (key == "horus") return foe.Count(u => u.Hex.R == hex.R);
            if (key == "ra") return foe.Count(u => u.Hex.C == hex.C);
            return foe.Count(u => Hex.Distance(u.Hex, hex) <= rules.aoeRadius);
        }

        /// <summary>Would casting this god at this hex accomplish anything? <c>aiPowerTriggerOK</c>.</summary>
        public static bool TriggerOk(MatchState state, AiRules rules, string key, Hex hex)
        {
            switch (rules.PowerClass(key))
            {
                case "aoe":
                    return Hits(state, rules, key, hex) >= rules.aoeHits;
                case "control":
                {
                    // Stunning a unit that is merely walking wastes the favor — it just resumes walking.
                    var caught = state.Units.All
                        .Where(u => !u.Dead && u.Side == PlayerSide && Hex.Distance(u.Hex, hex) <= rules.controlRadius).ToList();
                    return caught.Count >= rules.controlHits && caught.Any(u => u.Engagement != null);
                }
                case "support":
                {
                    var b = state.Buildings.StandingAt(hex);
                    if (b == null || b.Side != AiSide || b.Dead) return false;
                    return b.LastHitAt > 0f && state.Elapsed - b.LastHitAt <= rules.supportWindow;
                }
                case "buff":
                {
                    // No member today: Sekhmet is Egypt's only area buff and the AI never fields her.
                    // The rule is live the moment a buff god enters an AI god pick — not dead code.
                    int own = state.Units.All.Count(u => !u.Dead && u.Side == AiSide &&
                        Hex.Distance(u.Hex, hex) <= rules.buffRadius && u.Engagement != null);
                    return own >= rules.buffUnits;
                }
                default:
                    return true; // situational: the picker already refused when the power would be wasted
            }
        }
    }
}
