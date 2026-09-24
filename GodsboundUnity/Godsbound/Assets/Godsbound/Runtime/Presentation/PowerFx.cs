using System.Collections.Generic;
using Godsbound.Core;
using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Short cast effects: an expanding, fading ring on each affected hex. Presentation only — reads
    /// nothing from Core but the hexes it is handed, and clears on reset. Stands in for the browser's
    /// <c>spawnPowerFx</c>/<c>drawPowerFx</c> until real power art is imported.
    /// </summary>
    public sealed class PowerFx : MonoBehaviour
    {
        public const float Lifetime = 0.6f;
        private sealed class Burst { public GameObject Visual; public float Age; }
        private readonly List<Burst> bursts = new List<Burst>();
        private Sprite ring;
        private Texture2D ringTexture;

        public int ActiveCount => bursts.Count;

        public void Spawn(IEnumerable<Hex> hexes, Color color)
        {
            EnsureSprite();
            foreach (var h in hexes)
            {
                var go = new GameObject("PowerFx");
                go.transform.SetParent(transform, false);
                var p = BoardWorld.CenterOf(h, BoardWorld.UnitLayout());
                go.transform.position = new Vector3(p.x, p.y, -0.3f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ring; sr.color = color; sr.sortingOrder = 20000;
                go.transform.localScale = BoardWorld.GroundScale(0.4f);
                bursts.Add(new Burst { Visual = go });
            }
        }

        /// <summary>Advance and retire bursts. Called once per frame by the controller.</summary>
        public void Tick(float dt)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i];
                b.Age += dt;
                if (b.Age >= Lifetime) { Discard(b.Visual); bursts.RemoveAt(i); continue; }
                float t = b.Age / Lifetime;
                b.Visual.transform.localScale = BoardWorld.GroundScale(0.4f + 0.8f * t);
                var sr = b.Visual.GetComponent<SpriteRenderer>();
                var c = sr.color; c.a = 1f - t; sr.color = c;
            }
        }

        public void Clear()
        {
            foreach (var b in bursts) if (b.Visual != null) Discard(b.Visual);
            bursts.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            if (ringTexture != null) Discard(ringTexture);
        }

        private static void Discard(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        private void EnsureSprite()
        {
            if (ring != null) return;
            const int n = 64;
            ringTexture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) * 8f);
                    ringTexture.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            ringTexture.Apply();
            ring = Sprite.Create(ringTexture, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 2f);
        }
    }
}
