using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Presentation;

namespace Godsbound.EditorTools
{
    /// <summary>
    /// Authors the board scene programmatically.
    /// </summary>
    /// <remarks>
    /// The scene is generated rather than hand-built so it can be recreated exactly, and so
    /// the camera framing is derived from <see cref="BoardWorld.BoardBounds"/> rather than
    /// eyeballed — if the board's dimensions change, regenerating reframes it correctly.
    /// </remarks>
    public static class CreateBoardScene
    {
        private const string SceneDir = "Assets/Godsbound/Scenes";
        private const string ScenePath = SceneDir + "/Board.unity";

        [MenuItem("Godsbound/Create Board Scene", false, 1)]
        public static void Create()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog(
                    "Overwrite board scene?",
                    $"{ScenePath} already exists. Replace it?",
                    "Replace", "Cancel"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // --- board -------------------------------------------------------------
            var boardGo = new GameObject("Board");
            boardGo.AddComponent<BoardView>();

            // Buildings sit on their own object so they can be toggled independently of the
            // terrain while the art step is still outstanding.
            var buildingsGo = new GameObject("Buildings");
            buildingsGo.AddComponent<BuildingsView>();

            // --- light ---------------------------------------------------------------
            // An empty scene has NO light. The board uses an unlit shader so it does not
            // strictly need one, but if the shader ever falls back to a lit variant an
            // unlit scene renders the whole board black in the Game view while the Scene
            // view looks fine (it has its own headlight). A directional light costs
            // nothing and removes that failure mode entirely.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // --- camera, framed on the board ---------------------------------------
            var bounds = BoardWorld.BoardBounds(BoardWorld.UnitLayout());
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.043f, 0.051f, 0.075f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            // Frame the board into the screen MINUS the UI bands, never the whole screen.
            // The browser game's board sits in #boardwrap, which is only the leftover after
            // #topbar and #bottombar; matching that here is what keeps the unit cards and
            // god buttons off the player's own deployment rows.
            var framing = BoardViewport.Frame(
                bounds,
                BoardViewport.DesignAspect,
                BoardViewport.DefaultTopFraction,
                BoardViewport.DefaultBottomFraction);

            cam.orthographicSize = framing.OrthographicSize;
            camGo.transform.position = new Vector3(framing.Center.x, framing.Center.y, -10f);
            camGo.transform.rotation = Quaternion.identity;

            // Placeholder blocks showing the reserved bands. A scaffold for judging fit —
            // delete the component once the real UI exists.
            camGo.AddComponent<UiSafeAreaPreview>();

            boardGo.AddComponent<MatchController>();

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[Godsbound] Board scene created at {ScenePath} — " +
                      $"{Board.Cols}x{Board.Rows} = {Board.CellCount} cells, " +
                      $"bounds {bounds.size.x:0.##} x {bounds.size.y:0.##} world units, " +
                      $"ortho size {cam.orthographicSize:0.##}, " +
                      $"UI reserved {BoardViewport.DefaultTopFraction:P1} top / " +
                      $"{BoardViewport.DefaultBottomFraction:P1} bottom, " +
                      $"board band {framing.SafeArea.size.x:0.##} x {framing.SafeArea.size.y:0.##}, " +
                      $"shader '{BoardView.ResolvedShaderName ?? "(not yet resolved)"}'.");

            Selection.activeGameObject = boardGo;
        }
    }
}
