using System.Linq;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Decks;
using Godsbound.Core.Setup;

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
        public const float TitleHeight = 34f, RowHeight = 30f, Pad = 6f;
        private SetupController controller;
        private GUIStyle title, body, tile;
        private Vector2 scroll;

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
            var titleBar = Title(Screen.width, Screen.height);
            var controls = Controls(Screen.width, Screen.height);
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
            if (Session.Step == SetupStep.Terrain) HandleBoard();
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
        }

        private void DrawFaction()
        {
            GUILayout.BeginHorizontal();
            foreach (var faction in controller.Store == null ? new string[0]
                     : new[] { "egypt", "china", "greek", "aztec" })
            {
                bool chosen = Session.Faction == faction;
                GUI.color = chosen ? Color.yellow : Color.white;
                if (GUILayout.Button(faction, tile, GUILayout.Height(RowHeight * 1.4f))) Session.ChooseFaction(faction);
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();
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
            foreach (var brush in new[] { 'F', 'M', 'W', 'D' })
            {
                bool held = Session.Brush == brush;
                GUI.color = held ? Color.yellow : Color.white;
                string label = brush == 'D' ? "erase" : $"{brush} {Session.Remaining(brush)}";
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
