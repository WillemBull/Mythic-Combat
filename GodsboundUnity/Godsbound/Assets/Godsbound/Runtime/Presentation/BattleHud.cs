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
        // The band is fixed at 153 design px and every pixel is spoken for:
        // 3 + Hint 26 + 4 + Cards 68 + 4 + Gods 44 + 4. The god row grew to a thumb-sized 44 (U37)
        // and the four pixels came out of the hint row, which holds one line of text, rather than
        // out of the cards — the browser's .card{min-height:68px} is a floor, not a preference.
        public const float HintHeight = 26f, GodRowHeight = 44f, RowGap = 4f, EdgePad = 8f;

        /// <summary>
        /// The smallest a control may be and still be hit reliably with a thumb: 44 design px, which
        /// is Apple's 44pt and Google's 48dp rounded to the frame this HUD is laid out in (U37).
        /// </summary>
        public const float MinTouch = 44f;
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
        // U40: the ghost needs the unit's own art, so the HUD keeps a catalog of its own. Resolve
        // caches per resource, so this costs one sprite for the card being dragged.
        private UnitArtCatalog ghostArt;
        // A failed release explains itself where the finger let go, not only in the bar at the top
        // of the screen. Ours, not the browser's — on a phone that bar is nowhere near your thumb.
        private string failure; private float failureUntil; private Vector2 failureAt;
        public const float FailureSeconds = 1.6f;

        /// <summary>
        /// True while a drag is looking for a building to start from, so the board can light up the
        /// ones that qualify (U40). Added for touch: the browser only says it in words.
        /// </summary>
        public bool WantsDeployTargets => draft != null && draft.Source == null;
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
            ghostArt?.Dispose(); ghostArt = null;
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
        public static Rect BottomBand(float width, float height) => BottomBand(width, height, default);

        /// <summary>
        /// The bar's CONTENT band, lifted clear of the home indicator. The painted backdrop still
        /// runs to the bottom of the glass (<see cref="BottomBackdrop"/>) — content respects the safe
        /// area, background fills behind it, which is how a phone UI is supposed to look (U37).
        /// </summary>
        public static Rect BottomBand(float width, float height, SafeInsets safe)
        {
            float bandHeight = height * BoardViewport.DefaultBottomFraction;
            return new Rect(safe.Left, height - safe.Bottom - bandHeight,
                            width - safe.Left - safe.Right, bandHeight);
        }

        /// <summary>The painted area behind the bottom bar: the band plus the inset below it.</summary>
        public static Rect BottomBackdrop(float width, float height, SafeInsets safe)
        {
            var band = BottomBand(width, height, safe);
            return new Rect(0, band.y, width, height - band.y);
        }

        /// <summary>The top bar's content rect, pushed below a notch.</summary>
        public static Rect TopBand(float width, float height, SafeInsets safe) =>
            new Rect(safe.Left, safe.Top, width - safe.Left - safe.Right,
                     height * BoardViewport.DefaultTopFraction);

        /// <summary>
        /// Screen-edge exclusions — notch, home indicator, rounded corners — in the HUD's design
        /// space rather than device pixels.
        /// </summary>
        /// <remarks>
        /// <c>Screen.safeArea</c> is in pixels with the origin at the BOTTOM left; IMGUI lays out from
        /// the TOP left. Getting that flip wrong pushes the bar into the notch on exactly the devices
        /// it was meant to protect, so the conversion lives here, alone, and is tested.
        /// </remarks>
        public readonly struct SafeInsets
        {
            public readonly float Top, Bottom, Left, Right;
            public SafeInsets(float top, float bottom, float left, float right)
            { Top = top; Bottom = bottom; Left = left; Right = right; }

            public static SafeInsets From(Rect safeArea, float screenWidth, float screenHeight, float scale)
            {
                if (scale <= 0f || screenWidth <= 0f || screenHeight <= 0f) return default;
                // A device that reports nothing useful gets no insets rather than nonsense ones.
                if (safeArea.width <= 0f || safeArea.height <= 0f) return default;
                return new SafeInsets(
                    Mathf.Max(0f, screenHeight - safeArea.yMax) / scale,
                    Mathf.Max(0f, safeArea.yMin) / scale,
                    Mathf.Max(0f, safeArea.xMin) / scale,
                    Mathf.Max(0f, screenWidth - safeArea.xMax) / scale);
            }
        }

        /// <summary>Where the top bar's own controls sit. Pure, so the touch sizes can be tested.</summary>
        public readonly struct TopBarLayout
        {
            public readonly Rect Title, Resources, Action, Status, Restart;
            public TopBarLayout(Rect title, Rect resources, Rect action, Rect status, Rect restart)
            { Title = title; Resources = resources; Action = action; Status = status; Restart = restart; }
        }

        /// <summary>
        /// Two rows inside the top band: a thin line of text, then full-height buttons. The browser
        /// put its buttons on the same line as the text, which at this size left them 20px tall —
        /// unhittable with a thumb, and the reason this step exists (U37).
        /// </summary>
        public static TopBarLayout TopBar(Rect band)
        {
            float buttons = Mathf.Max(MinTouch, band.height * 0.6f);
            float textHeight = Mathf.Max(0f, band.height - buttons - 1f);
            float y = band.y + textHeight + 1f;
            float half = (band.width - EdgePad * 2f) * 0.5f;
            const float buttonWidth = 88f;
            return new TopBarLayout(
                new Rect(band.x + EdgePad, band.y, half, textHeight),
                new Rect(band.x + EdgePad + half, band.y, half, textHeight),
                new Rect(band.x + EdgePad, y, buttonWidth, buttons),
                new Rect(band.x + EdgePad + buttonWidth + 6f, y,
                         band.width - EdgePad * 2f - buttonWidth * 2f - 12f, buttons),
                new Rect(band.xMax - EdgePad - buttonWidth, y, buttonWidth, buttons));
        }
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
            var safe = SafeInsets.From(Screen.safeArea, Screen.width, Screen.height, scale);
            var bottom = BottomBand(width, height, safe);
            var layout = Layout(bottom);
            var top = TopBand(width, height, safe);
            var bar = TopBar(top);
            // Backdrops run under the notch and the home indicator; the controls do not.
            GUI.Box(new Rect(0, 0, width, top.yMax), GUIContent.none);
            GUI.Box(BottomBackdrop(width, height, safe), GUIContent.none);
            int seconds = Mathf.CeilToInt(state.Objectives.Clock.Remaining);
            GUI.Label(bar.Title, "GODSBOUND   " + seconds / 60 + ":" + (seconds % 60).ToString("00"), heading);
            GUI.Label(bar.Resources, $"Food {state.Resources[0].Food:0}   Favor {state.Resources[0].Favor:0}", text);
            if (state.Phase == MatchPhase.Setup)
            {
                if (GUI.Button(bar.Action, "Start battle")) controller.Begin();
            }
            else if (!state.Objectives.Resolved && GUI.Button(bar.Action, controller.Paused ? "Resume" : "Pause"))
                controller.SetPaused(!controller.Paused);
            if (GUI.Button(bar.Restart, "Restart")) { controller.NewMatch(); GUI.matrix = oldMatrix; return; }
            string status = state.Objectives.Resolved ? state.Objectives.Result.Value.Outcome.ToString() : controller.Paused ? "Paused" :
                state.Ai != null ? "China - " + state.Ai.Choice.Profile.style : "Training battlefield";
            GUI.Label(bar.Status, status, text);
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
            // Before the cards and the board see it: a player reaching to dismiss a hint is not
            // trying to deploy underneath it. The callout itself is drawn at the end, on top.
            if (TutorialRect(top, bottom, out var callout, out _) &&
                evt.type == EventType.MouseDown && evt.button == 0 && callout.Contains(evt.mousePosition))
            { controller.Hints.Dismiss(); evt.Use(); }
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
                // The browser's .card.dragging{opacity:.35} — the card you are holding looks held.
                if (draft != null && draft.Definition.key == def.key) GUI.color *= new Color(1f, 1f, 1f, 0.35f);
                if (!costLabels.TryGetValue(def.key, out var costLabel))
                    costLabels[def.key] = costLabel = $"{def.name}\n{def.costFood} food" + (def.costFavor > 0 ? $" • {def.costFavor} favor" : "");
                GUI.Box(rect, queued > 0 ? costLabel + "\nQueued: " + queued : costLabel, cardStyle);
                GUI.color = oldColor;
                if (enabled && evt.type == EventType.MouseDown && evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    Cancel(); draft = new DeploymentDraft(state, def); GUIUtility.hotControl = pointerControl;
                    hint = DeploymentPrompt.PickedUp(def.name); evt.Use();
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
                    if (draft.Source != null) hint = DeploymentPrompt.RouteNext;
                    evt.Use();
                }
                else if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    bool legal = onBoard && state.Terrain.RouteOk(hex, draft.Definition.IsFlying, state.Buildings.RouteBlocker);
                    var used = draft.Definition;
                    bool affordable = UnitPrice.CanAfford(state.Resources[0], used, state.TrainingModifiersFor(0), state.Database.Training);
                    string reason = DeploymentPrompt.ForFailure(draft.Source != null, draft.Route.Count > 0, legal, affordable)
                                    ?? DeploymentPrompt.NoBuilding;
                    bool paid = draft.Commit(legal);
                    hint = paid ? DeploymentPrompt.Training(used.name) : reason;
                    if (paid) RotateHand(deck, used.key, handSize);
                    else { failure = reason; failureUntil = state.Elapsed + FailureSeconds; failureAt = evt.mousePosition; }
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
            DrawDragGhost(evt);
            DrawFailure(state);
            DrawTutorialCallout(top, bottom);
            GUI.matrix = oldMatrix;
        }
        /// <summary>
        /// Where roadmap 7.3's callout sits, in the HUD's design space: just above the bar, or under
        /// the top band over the board. False when there is nothing to show.
        /// </summary>
        public bool TutorialRect(Rect top, Rect bottom, out Rect rect, out string message)
        {
            rect = default; message = null;
            var current = controller?.Hints?.Current;
            if (current == null) return false;
            const float h = 44f, pad = 14f;
            // Bar hints sit just above the cards they are about; arena hints hang under the top bar,
            // over the board, where the thing being explained actually is.
            float y = current.Value.Where == TutorialHints.Anchor.Bar ? bottom.y - h - 6f : top.yMax + 12f;
            rect = new Rect(top.x + pad, y, top.width - pad * 2f, h);
            message = current.Value.Text;
            return true;
        }

        /// <summary>
        /// The browser's <c>#ghost</c>: the unit rides the pointer from the moment the card is picked
        /// up until it is released. The browser draws the unit's emoji; Unity has the sprite, so it
        /// draws that — same job, better material. Offset like the browser's
        /// transform:translate(-50%,-60%), so the art sits above the fingertip rather than under it.
        /// </summary>
        private void DrawDragGhost(Event evt)
        {
            if (draft == null || evt.type != EventType.Repaint) return;
            if (ghostArt == null) ghostArt = new UnitArtCatalog(PresentationData.Load());
            var sprite = ghostArt.Resolve(draft.Definition.key, 0);
            const float size = 46f;
            var rect = new Rect(evt.mousePosition.x - size * 0.5f, evt.mousePosition.y - size * 0.6f, size, size);
            if (sprite != null && sprite.texture != null)
                GUI.DrawTexture(rect, sprite.texture, ScaleMode.ScaleToFit);
            else GUI.Label(rect, draft.Definition.name, text);   // art missing: still say what is being carried
        }

        /// <summary>A refused release, explained where it happened. Times out on the match clock.</summary>
        private void DrawFailure(MatchState state)
        {
            if (failure == null || state.Elapsed >= failureUntil) { failure = null; return; }
            var rect = new Rect(failureAt.x - 90f, failureAt.y - 34f, 180f, 30f);
            var old = GUI.color;
            GUI.color = new Color(0.1f, 0.06f, 0.06f, 0.9f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = new Color(1f, 0.72f, 0.66f);
            GUI.Label(rect, failure, text);
            GUI.color = old;
        }

        /// <summary>Drawn last, so nothing else paints over the one thing the player is meant to read.</summary>
        private void DrawTutorialCallout(Rect top, Rect bottom)
        {
            if (!TutorialRect(top, bottom, out var rect, out var message)) return;
            var old = GUI.color;
            GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.92f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = old;
            GUI.Label(new Rect(rect.x + 8f, rect.y, rect.width - 34f, rect.height), message, text);
            GUI.Label(new Rect(rect.xMax - 26f, rect.y, 20f, rect.height), "x", text);
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
