using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Godsbound.Core;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;
using Godsbound.Core.Setup;
using Godsbound.Data;

namespace Godsbound.Presentation.Setup
{
    /// <summary>
    /// The setup scenes' one owner: the session the player is editing, the two saved slots, and the
    /// board preview the terrain step draws on.
    /// </summary>
    /// <remarks>
    /// <para>Every control writes through <see cref="SetupSession"/>, never into scene state — so the
    /// deck that starts a match is the one the model agreed to, and the whole flow is testable
    /// without loading a scene.</para>
    /// <para>The preview is a real <see cref="TerrainMap"/> with the player's half composed into its
    /// own rows, so the terrain step uses the same hex picking, the same layout and the same
    /// <see cref="BoardView"/> the match does. The AI's half stays plain here: its terrain is rolled
    /// when the match starts and is not the player's to see.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SetupController : MonoBehaviour
    {
        [Tooltip("Where this scene opens: the menu's home hub, or straight into deck editing.")]
        [SerializeField] private SetupStep startStep = SetupStep.Home;

        [Tooltip("Scene loaded when a deck starts a match.")]
        [SerializeField] private string boardScene = "Board";

        [Tooltip("Scene loaded when the player edits a deck from the menu.")]
        [SerializeField] private string setupScene = "Setup";

        public SetupSession Session { get; private set; }
        public DeckStore Store { get; private set; }
        public TerrainMap Preview { get; private set; }
        public string LastProblem { get; private set; }

        /// <summary>The scene this controller asked for, for a test that must not load one.</summary>
        public string RequestedScene { get; private set; }

        public event Action Changed;

        private BoardView board;
        private GameDatabase db;

        private void Awake() { Initialize(); }

        public void Initialize(GameDatabase database = null, string storePath = null, BoardView boardView = null)
        {
            if (Session != null) return;
            db = database ?? GameDataLoader.Load();
            Store = new DeckStore(db, storePath);
            Store.Load();
            Session = new SetupSession(db, Store.Active(useDefault: true), startStep);
            Session.Changed += OnSessionChanged;
            Preview = new TerrainMap();
            board = boardView != null ? boardView : FindAnyObjectByType<BoardView>();
            SyncPreview();
        }

        private void OnSessionChanged()
        {
            SyncPreview();
            Changed?.Invoke();
        }

        /// <summary>Draw the half the player is editing into the preview board.</summary>
        public void SyncPreview()
        {
            if (Session == null || Preview == null) return;
            var plain = new string[Board.AiRows];
            for (int r = 0; r < Board.AiRows; r++) plain[r] = new string('P', Board.Cols);
            BoardSetup.Compose(Preview, Session.Half, plain);
            if (board != null) board.Bind(Preview);
        }

        /// <summary>Save what is being edited into a slot. A deck that is not legal is refused.</summary>
        public bool Save(int slot)
        {
            if (!Session.CanStart(out var preset, out var problem)) { LastProblem = problem; return false; }
            if (!Store.Store(slot, preset, out problem)) { LastProblem = problem; return false; }
            Store.Save();
            LastProblem = null;
            return true;
        }

        /// <summary>Open a slot for editing — an empty one starts from the faction default.</summary>
        public void Edit(int slot)
        {
            Store.SelectSlot(slot);
            Session.Slot = slot;
            // An empty slot opens on the faction's shipped deck, exactly as the browser's
            // applyDeckPreset(slot, useDefault) does, rather than on a blank board.
            Session.Load(Store.Slot(slot) ?? DeckRules.Default(db, Session.Faction));
            Session.Go(SetupStep.Faction);
        }

        /// <summary>Start a match with a slot's deck. Saves first, so the arena you built is the one you play.</summary>
        public bool Play(int slot)
        {
            if (!Save(slot)) return false;
            Store.SelectSlot(slot);
            Load(boardScene);
            return true;
        }

        /// <summary>Leave the menu for the editing scene.</summary>
        public void OpenSetupScene() => Load(setupScene);

        private void Load(string scene)
        {
            RequestedScene = scene;
            // EditMode tests drive this controller directly; loading a scene there would tear the
            // test fixture down mid-assertion, so the request is recorded and only acted on in play.
            if (Application.isPlaying && Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
        }
    }
}
