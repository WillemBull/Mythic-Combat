using System;
using System.Collections.Generic;
using UnityEngine;

namespace Godsbound.Presentation
{
    [Serializable] public sealed class UnitArtEntry
    {
        public string key, source, resource;
        public int side;
        public float height;
        public bool mirror;
    }
    [Serializable] public sealed class PowerHint { public string key, text; }
    [Serializable] public sealed class PresentationData
    {
        /// <summary>The browser's POWER_HINTS: one line per power, shown while it is armed.</summary>
        public PowerHint[] powerHints;
        public string HintFor(string key) => Array.Find(powerHints ?? Array.Empty<PowerHint>(), h => h.key == key)?.text ?? "";
        public string source;
        public float food, favor, aiFood, aiFavor, anchorDown;
        /// <summary>Cards shown at once; the rest of the deck waits in rotation (browser HAND_SIZE).</summary>
        public int handSize;
        public UnitArtEntry[] art;
        public static PresentationData Load() => JsonUtility.FromJson<PresentationData>(
            Resources.Load<TextAsset>("GameData/presentation").text);
    }
    // Each view owns its generated Sprite objects; imported textures remain Unity-owned.
    public sealed class UnitArtCatalog : IDisposable
    {
        public PresentationData Data { get; }
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public UnitArtCatalog(PresentationData data) { Data = data; }
        public UnitArtEntry Entry(string key, int side) => Array.Find(Data.art, e => e.key == key && e.side == side);
        public Sprite Resolve(string key, int side)
        {
            var e = Entry(key, side);
            if (e == null) return null;
            if (sprites.TryGetValue(e.resource, out var sprite)) return sprite;
            var texture = Resources.Load<Texture2D>(e.resource);
            sprite = texture == null ? null : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0f), texture.height);
            sprites[e.resource] = sprite;
            return sprite;
        }
        public void Dispose()
        {
            foreach (var sprite in sprites.Values) Release(sprite);
            sprites.Clear();
        }
        internal static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
