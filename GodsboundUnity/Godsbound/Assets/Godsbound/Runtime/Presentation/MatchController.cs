using System;
using Godsbound.Core;
using Godsbound.Core.Match;
using Godsbound.Core.Economy;
using Godsbound.Core.Combat;
using Godsbound.Core.Movement;
using Godsbound.Core.Gods;
using Godsbound.Data;
using System.Linq;
using Godsbound.Core.AI;
using Godsbound.Core.Decks;
using Godsbound.Core.Setup;
using UnityEngine;

namespace Godsbound.Presentation
{
    [DisallowMultipleComponent]
    public sealed class MatchController : MonoBehaviour
    {
        public MatchState State { get; private set; }
        public bool Paused { get; private set; }
        public event Action Resetting;
        private readonly MatchLoop loop = new MatchLoop();
        private BoardView board;
        private BuildingsView buildings;
        private UnitsView units;
        private DamageNumbers hits;

        /// <summary>
        /// The tutorial callout queue (U36). Ticked here, drawn by the HUD. Constructed OFF and with
        /// no settings read: a field initializer runs inside the MonoBehaviour constructor, during
        /// deserialization, where touching PlayerPrefs is not allowed. NewMatch reads the toggle.
        /// </summary>
        public TutorialHints Hints { get; } = new TutorialHints(enabled: false);
        private float lastAspect;
        private bool aiEnabled;

        /// <summary>The deck this match is being played with (U34).</summary>
        public DeckPreset Deck { get; private set; }

        /// <summary>The faction the AI drew for it.</summary>
        public string AiFaction { get; private set; }

        private BoardHalf playerHalf;
        private string[] aiHalfRows;

        private void Awake() { Initialize(); }

        /// <summary>
        /// Build the match the player's deck describes: its pantheon, its four gods, its painted half
        /// and the buildings it placed, against an AI half rolled for this arena.
        /// </summary>
        /// <param name="deck">The deck to play. Omitted, the active saved slot is used, and its
        /// faction's shipped deck when no slot holds one.</param>
        public void Initialize(BoardView boardView = null, BuildingsView buildingsView = null, bool enableAi = true,
                               DeckPreset deck = null, string storePath = null, System.Random rng = null)
        {
            if (State != null) return;
            aiEnabled = enableAi;
            var database = GameDataLoader.Load();
            var store = new DeckStore(database, storePath);
            store.Load();
            Deck = DeckRules.Normalize(database, deck) ?? store.Active(useDefault: true);
            var roll = rng ?? new System.Random();
            AiFaction = OpponentFor(Deck.faction, roll);
            Func<int, string> faction = side => side == 0 ? Deck.faction : AiFaction;
            var gods = new[] { GodState.Create(0, database, Deck.faction, Deck.gods),
                               GodState.Default(1, database, AiFaction) };
            // The AI's layout is rolled ONCE per arena: a rematch fights the same board, and only
            // coming back through the menu builds a new one — the browser's own rule.
            var aiData = AiDataLoader.Load();
            var layout = aiData.layouts[AiLayout.Pick(aiData, () => roll.NextDouble())];
            var aiBuildings = AiLayout.Buildings(layout).ToList();
            playerHalf = BoardHalf.FromPreset(Deck);
            var plan = AiLayout.GenerateTerrain(database, aiData, aiBuildings, AiFaction, () => roll.NextDouble());
            aiHalfRows = AiLayout.Rows(aiData, plan, AiFaction);
            var terrain = new TerrainMap();
            // Named apart from the BuildingsView field below: one is the six walls, the other draws them.
            var walls = BoardSetup.Compose(terrain, database, playerHalf, aiHalfRows, aiBuildings);
            State = new MatchState(database, terrain: terrain, buildings: walls,
                combatContext: new CombatContext { FactionForSide = faction },
                movementContext: new MovementContext { FactionForSide = faction }, gods: gods);
            board = boardView != null ? boardView : FindAnyObjectByType<BoardView>();
            buildings = buildingsView != null ? buildingsView : FindAnyObjectByType<BuildingsView>();
            units = GetComponent<UnitsView>() ?? gameObject.AddComponent<UnitsView>();
            hits = GetComponent<DamageNumbers>() ?? gameObject.AddComponent<DamageNumbers>();
            if (GetComponent<PowerFx>() == null) gameObject.AddComponent<PowerFx>();
            // The AI's casts start in Core, so nothing in the UI would show them. BattleHud already
            // draws the player's own (it knows the aim before the cast), so only the other side's
            // are drawn here, in the enemy's colour.
            State.Powers.Cast += OnPowerCast;
            if (GetComponent<BattleHud>() == null) gameObject.AddComponent<BattleHud>();
            NewMatch();
        }
        public void NewMatch()
        {
            Resetting?.Invoke();
            GetComponent<PowerFx>()?.Clear();
            State.Reset();
            var data = PresentationData.Load();
            State.Resources[0] = new Purse(data.food, data.favor);
            State.Resources[1] = new Purse(data.aiFood, data.aiFavor);
            // A reset restores the shipped map, so the two halves are composed onto it again.
            if (playerHalf != null && aiHalfRows != null) BoardSetup.Compose(State.Terrain, playerHalf, aiHalfRows);
            if (aiEnabled) State.Ai = AiController.Create(State, AiDataLoader.Load(), AiFaction);
            State.Phase = MatchPhase.Setup;
            Paused = false;
            if (board != null) board.Bind(State.Terrain);
            if (buildings != null) buildings.Bind(State);
            if (units != null) units.Bind(State);
            // Re-bound on every reset: the old subscription is dropped and the board clears of
            // numbers left over from the match just finished.
            if (hits != null) hits.Bind(State);
            // A restart re-runs the tutorial from the top, and picks up the toggle if it changed
            // while the player was in the menu.
            Hints.Enabled = Godsbound.Data.GameSettings.Tutorial;
            Hints.Reset();
            FrameCamera();
        }
        private void OnPowerCast(int side, string key, Hex at)
        {
            if (side == 0) return;
            GetComponent<PowerFx>()?.Spawn(PowerAim.Footprint(State, key, at), new Color(0.85f, 0.35f, 0.3f, 0.9f));
        }

        private void OnDestroy() { if (State != null) State.Powers.Cast -= OnPowerCast; }

        /// <summary>
        /// Who the player fights. Egypt and China answer each other, as the browser pairs them; the
        /// two pantheons the AI does not play draw one of those two at random.
        /// </summary>
        public static string OpponentFor(string playerFaction, System.Random rng)
        {
            if (playerFaction == "egypt") return "china";
            if (playerFaction == "china") return "egypt";
            return rng.NextDouble() < 0.5 ? "egypt" : "china";
        }

        public void Begin() { if (!State.Objectives.Resolved) { State.Phase = MatchPhase.Battle; Paused = false; } }
        public void SetPaused(bool paused) { Paused = paused; if (paused) GetComponent<BattleHud>()?.Cancel(); }
        /// <summary>One real frame: sub-stepped simulation, then one view refresh for both views.</summary>
        /// <remarks>The views refresh HERE, not in their own LateUpdate, so a frame syncs each of
        /// them exactly once; the EditMode tests drive this method directly and call Sync themselves
        /// where they need a refresh without a tick.</remarks>
        public void Advance(float dt)
        {
            if (Paused || State == null) return;
            loop.Advance(State, dt);
            if (buildings != null) buildings.Sync();
            if (units != null) units.Sync();
            if (hits != null) hits.Sync();
            // The bar knows a drag is hunting for a building; the board is what can show it (U40).
            if (buildings != null)
                buildings.DeployHighlight = GetComponent<BattleHud>()?.WantsDeployTargets == true ? 0 : -1;
            // After the step, so a hint's clock is the match's own: a paused match pauses the hints.
            Hints.Tick(State.Elapsed);
            if (board != null) board.SyncTerrain();
            GetComponent<PowerFx>()?.Tick(dt);
        }
        private void Update()
        {
            Advance(Time.deltaTime);
            if (Camera.main != null && !Mathf.Approximately(Camera.main.aspect, lastAspect)) FrameCamera();
        }
        private void OnApplicationFocus(bool focus) { if (!focus) SetPaused(true); }
        public void FrameCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var framing = BoardViewport.Frame(BoardWorld.BoardBounds(BoardWorld.UnitLayout()), cam.aspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
            cam.orthographic = true;
            cam.orthographicSize = framing.OrthographicSize;
            cam.transform.position = new Vector3(framing.Center.x, framing.Center.y, -10f);
            lastAspect = cam.aspect;
            var preview = cam.GetComponent<UiSafeAreaPreview>();
            if (preview != null) preview.enabled = false;
        }
    }
}
