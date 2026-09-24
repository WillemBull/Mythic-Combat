using UnityEngine;
using UnityEngine.SceneManagement;
using Godsbound.Core.Match;
using Godsbound.Core.Objectives;

namespace Godsbound.Presentation
{
    /// <summary>
    /// What the player sees when the clock runs out or a capital falls: the result, why it went that
    /// way, and what the match cost both sides.
    /// </summary>
    /// <remarks>
    /// Ports <c>endMatch</c>'s screen — the title, the buildings-destroyed and damage lines that
    /// decide a timed match — and adds roadmap 7.2's stats block underneath. Rematch rebuilds the
    /// same arena, as the browser's does; Menu goes back to the deck hub, where a new arena is rolled.
    /// </remarks>
    [RequireComponent(typeof(MatchController))]
    public sealed class EndScreen : MonoBehaviour
    {
        public const float PanelWidth = 340f, PanelHeight = 250f, RowHeight = 30f;

        [Tooltip("Scene the Menu button returns to.")]
        [SerializeField] private string menuScene = "Menu";

        private MatchController controller;
        private GUIStyle title, body;

        /// <summary>The scene this screen asked for, for a test that must not load one.</summary>
        public string RequestedScene { get; private set; }

        private void OnEnable() { Bind(GetComponent<MatchController>()); }

        /// <summary>Attach to the match this screen reports on. OnEnable calls it; EditMode tests
        /// call it directly, because OnEnable does not run there.</summary>
        public void Bind(MatchController target) { controller = target; }

        /// <summary>The browser's own words for an outcome.</summary>
        public static string Title(MatchOutcome outcome) =>
            outcome == MatchOutcome.Victory ? "⚜ VICTORY ⚜" : outcome == MatchOutcome.Defeat ? "DEFEAT" : "DRAW";

        public static Color Tint(MatchOutcome outcome) =>
            outcome == MatchOutcome.Victory ? new Color(0.91f, 0.72f, 0.29f)
            : outcome == MatchOutcome.Defeat ? new Color(1f, 0.36f, 0.30f) : new Color(0.8f, 0.8f, 0.8f);

        /// <summary>
        /// The two lines that explain a result — the same pair the browser prints, read off the
        /// settled <see cref="MatchResult"/> so the screen cannot disagree with Core about who won.
        /// </summary>
        public static string Detail(MatchResult result) =>
            $"Buildings destroyed — You: {result.PlayerBuildingsDestroyed}  Enemy: {result.EnemyBuildingsDestroyed}\n" +
            $"Building damage dealt — You: {Mathf.Floor(result.PlayerBuildingDamage)}  " +
            $"Enemy: {Mathf.Floor(result.EnemyBuildingDamage)}";

        /// <summary>Roadmap 7.2's block: what each side trained, lost and spent.</summary>
        public static string Stats(MatchState state)
        {
            var s = state.Stats;
            return $"Units trained — You: {s.Trained[0]}  Enemy: {s.Trained[1]}\n" +
                   $"Units lost — You: {s.Lost[0]}  Enemy: {s.Lost[1]}\n" +
                   $"Favor on powers — You: {Mathf.Floor(s.FavorOnPowers[0])}  Enemy: {Mathf.Floor(s.FavorOnPowers[1])}\n" +
                   $"Favor on units — You: {Mathf.Floor(s.FavorOnUnits[0])}  Enemy: {Mathf.Floor(s.FavorOnUnits[1])}";
        }

        /// <summary>Centred on the screen, at a size that still reads on a narrow phone.</summary>
        public static Rect Panel(float width, float height) =>
            new Rect((width - PanelWidth) * 0.5f, (height - PanelHeight) * 0.5f, PanelWidth, PanelHeight);

        public void Rematch()
        {
            RequestedScene = null;
            controller.NewMatch();
            controller.Begin();
        }

        public void ToMenu()
        {
            RequestedScene = menuScene;
            if (Application.isPlaying && Application.CanStreamedLevelBeLoaded(menuScene)) SceneManager.LoadScene(menuScene);
        }

        private void OnGUI()
        {
            var state = controller != null ? controller.State : null;
            if (state == null || !state.Objectives.Resolved) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                body = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperLeft, wordWrap = true };
            }
            var panel = Panel(Screen.width, Screen.height);
            GUI.Box(panel, GUIContent.none);
            var outcome = state.Objectives.Result.Value.Outcome;
            GUI.color = Tint(outcome);
            GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), Title(outcome), title);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 12f, panel.y + 44f, panel.width - 24f, 40f),
                Detail(state.Objectives.Result.Value), body);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 92f, panel.width - 24f, 76f), Stats(state), body);
            if (GUI.Button(new Rect(panel.x + 12f, panel.yMax - 44f, panel.width * 0.5f - 18f, RowHeight), "Rematch")) Rematch();
            if (GUI.Button(new Rect(panel.center.x + 6f, panel.yMax - 44f, panel.width * 0.5f - 18f, RowHeight), "Menu")) ToMenu();
        }
    }
}
