using System.Collections.Generic;
using Godsbound.Core;
using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>
    /// The arena each half is fought over: one painting per faction, cover-fitted across the board
    /// and cross-faded through no-man's land (U43, from the browser's <c>renderBG</c>).
    /// </summary>
    /// <remarks>
    /// <para>Sprites rather than meshes, because alpha is the whole point here and a SpriteRenderer
    /// blends without any shader work. They sit behind the board in Z and draw in the transparent
    /// queue, which is only visible at all because U43 turned the hexes into outlines.</para>
    /// <para>The fade is stepped into <see cref="ArenaBackdrop.FadeStrips"/> bands rather than being
    /// a true gradient. Every band samples the SAME cover fit — the browser warns about this in its
    /// own comment: fitting the image to each band's little box instead would tear the scene apart.</para>
    /// </remarks>
    [AddComponentMenu("Godsbound/Arena Backdrop")]
    public sealed class ArenaBackdropView : MonoBehaviour
    {
        [Tooltip("World units per hex circumradius. Match the Board View.")]
        [SerializeField] private float unitsPerHex = 1f;

        private readonly List<GameObject> parts = new List<GameObject>();
        private UnitArtCatalog art;
        private Texture2D flat;
        private string aiFaction = "china", playerFaction = "egypt";

        /// <summary>How many sprites the backdrop is currently made of. For tests.</summary>
        public int PartCount => parts.Count;

        /// <summary>Set the two halves' pantheons and rebuild.</summary>
        public void Bind(string player, string ai)
        {
            playerFaction = player ?? "egypt";
            aiFaction = ai ?? "china";
            Rebuild();
        }

        private void OnEnable() => Rebuild();
        private void OnDisable() => Clear();
        private void OnDestroy() { Clear(); art?.Dispose(); art = null; if (flat != null) UnitArtCatalog.Release(flat); }

        public void Rebuild()
        {
            Clear();
            if (!isActiveAndEnabled) return;
            if (art == null) art = new UnitArtCatalog(PresentationData.Load());
            if (flat == null)
            {
                flat = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
                flat.SetPixel(0, 0, Color.white); flat.Apply();
            }

            var layout = BoardWorld.UnitLayout();
            var bounds = BoardWorld.BoardBounds(layout, unitsPerHex);
            // A full hex of overhang, as the browser bleeds its art past the outermost hexes rather
            // than stopping level with them and leaving bare board colour at the edges.
            float pad = unitsPerHex;
            float left = bounds.min.x - pad, right = bounds.max.x + pad;
            float bottom = bounds.min.y - pad, top = bounds.max.y + pad;

            bool banded = ArenaBackdrop.NeutralBand(layout, out float bandTopBoard, out float bandBottomBoard);
            // Board space runs downward, world space upward: row 0 is the AI's, at the TOP of the
            // screen, which is the LARGEST world y. Flip the band's edges as they cross over.
            float bandNearWorld = BoardWorld.ToWorld(new BoardPoint(0f, bandBottomBoard), unitsPerHex).y;
            float bandFarWorld = BoardWorld.ToWorld(new BoardPoint(0f, bandTopBoard), unitsPerHex).y;
            if (!banded) { bandNearWorld = bandFarWorld = (top + bottom) * 0.5f; }

            // The AI's scene covers everything down to the near edge of the band; the player's is
            // cover-fitted to its OWN footprint so the faded part and the solid part below are one
            // continuous image rather than two differently-scaled copies.
            Add(aiFaction, left, bandNearWorld, right - left, top - bandNearWorld,
                left, bandNearWorld, right - left, top - bandNearWorld, 1f, -20);
            Add(playerFaction, left, bottom, right - left, bandNearWorld - bottom,
                left, bottom, right - left, bandFarWorld - bottom, 1f, -19);
            if (!banded) return;

            float stripHeight = (bandFarWorld - bandNearWorld) / ArenaBackdrop.FadeStrips;
            for (int i = 0; i < ArenaBackdrop.FadeStrips; i++)
            {
                float y = bandNearWorld + stripHeight * i;
                float alpha = ArenaBackdrop.FadeAt(y + stripHeight * 0.5f, bandFarWorld, bandNearWorld);
                Add(playerFaction, left, y, right - left, stripHeight,
                    left, bottom, right - left, bandFarWorld - bottom, alpha, -18);
            }
        }

        /// <summary>
        /// One band of a faction's painting: <paramref name="fitX"/>..<paramref name="fitHeight"/> is
        /// the footprint the image is cover-fitted to, and x..height is the slice actually shown.
        /// </summary>
        private void Add(string faction, float x, float y, float width, float height,
                         float fitX, float fitY, float fitWidth, float fitHeight, float alpha, int order)
        {
            if (width <= 0f || height <= 0f) return;
            var texture = art.Screen("arena_" + faction);
            var go = new GameObject("Arena_" + faction + "_" + order) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, alpha);

            if (texture == null)
            {
                // Greece has no arena painting and never has: a flat colour, exactly as the browser
                // falls back rather than flashing black while art decodes.
                renderer.sprite = Sprite.Create(flat, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                renderer.color = ArenaBackdrop.Fallback(faction) * new Color(1f, 1f, 1f, alpha);
                go.transform.localPosition = new Vector3(x + width * 0.5f, y + height * 0.5f, 0.6f);
                go.transform.localScale = new Vector3(width, height, 1f);
                parts.Add(go);
                return;
            }

            var fit = ArenaBackdrop.Cover(fitX, fitY, fitWidth, fitHeight, (float)texture.width / texture.height);
            // The slice of the image that lands in this band, in texture pixels.
            float u0 = (x - fit.X) / fit.Width, u1 = (x + width - fit.X) / fit.Width;
            float v0 = (y - fit.Y) / fit.Height, v1 = (y + height - fit.Y) / fit.Height;
            var rect = new Rect(u0 * texture.width, v0 * texture.height,
                                (u1 - u0) * texture.width, (v1 - v0) * texture.height);
            rect = Clamp(rect, texture.width, texture.height);
            if (rect.width < 1f || rect.height < 1f) { UnitArtCatalog.Release(go); return; }

            renderer.sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 1f);
            go.transform.localPosition = new Vector3(x + width * 0.5f, y + height * 0.5f, 0.6f);
            go.transform.localScale = new Vector3(width / rect.width, height / rect.height, 1f);
            parts.Add(go);
        }

        /// <summary>A sprite rect must sit inside its texture, or Sprite.Create throws.</summary>
        private static Rect Clamp(Rect r, int width, int height)
        {
            float x0 = Mathf.Clamp(r.xMin, 0f, width), x1 = Mathf.Clamp(r.xMax, 0f, width);
            float y0 = Mathf.Clamp(r.yMin, 0f, height), y1 = Mathf.Clamp(r.yMax, 0f, height);
            return new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
        }

        private void Clear()
        {
            foreach (var go in parts)
            {
                if (go == null) continue;
                var sprite = go.GetComponent<SpriteRenderer>()?.sprite;
                UnitArtCatalog.Release(go);
                if (sprite != null) UnitArtCatalog.Release(sprite);   // Sprite.Create makes one per band
            }
            parts.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (child.name.StartsWith("Arena_")) UnitArtCatalog.Release(child);
            }
        }
    }
}
