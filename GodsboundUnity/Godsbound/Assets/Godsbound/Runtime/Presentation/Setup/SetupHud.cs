using System.Linq;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Decks;
using Godsbound.Core.Setup;
using Godsbound.Data;

namespace Godsbound.Presentation.Setup
{
    /// <summary>
    /// The setup screens: the home hub with its two deck slots, the faction, god, hero and card
    /// pickers, and the terrain step with its brushes and its draggable buildings.
    /// </summary>
    /// <remarks>
    /// <para>Drawing only. Every tap goes into <see cref="SetupSession"/> and every label is read
    /// back out of it, so what the player sees and what a match will be handed cannot drift.</para>
    /// <para>The same fixed portrait frame as the battle HUD: a top band for the step's title and
    /// counter and a bottom band for its controls, with the board — here the player's own half —
    /// in between.</para>
    /// </remarks>
    [RequireComponent(typeof(SetupController))]
    public sealed class SetupHud : MonoBehaviour
    {
        public const float TitleHeight = 34f, RowHeight = 44f, Pad = 6f;
        private SetupController controller;
        private GUIStyle title, body, tile;
        private Vector2 scroll;
        private UnitArtCatalog art;
        private Texture2D scrim;

        /// <summary>
        /// Which steps are about the BOARD. Only the terrain step is: it is where you build your
        /// half, so the board belongs on screen. The rest — the hub, the pantheon, the gods, the
        /// heroes, the cards — are menus, and until U42 they were drawn as a small panel floating
        /// over the arena, which is exactly what they looked like: a debug overlay on a game.
        /// </summary>
        public static bool ShowsBoard(SetupStep step) => step == SetupStep.Terrain;

        /// <summary>A menu step's content area: the whole screen under the title, not a band.</summary>
        public static Rect Sheet(float width, float height)
        {
            var bar = Title(width, height);
            return new Rect(Pad * 2f, bar.yMax, width - Pad * 4f, height - bar.yMax - Pad * 3f);
        }

        /// <summary>
        /// The face of a pantheon: the first god the database lists for it. A rule rather than a
        /// taste — the browser has arena art for only three of the four, so a portrait is the one
        /// image every pantheon is guaranteed to have.
        /// </summary>
        public static string FaceOf(Godsbound.Core.Data.GameDatabase db, string faction)
        {
            foreach (var god in db.GodsOf(faction)) return god.key;
            return null;
        }

        private SetupSession Session => controller != null ? controller.Session : null;

        private void OnEnable() { Bind(GetComponent<SetupController>()); }

        public void Bind(SetupController target)
        {
            controller = target;
            controller?.Initialize();
        }

        /// <summary>Where a step's controls sit — the bottom band, as on the board.</summary>
        public static Rect Controls(float width, float height) =>
            new Rect(0f, height * (1f - BoardViewport.DefaultBottomFraction), width,
                     height * BoardViewport.DefaultBottomFraction);

        /// <summary>Where the step's title and counter sit.</summary>
        public static Rect Title(float width, float height) =>
            new Rect(0f, 0f, width, height * BoardViewport.DefaultTopFraction);

        /// <summary>The counter a picking step shows: "2/4".</summary>
        public static string Counter(int picked, int cap) => $"{picked}/{cap}";

        private void EnsureArt()
        {
            if (art != null) return;
            art = new UnitArtCatalog(PresentationData.Load());
            // The browser's four-stop scrim, built once as a 1x64 ramp and stretched. IMGUI has no
            // gradient of its own, and a flat tint either drowns the painting or loses the text.
            var stops = art.Data.menuScrim ?? new[] { 0.42f, 0.18f, 0.70f, 0.92f };
            var at = new[] { 0f, 0.38f, 0.75f, 1f };
            scrim = new Texture2D(1, 64) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 64; y++)
            {
                float t = 1f - y / 63f;                 // texture rows run bottom-up; the panel reads top-down
                int i = 0; while (i < at.Length - 2 && t > at[i + 1]) i++;
                float span = Mathf.Max(0.0001f, at[i + 1] - at[i]);
                float a = Mathf.Lerp(stops[i], stops[i + 1], (t - at[i]) / span);
                scrim.SetPixel(0, y, new Color(3f / 255f, 7f / 255f, 10f / 255f, a));
            }
            scrim.Apply();
        }

        private void OnDestroy()
        {
            art?.Dispose();
            if (scrim != null) UnitArtCatalog.Release(scrim);
        }

        private void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            body = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            tile = new GUIStyle(GUI.skin.button) { fontSize = 11, wordWrap = true };
        }

        private void OnGUI()
        {
            if (Session == null) return;
            Styles();
            EnsureArt();
            // A menu is a screen, not a panel floating over the arena (U42). The board stays for the
            // one step that is about the board.
            bool board = ShowsBoard(Session.Step);
            var titleBar = Title(Screen.width, Screen.height);
            var controls = board ? Controls(Screen.width, Screen.height) : Sheet(Screen.width, Screen.height);
            if (!board) DrawBackdrop();
            GUI.Label(titleBar, Heading(), title);
            if (!string.IsNullOrEmpty(Session.Hint))
                GUI.Label(new Rect(0f, titleBar.yMax - 16f, Screen.width, 16f), Session.Hint, body);
            GUILayout.BeginArea(new Rect(controls.x + Pad, controls.y + Pad, controls.width - Pad * 2f, controls.height - Pad * 2f));
            switch (Session.Step)
            {
                case SetupStep.Home: DrawHome(); break;
                case SetupStep.Faction: DrawFaction(); break;
                case SetupStep.Gods: DrawPicks(Session.GodChoices().Select(g => (g.key, g.name)).ToArray(),
                    Session.Gods, DeckRules.GodCount, Session.ToggleGod, SetupStep.Heroes); break;
                case SetupStep.Heroes: DrawPicks(Session.HeroChoices().Select(u => (u.key, u.name)).ToArray(),
                    Session.Heroes, DeckRules.HeroCount, Session.ToggleHero, SetupStep.Units); break;
                case SetupStep.Units: DrawPicks(Session.UnitChoices().Select(u => (u.key, u.name)).ToArray(),
                    Session.Loadout, DeckRules.HandSize, Session.ToggleUnit, SetupStep.Terrain); break;
                case SetupStep.Terrain: DrawTerrain(); break;
                default: DrawHome(); break;
            }
            GUILayout.EndArea();
            if (board) HandleBoard();
        }

        /// <summary>
        /// The menu's own backdrop — the browser's #menupanel.home-active painting under its scrim.
        /// Drawn over the whole screen, which is what takes the menu out of the arena: the board is
        /// still behind it, framed exactly as the battle camera frames it, so nothing can drift out
        /// of step the way U33 feared when it made these three scenes identical.
        /// </summary>
        private void DrawBackdrop()
        {
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            var painting = art.Screen("menu");
            if (painting != null) GUI.DrawTexture(full, painting, ScaleMode.ScaleAndCrop);
            else { var was = GUI.color; GUI.color = new Color(0.02f, 0.03f, 0.04f); GUI.DrawTexture(full, Texture2D.whiteTexture); GUI.color = was; }
            if (scrim != null) GUI.DrawTexture(full, scrim, ScaleMode.StretchToFill);
        }

        private string Heading()
        {
            switch (Session.Step)
            {
                case SetupStep.Home: return "GODSBOUND";
                case SetupStep.Faction: return "CHOOSE YOUR PANTHEON";
                case SetupStep.Gods: return "CHOOSE YOUR GODS — " + Counter(Session.Gods.Count, DeckRules.GodCount);
                case SetupStep.Heroes: return "CHOOSE YOUR HEROES — " + Counter(Session.Heroes.Count, DeckRules.HeroCount);
                case SetupStep.Units: return "CHOOSE YOUR CARDS — " + Counter(Session.Loadout.Count, DeckRules.HandSize);
                case SetupStep.Terrain: return "BUILD YOUR HALF";
                default: return "GODSBOUND";
            }
        }

        private void DrawHome()
        {
            GUILayout.Label("Two decks live on this device.", body);
            for (int slot = 0; slot < Godsbound.Data.DeckStore.SlotCount; slot++)
            {
                var saved = controller.Store.Slot(slot);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Deck {slot + 1}: {(saved == null ? "empty" : saved.faction)}", body, GUILayout.Width(140f));
                if (GUILayout.Button("Edit", GUILayout.Height(RowHeight))) controller.Edit(slot);
                GUI.enabled = saved != null || slot == Session.Slot;
                if (GUILayout.Button("Play", GUILayout.Height(RowHeight))) controller.Play(slot);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            // Roadmap 7.3's switch. Read and written straight through, so a match started from here
            // picks up the change without anything having to remember to pass it along.
            bool tutorial = GUILayout.Toggle(Godsbound.Data.GameSettings.Tutorial, " Tutorial hints");
            if (tutorial != Godsbound.Data.GameSettings.Tutorial) Godsbound.Data.GameSettings.Tutorial = tutorial;
        }

        /// <summary>
        /// Four illustrated panels, two by two, each carrying its pantheon's face. The old version
        /// was four grey buttons with lower-case words on them, which told a new player nothing
        /// about what they were choosing between.
        /// </summary>
        private void DrawFaction()
        {
            var db = GameDataLoader.Load();   // cached after the first call; the faces are looked up once per frame
            var factions = new[] { "egypt", "china", "greek", "aztec" };
            var names = new[] { "EGYPT", "CHINA", "GREECE", "AZTEC" };
            float w = (Screen.width - Pad * 6f) * 0.5f;
            float h = Mathf.Max(96f, (Sheet(Screen.width, Screen.height).height - RowHeight * 2f - Pad * 6f) * 0.5f);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++)
                {
                    int i = row * 2 + col;
                    var rect = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
                    bool chosen = Session.Faction == factions[i];
                    var face = art.Portrait(FaceOf(db, factions[i]));
                    if (face != null) GUI.DrawTexture(rect, face, ScaleMode.ScaleAndCrop);
                    if (scrim != null) GUI.DrawTexture(rect, scrim, ScaleMode.StretchToFill);
                    var was = GUI.color;
                    GUI.color = chosen ? new Color(1f, 0.85f, 0.35f) : new Color(0.75f, 0.78f, 0.86f);
                    GUI.Label(new Rect(rect.x, rect.yMax - 26f, rect.width, 22f), names[i], title);
                    GUI.color = was;
                    // A chosen pantheon is outlined, not merely tinted: the painting behind it is
                    // busy enough that a colour shift alone reads as a trick of the art.
                    if (chosen) GUI.Box(rect, GUIContent.none);
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Session.ChooseFaction(factions[i]);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(Pad);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Gods →", GUILayout.Height(RowHeight))) Session.Go(SetupStep.Gods);
            if (GUILayout.Button("← Menu", GUILayout.Height(RowHeight))) Session.Go(SetupStep.Home);
        }

        private void DrawPicks((string key, string name)[] choices, System.Collections.Generic.IReadOnlyList<string> picked,
                               int cap, System.Func<string, bool> toggle, SetupStep next)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            int perRow = 4;
            for (int i = 0; i < choices.Length; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int c = i; c < i + perRow && c < choices.Length; c++)
                {
                    bool chosen = picked.Contains(choices[c].key);
                    GUI.color = chosen ? Color.yellow : Color.white;
                    if (GUILayout.Button(choices[c].name, tile, GUILayout.Height(RowHeight * 1.2f))) toggle(choices[c].key);
                    GUI.color = Color.white;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUI.enabled = picked.Count == cap;
            if (GUILayout.Button("Next →", GUILayout.Height(RowHeight))) Session.Go(next);
            GUI.enabled = true;
        }

        private void DrawTerrain()
        {
            GUILayout.BeginHorizontal();
            foreach (var brush in new[] { 'F', 'M', 'W', 'P' }) // 'P' is the eraser (was 'D')
            {
                bool held = Session.Brush == brush;
                GUI.color = held ? Color.yellow : Color.white;
                string label = brush == 'P' ? "erase" : $"{brush} {Session.Remaining(brush)}";
                if (GUILayout.Button(label, tile, GUILayout.Height(RowHeight))) Session.ChooseBrush(brush);
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Drag a building to move it. Tap with a brush to paint.", body);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← Cards", GUILayout.Height(RowHeight))) Session.Go(SetupStep.Units);
            GUI.enabled = Session.CanStart();
            if (GUILayout.Button("Battle", GUILayout.Height(RowHeight))) controller.Play(Session.Slot);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// The terrain step's pointer: a brush paints the hex under it, and a building under it is
        /// dragged by holding and released where it lands.
        /// </summary>
        private void HandleBoard()
        {
            var e = Event.current;
            if (e == null || e.type == EventType.Repaint || e.type == EventType.Layout) return;
            var screen = new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y);
            if (!BattleHud.TryHex(Camera.main, screen, out var hex)) return;
            if (hex.R < Board.PlayerRow0) return;
            var local = new Hex(hex.C, hex.R - Board.PlayerRow0);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (Session.Brush != '\0') Session.PaintAt(local);
                    else Session.BeginDrag(local);
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (Session.Drag.Active) { Session.DragTo(local); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (Session.Drag.Active) { Session.DropDrag(); e.Use(); }
                    break;
            }
        }
    }
}
