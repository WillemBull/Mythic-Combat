using System.Collections.Generic;
using UnityEngine;
using Godsbound.Core;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Renders the static board: one hex per cell, coloured by its terrain, built entirely
    /// from <see cref="Board"/>, <see cref="TerrainMap"/> and <see cref="HexLayout"/>.
    /// </summary>
    /// <remarks>
    /// <para>The board is DATA-DRIVEN — there is no hand-placed geometry and no hardcoded
    /// 117. Changing <see cref="Board.Rows"/> or the terrain map changes what is drawn.</para>
    /// <para>Runs with <c>[ExecuteAlways]</c> so the board appears in the Scene view without
    /// entering Play Mode, which is what makes the board fit reviewable at a glance.</para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("Godsbound/Board View")]
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("World units per hex circumradius.")]
        [SerializeField] private float unitsPerHex = 1f;

        [Tooltip("Shrinks each hex slightly so the grid reads as separate cells.")]
        [Range(0.80f, 1f)]
        [SerializeField] private float cellFill = 0.97f;

        [Header("Debug")]
        [Tooltip("Tint no-man's land, so the neutral band is visible while building the board.")]
        [SerializeField] private bool shadeNeutralRow = true;

        private readonly List<GameObject> _cells = new List<GameObject>();
        private Mesh _mesh;
        private Material _material;
        private TerrainMap _terrain;

        /// <summary>The live terrain being rendered. Rebuild after mutating it.</summary>
        public TerrainMap Terrain => _terrain ?? (_terrain = new TerrainMap());

        /// <summary>The layout the board is drawn with.</summary>
        public HexLayout Layout => BoardWorld.UnitLayout();

        /// <summary>Number of cell objects currently built.</summary>
        public int CellCount => _cells.Count;

        public void Bind(TerrainMap terrain)
        {
            _terrain = terrain ?? throw new System.ArgumentNullException(nameof(terrain));
            Rebuild();
        }

        private void OnEnable() => Rebuild();

        private void OnDisable() => ClearCells();

        /// <summary>Tear the board down and build it again from current data.</summary>
        private int _terrainVersion = -1;

        /// <summary>Rebuild only when god-power overlays (or painting) changed the live terrain.</summary>
        public void SyncTerrain()
        {
            if (_terrain != null && _terrain.Version != _terrainVersion) Rebuild();
        }

        public void Rebuild()
        {
            ClearCells();

            if (_mesh == null) _mesh = HexMesh.Create(1f);
            if (_material == null) _material = CreateMaterial();

            var layout = Layout;
            var terrain = Terrain;
            _terrainVersion = terrain.Version;
            var block = new MaterialPropertyBlock();

            foreach (var h in Board.AllCells())
            {
                var go = new GameObject($"Hex_{h.C}_{h.R}")
                {
                    hideFlags = HideFlags.DontSave
                };
                go.transform.SetParent(transform, false);
                go.transform.localPosition = BoardWorld.CenterOf(h, layout, unitsPerHex);
                // Ground-plane geometry: flattened to the viewing angle. Unit sprites
                // will use BoardWorld.UprightScale instead and stay undistorted.
                go.transform.localScale = BoardWorld.GroundScale(unitsPerHex * cellFill);

                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                var colour = TerrainPalette.For(terrain[h], h.C, h.R);
                if (shadeNeutralRow && Board.IsNeutralRow(h.R))
                    colour = Color.Lerp(colour, new Color(0.024f, 0.031f, 0.055f), 0.30f);

                block.Clear();
                block.SetColor(BaseColorId, colour);
                block.SetColor(LegacyColorId, colour);
                mr.SetPropertyBlock(block);

                _cells.Add(go);
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// An unlit material for the board. URP's Unlit shader is preferred; the fallbacks
        /// keep the board visible if the render pipeline differs, rather than filling the
        /// scene with magenta error material.
        /// </summary>
        private static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Lit");

            if (shader == null)
                throw new MissingReferenceException("No usable shader found for the board.");

            ResolvedShaderName = shader.name;
            return new Material(shader) { name = "GodsboundBoardCell", hideFlags = HideFlags.DontSave };
        }

        /// <summary>
        /// Which shader the board actually resolved to. Worth surfacing: an UNLIT board in
        /// an unlit scene renders correctly, but if this falls through to a Lit shader and
        /// the scene has no light, every cell renders black in the Game view while the
        /// Scene view still looks right — because the Scene view supplies its own headlight.
        /// That cost real time during U2.
        /// </summary>
        public static string ResolvedShaderName { get; private set; }

        private void ClearCells()
        {
            foreach (var go in _cells)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            _cells.Clear();

            // Catch anything a domain reload or an interrupted rebuild orphaned.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (!child.name.StartsWith("Hex_")) continue;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            UnityEditor_DelayedRebuild();
        }

        private void UnityEditor_DelayedRebuild()
        {
#if UNITY_EDITOR
            // OnValidate runs during serialization, where creating and destroying objects
            // is illegal. Defer to the next editor tick.
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
