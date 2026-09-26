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
    /// <summary>One god's face, from the browser's GOD_PORTRAITS table (U41).</summary>
    [Serializable] public sealed class GodArtEntry { public string key, resource; }
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
        /// <summary>The 42 god portraits. A god missing from here draws as text, never as a hole.</summary>
        public GodArtEntry[] godArt;
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
        /// <summary>
        /// A god's portrait, or null when the table does not have one. Cached per resource like the
        /// unit art, because a tile asks for its face on every OnGUI event.
        /// </summary>
        public Texture2D Portrait(string godKey)
        {
            var entry = Array.Find(Data.godArt ?? Array.Empty<GodArtEntry>(), g => g.key == godKey);
            if (entry == null) return null;
            if (portraits.TryGetValue(entry.resource, out var cached)) return cached;
            return portraits[entry.resource] = Resources.Load<Texture2D>(entry.resource);
        }
        private readonly Dictionary<string, Texture2D> portraits = new Dictionary<string, Texture2D>();

        public void Dispose()
        {
            foreach (var sprite in sprites.Values) Release(sprite);
            sprites.Clear();
            // The textures themselves are Resources-owned; only the lookup is ours to drop.
            portraits.Clear();
        }
        internal static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
