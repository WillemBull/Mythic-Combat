using System;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Data;
using Godsbound.Core.Economy;
using Godsbound.Core.Gods;
using Godsbound.Core.Movement;
using Godsbound.Core.Objectives;
using Godsbound.Core.Pathfinding;
using Godsbound.Core.Training;
using Godsbound.Core.Units;
using Godsbound.Core.AI;

namespace Godsbound.Core.Match
{
    /// <summary>Which half of the wizard/battle the match is in. Mirrors the browser's <c>S.phase</c>.</summary>
    public enum MatchPhase
    {
        Setup,
        Battle
    }

    /// <summary>
    /// Everything one match owns, constructed together and wired to each other once.
    /// </summary>
    /// <remarks>
    /// <para>The browser keeps all of this in the global <c>S</c> plus a handful of module-level
    /// arrays. Gathering it into one object is what lets a match be constructed, ticked and
    /// asserted on without a scene — and lets two matches exist at once, which the globals
    /// never allowed.</para>
    /// <para>No <c>UnityEngine</c> here, by the rule that governs all of Core.</para>
    /// </remarks>
    public sealed class MatchState
    {
        public GameDatabase Database { get; }
        public TerrainMap Terrain { get; }
        public BuildingMap Buildings { get; }
        public UnitField Units { get; }
        public PathFinder Paths { get; }
        public RouteMovement Movement { get; }
        public UnitUpdateContext UnitContext { get; }
        public CombatSystem Combat { get; }
        public FortressSystem Fortresses { get; }
        public TrainingQueue Training { get; }
        public MatchObjectives Objectives { get; }
        public AiController Ai { get; set; }

        /// <summary>Each side's gods, indexed by side. Sides built without gods field none.</summary>
        public GodState[] Gods { get; }

        /// <summary>Casting, Set's chaos windows and Ptah's charges (U26).</summary>
        public PowerSystem Powers { get; }

        /// <summary>Per-frame god upkeep: poison, regen, generated income, despawns, auras (U26).</summary>
        public GodEffects GodEffects { get; }

        /// <summary>Form chains and the swallowed, ported from <c>tickFormChains</c> (U28).</summary>
        public FormChains FormChains { get; }

        /// <summary>What each side trained, lost and spent (U34).</summary>
        public MatchStats Stats { get; } = new MatchStats();

        /// <summary>Held resources, indexed by side. Side 0 is the player.</summary>
        public Purse[] Resources { get; } = { Purse.Empty, Purse.Empty };

        /// <summary>Nothing simulates outside <see cref="MatchPhase.Battle"/> — the browser gates on `S.phase`.</summary>
        public MatchPhase Phase { get; set; } = MatchPhase.Battle;

        /// <summary>Seconds since the match began. The clock is the single source.</summary>
        public float Elapsed => Objectives.Clock.Elapsed;

        /// <summary>True while the match should keep ticking.</summary>
        public bool Live => Phase == MatchPhase.Battle && !Objectives.Resolved;

        /// <param name="combatContext">
        /// Shared with <see cref="RouteMovement"/>'s context where the lookups overlap. Its
        /// <c>Resources</c> is pointed at this match's <see cref="Resources"/> so combat rewards
        /// (Anubis's kill refund, temple death favor) land in the same purses the economy fills.
        /// </param>
        /// <param name="gods">
        /// Both sides' gods. When supplied, every passive hook (combat, movement, unit update,
        /// economy, pricing) is answered by <see cref="GodRules.HasPassive"/> over them. When
        /// omitted, both sides field no gods and any <c>HasPassive</c> the caller put on the
        /// contexts is kept — the isolated simulation tests rely on that.
        /// </param>
        public MatchState(GameDatabase database,
                          TerrainMap terrain = null,
                          BuildingMap buildings = null,
                          CombatContext combatContext = null,
                          MovementContext movementContext = null,
                          System.Random rng = null,
                          bool pathJitter = true,
                          UnitUpdateContext unitContext = null,
                          GodState[] gods = null)
        {
            Database = database ?? throw new ArgumentNullException(nameof(database));
            if (gods != null && (gods.Length != 2 || gods[0] == null || gods[1] == null ||
                                 gods[0].Side != 0 || gods[1].Side != 1))
                throw new ArgumentException("gods must hold side 0 then side 1", nameof(gods));
            Gods = gods ?? new[] { GodState.None(0), GodState.None(1) };
            Terrain = terrain ?? new TerrainMap();
            Buildings = buildings ?? BuildingMap.FromExport(database.Buildings);
            Units = new UnitField();

            Paths = new PathFinder(Terrain, Buildings.RouteBlocker, database.Pathfinding, rng, pathJitter);

            var combat = combatContext ?? new CombatContext();
            var movement = movementContext ?? new MovementContext();
            if (gods != null)
            {
                Func<int, string, bool> passive = (side, key) => GodRules.HasPassive(Gods[side], key, Elapsed);
                combat.HasPassive = passive;
                movement.HasPassive = passive;
                if (unitContext != null) unitContext.HasPassive = passive;
                combat.ChaosBuffUntil = side => Powers.ChaosBuffUntil[side];
                // Xi Wangmu's grove belongs to the side that grew it; her Garden multipliers come
                // from whichever of the side's gods carries the passive (gardenMult).
                Func<Unit, bool> grove = u => Terrain.InGrove(u.Hex, u.Side);
                combat.InGrove = grove;
                movement.InGrove = grove;
                if (unitContext != null) unitContext.InGrove = grove;
                combat.GardenDamage = side => GardenMultiplier(side, "gardenDmg");
                movement.GardenSpeed = side => GardenMultiplier(side, "gardenSpeed");
            }
            _combatContext = combat;
            UnitContext = unitContext ?? new UnitUpdateContext
            {
                FactionForSide = combat.FactionForSide,
                HasPassive = combat.HasPassive,
                InGrove = combat.InGrove
            };
            combat.Resources = Resources; // same array, so rewards and income share a purse
            Combat = new CombatSystem(Units, Buildings, Terrain, database, combat);
            Fortresses = new FortressSystem(Buildings, Units, Combat, database.Fortresses);

            Movement = new RouteMovement(Units, Buildings, Terrain, Paths,
                                         database.Movement, movement);

            Training = new TrainingQueue(database.Training);
            Objectives = new MatchObjectives(Buildings, Combat.BuildingDamage, MatchClock.From(database));
            Powers = new PowerSystem(this, rng);
            GodEffects = new GodEffects(this);
            FormChains = new FormChains(this);
            // Artemis's mark is read by targeting and concealment, which must not reach into Powers.
            UnitContext.IsQuarry = (u, elapsed) => Powers.IsQuarry(u, elapsed);
            Training.Spawned += u => Powers.ApplyForge(u);
            // Counted where each thing happens, never inferred from the board afterwards.
            Training.Spawned += u => Stats.Trained[u.Side]++;
            Training.Paid += (side, food, favor) => Stats.FavorOnUnits[side] += favor;
            Combat.UnitDied += u => Stats.Lost[u.Side]++;
        }

        private readonly CombatContext _combatContext;

        /// <summary>
        /// The match's one passive lookup — whatever the combat context answers, which is
        /// <see cref="GodRules.HasPassive"/> whenever the match was built with gods.
        /// </summary>
        public bool HasPassive(int side, string key) => _combatContext.HasPassive(side, key);

        private float GardenMultiplier(int side, string field)
        {
            foreach (var s in Gods[side].Selected)
                if (s.Def.passiveKey == "gardenOfKunlun") return s.Def.GetFloat(field, 1f);
            return 1f;
        }

        /// <summary>The faction a side plays, as the combat context reports it.</summary>
        public string FactionFor(int side) => _combatContext.FactionForSide(side) ?? "";

        /// <summary>Thoth, Nu Wa and Hermes, as the price and queue read them.</summary>
        public TrainingModifiers TrainingModifiersFor(int side) =>
            GodRules.TrainingModifiersFor(key => HasPassive(side, key));

        /// <summary><c>godUnlockCost(g,side)</c> with this side's live passives.</summary>
        public int GodUnlockCost(int side, GodData god) =>
            GodRules.UnlockCost(god, key => HasPassive(side, key), Database.Training);

        /// <summary><c>godPowerCost(g,side)</c> with this side's live passives.</summary>
        public int GodPowerCost(int side, GodData god) =>
            GodRules.PowerCost(god, key => HasPassive(side, key), Database.Training);

        /// <summary>Unlock one of a side's gods, paying from that side's purse.</summary>
        public UnlockOutcome TryUnlockGod(int side, string key)
        {
            var purse = Resources[side];
            var outcome = GodRules.TryUnlock(Gods[side], key, ref purse, k => HasPassive(side, k),
                                             Database.Training, Database.GodRates, Buildings);
            Resources[side] = purse;
            return outcome;
        }

        /// <summary>
        /// Economy inputs for one side, read from live building state, the side's passives and
        /// its faction's economy traits.
        /// </summary>
        /// <remarks>
        /// Long Wang's water favor counts water on the side's OWN half — rows
        /// <see cref="Board.PlayerRow0"/>..end for the player, 0..<see cref="Board.AiRows"/>-1
        /// for the AI — as the browser does since Willem's 2026-09-16 fix.
        /// </remarks>
        public EconomyConditions ConditionsFor(int side) => new EconomyConditions(
            cityStanding: Standing(side, BuildingType.City),
            templeStanding: Standing(side, BuildingType.Temple),
            templeBonus: TempleBonus(side),
            cityFoodBoost: HasPassive(side, PassiveKeys.CityFoodBoost),
            morningStar: HasPassive(side, PassiveKeys.MorningStar),
            bounty: HasPassive(side, PassiveKeys.Bounty),
            blighted: Elapsed < Powers.BlightUntil[side], // Demeter's Blight: nothing grows
            mandateOfHeaven: HasPassive(side, PassiveKeys.MandateOfHeaven),
            standingBuildings: Buildings.StandingCount(side),
            waterFavorPerSecond: HasPassive(side, PassiveKeys.LordOfFourSeas)
                ? WaterTilesOnOwnHalf(side) * Database.GodRates.waterFavorPerTile
                : 0f);

        /// <summary>Water hexes on a side's own half, excluding no-man's land.</summary>
        public int WaterTilesOnOwnHalf(int side)
        {
            int r0 = side == 0 ? Board.PlayerRow0 : 0, r1 = side == 0 ? Board.Rows : Board.AiRows, count = 0;
            for (int r = r0; r < r1; r++)
                for (int c = 0; c < Board.Cols; c++)
                    if (Terrain[c, r] == TerrainType.Water) count++;
            return count;
        }

        /// <summary><c>economyFor(side).templeBonus</c>: false only for a faction that opts out (Aztec).</summary>
        private bool TempleBonus(int side)
        {
            var faction = Database.Faction(_combatContext.FactionForSide(side) ?? "");
            var flag = faction?.economy?.Find(e => e.key == "templeBonus");
            return flag == null || flag.value != "false";
        }

        private bool Standing(int side, BuildingType type)
        {
            var b = Buildings.Of(side, type);
            return b != null && !b.Dead;
        }

        /// <summary>Back to the start of a match: clock, purses, units, buildings and queue.</summary>
        public void Reset()
        {
            Objectives.Reset();
            Resources[0] = Resources[1] = Purse.Empty;
            Units.Clear();
            UnitContext.ResetTimers();
            Combat.Reset(); // death records and the damage tiebreaker must not carry across matches
            Training.Clear();
            Ai?.Reset();
            Powers.Reset();
            FormChains.Reset();
            Stats.Reset();
            Terrain.ClearOverlays();
            Gods[0].Reset();
            Gods[1].Reset();
            Buildings.ResetAll();
            Phase = MatchPhase.Battle;
        }

        public override string ToString() =>
            $"{Phase} {Objectives.Clock}, {Units.AliveCount} units" +
            (Objectives.Resolved ? $", {Objectives.Result.Value.Outcome}" : "");
    }
}
