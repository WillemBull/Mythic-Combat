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
        private Mesh _mesh, _ringDark, _ringLight;
        private Material _material, _overlay;
        private TerrainMap _terrain;
        private UnitArtCatalog _art;

        /// <summary>
        /// The grid's two strokes, from the browser: a dark base pass 2.4 wide, then a light line
        /// 1.1 wide on top, both against a board hex of 24px. Expressed as fractions of the hex so
        /// they hold at any zoom. The dark pass is what keeps the grid legible over bright art.
        /// </summary>
        public const float DarkStroke = 2.4f / 24f, LightStroke = 1.1f / 24f;
        public static readonly Color GridDark = new Color(0f, 0f, 0f, 0.32f);
        public static readonly Color GridLight = new Color(255f / 255f, 244f / 255f, 214f / 255f, 0.65f);
        /// <summary>The browser's NEUTRAL_ROW_SHADE, drawn UNDER the strokes so they stay crisp.</summary>
        public static readonly Color NeutralShade = new Color(0.02f, 0.03f, 0.06f, 0.38f);

        /// <summary>
        /// Which painting a hex wears, or null for the four codes the browser draws nothing for —
        /// Plains, Desert, Road and High Ground are the arena photograph itself.
        /// Forest is bamboo on China's side of the board, as <c>drawSpecialTile</c> resolves it.
        /// </summary>
        public static string TileArt(TerrainType terrain, int row, string factionOfRow) =>
            terrain == TerrainType.Forest ? (factionOfRow == "china" ? "bamboo" : "forest")
            : terrain == TerrainType.Mountain ? "mountain"
            : terrain == TerrainType.Water ? "water"
            : null;

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
            if (_ringDark == null) _ringDark = HexRingMesh.Create(1f, DarkStroke);
            if (_ringLight == null) _ringLight = HexRingMesh.Create(1f, LightStroke);
            if (_material == null) _material = CreateMaterial();
            if (_overlay == null) _overlay = CreateOverlayMaterial();
            if (_art == null) _art = new UnitArtCatalog(PresentationData.Load());

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

                // U43: a hex is no longer a coloured plate. No-man's land keeps a shade — under the
                // strokes, darkening whatever the arena left there rather than painting its own
                // colour — and every hex gets the browser's two grid strokes. The terrain itself is
                // the arena painting behind, plus a tile image for the three codes that have one.
                if (shadeNeutralRow && Board.IsNeutralRow(h.R))
                    Part(go, _mesh, NeutralShade, block, 0f);
                Part(go, _ringDark, GridDark, block, -0.01f);
                Part(go, _ringLight, GridLight, block, -0.02f);

                var tile = TileArt(terrain[h], h.R, FactionOfRow(h.R));
                if (tile != null) Tile(go, tile);

                _cells.Add(go);
            }
        }

        /// <summary>Which pantheon owns a row, for Forest's bamboo/forest split.</summary>
        public System.Func<int, string> FactionOfRow { private get; set; } =
            row => row < Board.PlayerRow0 ? "china" : "egypt";

        /// <summary>One coloured layer of a cell: shade, dark stroke or light stroke.</summary>
        private void Part(GameObject cell, Mesh mesh, Color colour, MaterialPropertyBlock block, float z)
        {
            var go = new GameObject(mesh.name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(cell.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _overlay;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            block.Clear();
            block.SetColor(BaseColorId, colour);
            block.SetColor(LegacyColorId, colour);
            mr.SetPropertyBlock(block);
            // Sprites/Default is in the transparent queue, which is sorted by distance rather than
            // by draw order, so the three layers are separated in Z by Part's caller.
            mr.sortingOrder = 1;
        }

        /// <summary>
        /// A special tile's painting. Every terrain asset is a hexagon drawn inscribed in its canvas
        /// touching the left and right edges, so it is sized by WIDTH — the hex's point-to-point
        /// 2*radius — and its height follows the source aspect. Bamboo overflows the top of its hex
        /// on purpose: its foliage is painted above the plate, and Willem asked for exactly that.
        /// </summary>
        private void Tile(GameObject cell, string key)
        {
            var texture = _art.Tile(key);
            if (texture == null) return;
            var go = new GameObject("Tile_" + key) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(cell.transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                                            new Vector2(0.5f, 0.5f), texture.width * 0.5f);
            renderer.sortingOrder = -10;
            // The cell is already squashed to the ground plane, so the sprite inherits the tilt.
            go.transform.localPosition = new Vector3(0f, 0f, -0.005f);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        /// <summary>
        /// A BLENDING material for the grid strokes and the neutral shade.
        /// </summary>
        /// <remarks>
        /// The board's own material is URP Unlit in its opaque mode, which throws the alpha away —
        /// so the browser's translucent strokes (.32 and .65) came out solid, and no-man's land's
        /// 0.38 shade painted the middle of the board a flat black stripe. Sprites/Default blends,
        /// takes a plain `_Color`, and needs no surface-mode plumbing to get there.
        /// </remarks>
        private static Material CreateOverlayMaterial()
        {
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color");
            if (shader == null)
                throw new MissingReferenceException("No usable shader found for the board grid.");
            return new Material(shader) { name = "GodsboundBoardGrid", hideFlags = HideFlags.DontSave };
        }

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
