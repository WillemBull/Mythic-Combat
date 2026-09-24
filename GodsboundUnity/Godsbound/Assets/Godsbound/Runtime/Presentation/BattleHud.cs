using System.Collections.Generic;
using System.Linq;
using Godsbound.Core;
using Godsbound.Core.Data;
using Godsbound.Core.Gods;
using Godsbound.Core.Match;
using Godsbound.Core.Training;
using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>Where the bottom bar's three rows sit. Depends only on the band, never on match state,
    /// so unlocking a god cannot change the bar's height (HANDOFF's fixed-height rule).</summary>
    public readonly struct HudLayout
    {
        public readonly Rect Hint, Cards, Gods;
        public HudLayout(Rect hint, Rect cards, Rect gods) { Hint = hint; Cards = cards; Gods = gods; }
    }

    /// <summary>What one god tile shows, derived purely from Core state.</summary>
    public readonly struct GodTileView
    {
        public readonly string Key, Name, Label;
        public readonly bool Unlocked, Previewed, Affordable, Recharging;
        public readonly int UnlockCost, PowerCost;
        public GodTileView(string key, string name, string label, bool unlocked, bool previewed,
                           bool affordable, bool recharging, int unlockCost, int powerCost)
        {
            Key = key; Name = name; Label = label; Unlocked = unlocked; Previewed = previewed;
            Affordable = affordable; Recharging = recharging; UnlockCost = unlockCost; PowerCost = powerCost;
        }
    }

    /// <summary>Local, dependency-free mouse/touch controls; all gameplay commands go to Core.</summary>
    public sealed class BattleHud : MonoBehaviour
    {
        public const float HintHeight = 30f, GodRowHeight = 30f, RowGap = 4f, EdgePad = 8f;
        private MatchController controller;
        private DeploymentDraft draft;
        private readonly GodTapFlow godTaps = new GodTapFlow(0);
        private readonly PowerAim aim = new PowerAim();
        /// <summary>The player's armed power, if any (U30).</summary>
        public PowerAim Aim => aim;
        private readonly List<string> deck = new List<string>();
        private GodState subscribedGods, subscribedEnemyGods;
        private int handSize = 6;
        /// <summary>Raised when a ready, unlocked god is tapped (it arms or disarms).</summary>
        public event System.Action<string> PowerArmRequested;
        /// <summary>Raised after a cast that paid, with the power key and whether it took hold.</summary>
        public event System.Action<string, bool> PowerCast;
        public GodTapFlow GodTaps => godTaps;
        public IReadOnlyList<string> Deck => deck;
        private const string InitialHint = "Drag a card through your building, then draw a route.";
        private string hint = InitialHint;
        // Per-card cost text never changes for a unit, so it is built once per key rather than on
        // every OnGUI event (which fires several times a frame).
        private readonly Dictionary<string, string> costLabels = new Dictionary<string, string>();
        private GUIStyle text, cardStyle, heading;
        private int pointerControl;
        public DeploymentDraft Draft => draft;
        private void OnEnable() { Bind(GetComponent<MatchController>()); }
        /// <summary>Attach to a controller: follow its resets and deal from its match. Idempotent.
        /// OnEnable calls it; EditMode tests call it directly because OnEnable does not run there.</summary>
        public void Bind(MatchController target)
        {
            if (controller != null) controller.Resetting -= ResetCards;
            controller = target;
            if (controller == null) return;
            controller.Resetting += ResetCards;
            fx = controller.GetComponent<PowerFx>();
            if (controller.State != null) EnsureDeck(controller.State);
        }
        private void OnDisable()
        {
            Cancel();
            if (controller != null) controller.Resetting -= ResetCards;
            Subscribe(null);
            SubscribeEnemy(null);
        }
        private void ResetCards() { Cancel(); aim.Cancel(); deck.Clear(); costLabels.Clear(); godTaps.Clear(); hint = InitialHint; }

        /// <summary>
        /// Handle a ready god tap: arm it, or disarm it if it was armed. Returns the hint line, worded
        /// as the browser's <c>onGodTap</c> tail.
        /// </summary>
        public string ToggleArm(MatchState state, string key)
        {
            aim.Toggle(key);
            PowerArmRequested?.Invoke(key);
            var g = state.Gods[0].Slot(key).Def;
            if (!aim.IsArmed) return "Power cancelled.";
            string tail = state.Powers.Definition(key)?.Aim == PowerTarget.EnemyGod ? "Choose an enemy god, or tap the board." : "Tap a target.";
            return $"{g.power}: {PresentationData.Load().HintFor(key)} {tail}";
        }

        /// <summary>Cast the armed power at a board hex and describe the outcome.</summary>
        public string CastArmedAt(MatchState state, Hex target)
        {
            var key = aim.Armed;
            var g = state.Gods[0].Slot(key)?.Def;
            var result = aim.CastAt(state, target);
            return Describe(state, key, g, result, target);
        }

        /// <summary>Isis: lock the chosen enemy god.</summary>
        public string CastArmedOnGod(MatchState state, string enemyGod)
        {
            var key = aim.Armed;
            var g = state.Gods[0].Slot(key)?.Def;
            var result = aim.PickGod(state, enemyGod);
            if (result == null) return "That god is not unlocked, or is already locked.";
            return Describe(state, key, g, result, null);
        }

        private string Describe(MatchState state, string key, Core.Data.GodData g, CastResult? result, Hex? target)
        {
            if (result == null || g == null) return hint;
            if (!result.Value.Paid) return "Power unavailable.";
            PowerCast?.Invoke(key, result.Value.Applied);
            if (result.Value.Applied && target.HasValue && fx != null)
                fx.Spawn(PowerAim.Footprint(state, key, target.Value), new Color(1f, 0.85f, 0.4f, 0.9f));
            return $"{g.power} cast.";
        }

        private PowerFx fx;
        private void Subscribe(GodState gods)
        {
            if (subscribedGods == gods) return;
            if (subscribedGods != null) subscribedGods.GodUnlocked -= OnGodUnlocked;
            subscribedGods = gods;
            if (subscribedGods != null) subscribedGods.GodUnlocked += OnGodUnlocked;
        }
        private void SubscribeEnemy(GodState gods)
        {
            if (subscribedEnemyGods == gods) return;
            if (subscribedEnemyGods != null) subscribedEnemyGods.GodUnlocked -= OnEnemyGodUnlocked;
            subscribedEnemyGods = gods;
            if (subscribedEnemyGods != null) subscribedEnemyGods.GodUnlocked += OnEnemyGodUnlocked;
        }
        private void OnEnemyGodUnlocked(GodSlot slot) { hint = $"Enemy unlocked {slot.Def.name}!"; }
        // DECK.push(g.myth): the myth joins the END of the rotation, not the visible hand.
        private void OnGodUnlocked(GodSlot slot) { if (slot.Def.HasMyth) deck.Add(slot.Def.myth); }

        /// <summary>
        /// Bind to the match's gods and deal the starting deck if it is empty. Returns the player's
        /// faction, which follows the player's gods — and so follows the deck the match was built with.
        /// </summary>
        public string EnsureDeck(MatchState state)
        {
            string faction = state.Gods[0].Faction != "" ? state.Gods[0].Faction : "egypt";
            Subscribe(state.Gods[0]);
            SubscribeEnemy(state.Gods[1]);
            if (PresentationHandSize > 0) handSize = PresentationHandSize;
            if (deck.Count == 0)
            {
                // U34: the cards are the ones the player CHOSE. A match built without a deck (a test,
                // or a scene opened straight into the board) still deals the faction's shipped hand.
                var chosen = controller != null ? controller.Deck : null;
                deck.AddRange(chosen != null && chosen.faction == faction
                    ? chosen.loadout.Concat(chosen.heroes).ToList()
                    : InitialDeck(state.Database.Faction(faction)));
                deck.AddRange(state.Gods[0].MythDeck); // gods unlocked before the deck was dealt
            }
            return faction;
        }
        public int HandSize => handSize;

        /// <summary>The player's starting deck: four humans then two heroes (browser DECK_INITIAL).</summary>
        public static List<string> InitialDeck(FactionData faction) =>
            faction.defaultLoadout.Concat(faction.defaultHeroes).ToList();

        /// <summary>
        /// Port of <c>useCard</c>. The used card goes to the back; the first waiting card beyond the
        /// hand takes its slot. A card outside the hand cannot be used.
        /// </summary>
        public static void RotateHand(List<string> deck, string key, int handSize)
        {
            int i = deck.IndexOf(key);
            if (i < 0 || i >= handSize) return;
            string next = null;
            if (deck.Count > handSize) { next = deck[handSize]; deck.RemoveAt(handSize); }
            deck.RemoveAt(i);
            if (next != null) deck.Insert(i, next);
            deck.Add(key);
        }

        /// <summary>Rows inside the fixed bottom band: hint, cards, then the four god tiles.</summary>
        public static HudLayout Layout(Rect band)
        {
            var hintRect = new Rect(band.x + EdgePad, band.y + 3f, band.width - 2 * EdgePad, HintHeight);
            float godsY = band.yMax - RowGap - GodRowHeight;
            var gods = new Rect(band.x + EdgePad, godsY, band.width - 2 * EdgePad, GodRowHeight);
            float cardsY = hintRect.yMax + RowGap;
            var cardsRect = new Rect(band.x + EdgePad, cardsY, band.width - 2 * EdgePad, godsY - RowGap - cardsY);
            return new HudLayout(hintRect, cardsRect, gods);
        }

        /// <summary>The god tile at <paramref name="index"/> of the player's selected four.</summary>
        public static GodTileView TileView(MatchState state, GodTapFlow flow, int index)
        {
            var slot = state.Gods[0].Selected[index];
            var g = slot.Def;
            int unlock = state.GodUnlockCost(0, g), power = state.GodPowerCost(0, g);
            float favor = state.Resources[0].Favor;
            bool recharging = slot.Unlocked && (state.Elapsed < slot.CooldownUntil || state.Elapsed < slot.LockedUntil);
            string label = !slot.Unlocked ? $"{g.name}\n{unlock} favor"
                : recharging ? $"{g.name}\n{Mathf.CeilToInt(Mathf.Max(slot.CooldownUntil, slot.LockedUntil) - state.Elapsed)}s"
                : $"{g.name}\n{g.power} {power}";
            return new GodTileView(g.key, g.name, label, slot.Unlocked, flow.Preview == g.key,
                slot.Unlocked ? favor >= power : favor >= unlock, recharging, unlock, power);
        }

        /// <summary>The hint line for a god tap, worded as the browser's <c>onGodTap</c>.</summary>
        public static string GodHint(MatchState state, GodTapResult result, string key)
        {
            var slot = state.Gods[0].Slot(key);
            if (slot == null) return null;
            var g = slot.Def;
            switch (result)
            {
                case GodTapResult.Previewed:
                    return g.name + " — " + (string.IsNullOrEmpty(g.passive) ? "No passive." : "Passive: " + g.passive) +
                           $" Tap again to unlock ({state.GodUnlockCost(0, g)} favor).";
                case GodTapResult.Unlocked:
                    var myth = g.HasMyth ? state.Database.Unit(state.Gods[0].Faction, g.myth) : null;
                    return $"{g.power} available." + (myth != null ? $" {myth.name} joined your card rotation." : "");
                case GodTapResult.CannotAffordUnlock: return $"Need {state.GodUnlockCost(0, g)} Favor to unlock {g.name}.";
                case GodTapResult.Recharging: return $"{g.power} recharging…";
                case GodTapResult.PowerLocked: return $"{g.name}'s power is locked!";
                case GodTapResult.CannotAffordPower: return $"Need {state.GodPowerCost(0, g)} Favor for {g.power}.";
                case GodTapResult.ArmRequested: return null; // ToggleArm words the armed/cancelled line
                default: return null;
            }
        }
        public void Cancel()
        {
            draft?.Cancel(); draft = null;
            if (pointerControl != 0 && GUIUtility.hotControl == pointerControl) GUIUtility.hotControl = 0;
        }
        // Pixel coordinates, with a bottom-left origin as used by Camera.ScreenToWorldPoint.
        public static bool TryHex(Camera camera, Vector2 screen, out Hex hex)
        {
            hex = default;
            if (camera == null || !camera.pixelRect.Contains(screen)) return false;
            var viewport = camera.ScreenToViewportPoint(screen);
            if (viewport.y < BoardViewport.DefaultBottomFraction || viewport.y > 1f - BoardViewport.DefaultTopFraction) return false;
            var p = BoardWorld.FromWorld(camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -camera.transform.position.z)));
            // PixelToHex expects the logical layout's projected space, undoing its own pivot squash.
            var layout = BoardWorld.UnitLayout(); var projected = layout.ToScreen(p);
            var picked = layout.PixelToHex(projected.X, projected.Y);
            if (!picked.HasValue) return false;
            hex = picked.Value; return true;
        }
        public static Rect BottomBand(float width, float height) =>
            new Rect(0, height * (1f - BoardViewport.DefaultBottomFraction), width, height * BoardViewport.DefaultBottomFraction);
        public static float HudScale(float width, float height) =>
            Mathf.Min(width / BoardViewport.DesignWidthPx, height / BoardViewport.DesignHeightPx);
        private void OnGUI()
        {
            if (controller == null || controller.State == null) return;
            var state = controller.State;
            string faction = EnsureDeck(state);
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                cardStyle = new GUIStyle(GUI.skin.box) { fontSize = 10, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                heading = new GUIStyle(text) { fontSize = 15, fontStyle = FontStyle.Bold };
            }
            float scale = HudScale(Screen.width, Screen.height), width = Screen.width / scale, height = Screen.height / scale;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            var bottom = BottomBand(width, height);
            var layout = Layout(bottom);
            float topHeight = height * BoardViewport.DefaultTopFraction;
            GUI.Box(new Rect(0, 0, width, topHeight), GUIContent.none);
            GUI.Box(bottom, GUIContent.none);
            int seconds = Mathf.CeilToInt(state.Objectives.Clock.Remaining);
            GUI.Label(new Rect(8, 0, width - 16, topHeight * 0.34f), "GODSBOUND   " + seconds / 60 + ":" + (seconds % 60).ToString("00"), heading);
            GUI.Label(new Rect(8, topHeight * 0.34f, width - 16, topHeight * 0.30f), $"Food {state.Resources[0].Food:0}   Favor {state.Resources[0].Favor:0}", text);
            if (state.Phase == MatchPhase.Setup)
            {
                if (GUI.Button(new Rect(8, topHeight * 0.65f, 88, topHeight * 0.33f), "Start battle")) controller.Begin();
            }
            else if (!state.Objectives.Resolved && GUI.Button(new Rect(8, topHeight * 0.65f, 88, topHeight * 0.33f), controller.Paused ? "Resume" : "Pause"))
                controller.SetPaused(!controller.Paused);
            if (GUI.Button(new Rect(width - 96, topHeight * 0.65f, 88, topHeight * 0.33f), "Restart")) { controller.NewMatch(); GUI.matrix = oldMatrix; return; }
            string status = state.Objectives.Resolved ? state.Objectives.Result.Value.Outcome.ToString() : controller.Paused ? "Paused" :
                state.Ai != null ? "China - " + state.Ai.Choice.Profile.style : "Training battlefield";
            GUI.Label(new Rect(98, topHeight * 0.65f, width - 196, topHeight * 0.33f), status, text);
            var enemyGodsToPick = aim.IsArmed && state.Powers.Definition(aim.Armed)?.Aim == PowerTarget.EnemyGod
                ? PowerAim.LockableEnemyGods(state).ToList() : null;
            if (enemyGodsToPick != null && enemyGodsToPick.Count > 0)
            {
                float w = layout.Hint.width / enemyGodsToPick.Count;
                for (int i = 0; i < enemyGodsToPick.Count; i++)
                    if (GUI.Button(new Rect(layout.Hint.x + i * w, layout.Hint.y, w - 3, layout.Hint.height), "Lock " + enemyGodsToPick[i].Def.name)
                        && state.Live && !controller.Paused)
                        hint = CastArmedOnGod(state, enemyGodsToPick[i].Key);
            }
            else GUI.Label(layout.Hint, hint, text);

            var evt = Event.current;
            pointerControl = GUIUtility.GetControlID(FocusType.Passive);
            if (draft != null && (!state.Live || controller.Paused || evt.type == EventType.Ignore ||
                evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)) Cancel();
            int shown = Mathf.Min(handSize, deck.Count);
            float cardWidth = layout.Cards.width / Mathf.Max(1, shown);
            for (int i = 0; i < shown; i++)
            {
                var def = state.Database.Unit(faction, deck[i]);
                if (def == null) continue;
                var rect = new Rect(layout.Cards.x + i * cardWidth, layout.Cards.y, cardWidth - 3, layout.Cards.height);
                int queued = 0;
                var pending = state.Training.Pending;
                for (int q = 0; q < pending.Count; q++)
                    if (pending[q].Side == 0 && pending[q].Def.key == def.key) queued++;
                bool enabled = state.Live && !controller.Paused &&
                    UnitPrice.CanAfford(state.Resources[0], def, state.TrainingModifiersFor(0), state.Database.Training);
                var oldColor = GUI.color;
                GUI.color = enabled ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                if (!costLabels.TryGetValue(def.key, out var costLabel))
                    costLabels[def.key] = costLabel = $"{def.name}\n{def.costFood} food" + (def.costFavor > 0 ? $" • {def.costFavor} favor" : "");
                GUI.Box(rect, queued > 0 ? costLabel + "\nQueued: " + queued : costLabel, cardStyle);
                GUI.color = oldColor;
                if (enabled && evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    Cancel(); draft = new DeploymentDraft(state, def); GUIUtility.hotControl = pointerControl;
                    hint = "Drag through a gold, purple or red building on your half."; evt.Use();
                }
            }
            DrawGodTiles(state, layout.Gods, evt);
            if (aim.IsArmed && draft == null)
            {
                Vector2 screen = new Vector2(evt.mousePosition.x * scale, Screen.height - evt.mousePosition.y * scale);
                if (TryHex(Camera.main, screen, out var target))
                {
                    if (evt.type == EventType.MouseDown && evt.button == 0 && state.Live && !controller.Paused)
                    { hint = CastArmedAt(state, target); evt.Use(); }
                    else if (evt.type == EventType.Repaint)
                        DrawRoute(PowerAim.Footprint(state, aim.Armed, target).ToList(), new Color(1f, 0.55f, 0.25f, 0.8f), scale);
                }
                if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { aim.Cancel(); hint = "Power cancelled."; evt.Use(); }
            }
            if (draft != null)
            {
                Vector2 screen = new Vector2(evt.mousePosition.x * scale, Screen.height - evt.mousePosition.y * scale);
                bool onBoard = TryHex(Camera.main, screen, out var hex);
                if (evt.type == EventType.MouseDrag)
                {
                    if (onBoard) draft.Visit(hex);
                    if (draft.Source != null) hint = "Draw your route. Release to train; Esc cancels.";
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    bool legal = onBoard && state.Terrain.RouteOk(hex, draft.Definition.IsFlying, state.Buildings.RouteBlocker);
                    var used = draft.Definition;
                    string reason = draft.Source == null ? "Drag through your building first." : draft.Route.Count == 0 ?
                        "Draw a route out of your building." : !legal ? "Release on an open battlefield hex." : "Route cancelled. No resources spent.";
                    bool paid = draft.Commit(legal);
                    hint = paid ? used.name + " training…" : reason;
                    if (paid) RotateHand(deck, used.key, handSize);
                    Cancel(); evt.Use();
                }
                else if (evt.type == EventType.Repaint)
                {
                    DrawRoute(draft.Route, new Color(1f, 0.8f, 0.2f), scale);
                    DrawRoute(draft.Continuation, new Color(0.4f, 0.85f, 1f, 0.65f), scale);
                }
            }
            if (state.Objectives.Resolved)
                GUI.Box(new Rect(45, height * 0.42f, width - 90, 70), state.Objectives.Result.Value.Outcome + "\nRestart to play again.", heading);
            GUI.matrix = oldMatrix;
        }
        private void DrawGodTiles(MatchState state, Rect row, Event evt)
        {
            var gods = state.Gods[0].Selected;
            if (gods.Count == 0) return;
            float w = row.width / GodState.SelectionSize;
            for (int i = 0; i < gods.Count; i++)
            {
                var view = TileView(state, godTaps, i);
                var rect = new Rect(row.x + i * w, row.y, w - 4, row.height);
                var oldColor = GUI.color;
                GUI.color = aim.Armed == view.Key ? new Color(1f, 0.55f, 0.25f)
                    : view.Previewed ? new Color(0.78f, 0.55f, 1f)
                    : view.Unlocked ? (view.Recharging ? new Color(0.6f, 0.6f, 0.6f) : new Color(1f, 0.85f, 0.45f))
                    : view.Affordable ? new Color(0.8f, 0.7f, 1f) : new Color(0.55f, 0.55f, 0.6f);
                GUI.Box(rect, view.Label, cardStyle);
                GUI.color = oldColor;
                bool usable = state.Live && !controller.Paused && draft == null;
                if (usable && evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    var result = godTaps.Tap(state, view.Key);
                    hint = result == GodTapResult.ArmRequested ? ToggleArm(state, view.Key)
                        : GodHint(state, result, view.Key) ?? hint;
                    evt.Use();
                }
            }
        }
        private static int PresentationHandSize
        {
            get
            {
                if (cachedHandSize < 0) cachedHandSize = PresentationData.Load()?.handSize ?? 0;
                return cachedHandSize;
            }
        }
        private static int cachedHandSize = -1;
        private static void DrawRoute(IReadOnlyList<Hex> route, Color color, float scale)
        {
            if (Camera.main == null) return;
            var oldColor = GUI.color; GUI.color = color;
            foreach (var h in route)
            {
                var p = Camera.main.WorldToScreenPoint(BoardWorld.CenterOf(h, BoardWorld.UnitLayout()));
                GUI.DrawTexture(new Rect(p.x / scale - 3, (Screen.height - p.y) / scale - 3, 6, 6), Texture2D.whiteTexture);
            }
            GUI.color = oldColor;
        }
    }
}
