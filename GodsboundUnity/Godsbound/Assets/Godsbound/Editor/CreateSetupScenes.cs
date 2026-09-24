using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Setup;
using Godsbound.Presentation;
using Godsbound.Presentation.Setup;

namespace Godsbound.EditorTools
{
    /// <summary>
    /// Authors the menu and setup scenes, siblings of <see cref="CreateBoardScene"/>.
    /// </summary>
    /// <remarks>
    /// Both scenes are the same three objects — a board view showing the player's own half, a camera
    /// framed exactly as the battle camera is, and the controller with its HUD — and differ only in
    /// which step they open on. That is deliberate: the browser has ONE menu screen with steps inside
    /// it, so two scenes that drift apart would be two places to fix every layout change.
    /// </remarks>
    public static class CreateSetupScenes
    {
        private const string SceneDir = "Assets/Godsbound/Scenes";
        public const string MenuPath = SceneDir + "/Menu.unity";
        public const string SetupPath = SceneDir + "/Setup.unity";
        public const string BoardPath = SceneDir + "/Board.unity";

        [MenuItem("Godsbound/Create Menu Scene", false, 2)]
        public static void CreateMenu() => Create(MenuPath, SetupStep.Home);

        [MenuItem("Godsbound/Create Setup Scene", false, 3)]
        public static void CreateSetup() => Create(SetupPath, SetupStep.Faction);

        private static void Create(string path, SetupStep step)
        {
            if (File.Exists(path) &&
                !EditorUtility.DisplayDialog("Overwrite scene?", $"{path} already exists. Replace it?",
                                             "Replace", "Cancel"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var boardGo = new GameObject("Board");
            var board = boardGo.AddComponent<BoardView>();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // The same framing as the board scene, so a half looks the size it will play at.
            var bounds = BoardWorld.BoardBounds(BoardWorld.UnitLayout());
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.043f, 0.051f, 0.075f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            var framing = BoardViewport.Frame(bounds, BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
            cam.orthographicSize = framing.OrthographicSize;
            camGo.transform.position = new Vector3(framing.Center.x, framing.Center.y, -10f);

            var setupGo = new GameObject("Setup");
            var controller = setupGo.AddComponent<SetupController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("startStep").enumValueIndex = (int)step;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            setupGo.AddComponent<SetupHud>();

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, path);
            Register(MenuPath, SetupPath, BoardPath);
            AssetDatabase.Refresh();

            Debug.Log($"[Godsbound] {path} created, opening on {step}; " +
                      $"board {Board.Cols}x{Board.Rows}, ortho size {cam.orthographicSize:0.##}. " +
                      $"Build settings now list {EditorBuildSettings.scenes.Length} scenes.");
            Selection.activeGameObject = setupGo;
        }

        /// <summary>
        /// Put the three scenes in Build Settings, menu first. A scene that is not listed cannot be
        /// loaded at runtime, so the flow would dead-end the first time a player pressed Battle.
        /// </summary>
        private static void Register(params string[] paths)
        {
            var listed = EditorBuildSettings.scenes.ToList();
            foreach (var path in paths)
            {
                if (!File.Exists(path) || listed.Any(s => s.path == path)) continue;
                listed.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = listed.ToArray();
        }
    }
}
