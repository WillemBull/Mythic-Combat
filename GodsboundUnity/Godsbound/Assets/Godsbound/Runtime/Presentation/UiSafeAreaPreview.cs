using System.Collections.Generic;
using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Draws the reserved UI bands as flat placeholder blocks so the room set aside for the
    /// unit cards and god buttons is visible before any real UI exists.
    /// </summary>
    /// <remarks>
    /// A scaffold, not the UI. The bottom band is subdivided into the god-button row and
    /// the card row at the same proportions the browser game's <c>#bottombar</c> uses, so
    /// what you see is what those controls will occupy. Delete this component once the real
    /// UI lands; nothing depends on it.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Godsbound/UI Safe Area Preview")]
    public sealed class UiSafeAreaPreview : MonoBehaviour
    {
        [Tooltip("Show the placeholder bands. Turn off to see the bare board.")]
        [SerializeField] private bool show = true;

        /* Initialised from BoardViewport's constants, NOT from a sentinel. A -1 "use the
           default" sentinel does not survive [Range], which clamps the serialized value to
           0 — so both fractions read as zero, no bands were built, and the preview silently
           did nothing. Both consts, so this is a compile-time constant expression. */
        [Tooltip("Share of the screen height reserved for the top bar.")]
        [Range(0f, 0.4f)]
        [SerializeField]
        private float topFraction = BoardViewport.TopUiPx / BoardViewport.DesignHeightPx;

        [Tooltip("Share of the screen height reserved for the cards and god buttons.")]
        [Range(0f, 0.5f)]
        [SerializeField]
        private float bottomFraction = BoardViewport.BottomUiPx / BoardViewport.DesignHeightPx;

        /// <summary>
        /// Of the bottom band, the share taken by the god-button row. The rest is the card
        /// row. Derived from the browser game's bottom bar: roughly 38px of god row against
        /// 90px of card row.
        /// </summary>
        [Range(0.1f, 0.6f)] [SerializeField] private float godRowShareOfBottom = 0.30f;

        private readonly List<GameObject> _blocks = new List<GameObject>();
        private Mesh _quad;
        private Material _material;

        private void OnEnable() => Rebuild();
        private void OnDisable() => Clear();

        public float TopFraction => topFraction;
        public float BottomFraction => bottomFraction;

        public void Rebuild()
        {
            Clear();
            if (!show) return;

            var cam = GetComponent<Camera>();
            if (cam == null || !cam.orthographic) return;

            if (_quad == null) _quad = QuadMesh.Create();
            if (_material == null) _material = CreateMaterial();

            float size = cam.orthographicSize;
            float halfW = size * cam.aspect;
            float top = transform.position.y + size;
            float bottom = transform.position.y - size;
            float x = transform.position.x;
            float width = halfW * 2f;

            float topH = 2f * size * TopFraction;
            float bottomH = 2f * size * BottomFraction;

            // Top bar: names, timer, enemy gods, resource bars.
            if (topH > 0f)
                AddBlock("UiPreview_TopBar", x, top - topH * 0.5f, width, topH,
                         TopBarColour);

            // Bottom bar, split the way #bottombar splits.
            if (bottomH > 0f)
            {
                float godH = bottomH * godRowShareOfBottom;
                float cardH = bottomH - godH;

                AddBlock("UiPreview_GodButtons", x, bottom + cardH + godH * 0.5f, width, godH,
                         GodRowColour);
                AddBlock("UiPreview_UnitCards", x, bottom + cardH * 0.5f, width, cardH,
                         CardRowColour);
            }
        }

        /* Deliberately LIGHTER than the camera's near-black clear colour. The first pass
           used the browser game's own bar colours, which are almost exactly the background
           and left the bands invisible -- defeating the only purpose this component has.
           These are scaffold colours for judging fit, not the shipping palette. */
        private static readonly Color TopBarColour = new Color(0.16f, 0.17f, 0.23f, 1f);
        private static readonly Color GodRowColour = new Color(0.31f, 0.25f, 0.44f, 1f);
        private static readonly Color CardRowColour = new Color(0.20f, 0.24f, 0.34f, 1f);

        private void AddBlock(string name, float cx, float cy, float w, float h, Color colour)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            // In front of the board (which sits at z=0) as seen from this camera.
            go.transform.position = new Vector3(cx, cy, transform.position.z + 1f);
            go.transform.localScale = new Vector3(w, h, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var block = new MaterialPropertyBlock();
            block.SetColor(Shader.PropertyToID("_BaseColor"), colour);
            block.SetColor(Shader.PropertyToID("_Color"), colour);
            mr.SetPropertyBlock(block);

            _blocks.Add(go);
        }

        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            return new Material(shader) { name = "GodsboundUiPreview", hideFlags = HideFlags.DontSave };
        }

        private void Clear()
        {
            foreach (var go in _blocks)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _blocks.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (!child.name.StartsWith("UiPreview_")) continue;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Rebuild();
            };
#else
            Rebuild();
#endif
        }
    }
}
