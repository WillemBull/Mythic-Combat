using System;
using System.Collections.Generic;
using UnityEngine;

namespace Godsbound.Presentation
{
    [Serializable] public sealed class BuildingArtEntry
    { public string key, faction, type, source, resource; public float scale; }
    [Serializable] public sealed class BuildingPresentationData
    {
        public string source;
        public float canvas, anchorX, anchorY, healthGap, healthWidth, labelInset, labelHeight;
        public BuildingArtEntry[] art;
        public static BuildingPresentationData Load() => JsonUtility.FromJson<BuildingPresentationData>(
            Resources.Load<TextAsset>("GameData/building_presentation").text);
    }

    /// <summary>Shared front artwork for either side; the footprint column controls mirroring.</summary>
    public sealed class BuildingArtCatalog : IDisposable
    {
        public BuildingPresentationData Data { get; }
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public BuildingArtCatalog(BuildingPresentationData data) { Data = data; }
        public BuildingArtEntry Entry(string faction, string type) =>
            Array.Find(Data.art, e => e.faction == faction && e.type == type);
        public Sprite Resolve(string faction, string type)
        {
            var e = Entry(faction,type);
            if(e == null) return null;
            if(sprites.TryGetValue(e.resource,out var sprite)) return sprite;
            var texture = Resources.Load<Texture2D>(e.resource);
            sprite = texture == null ? null : Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),
                new Vector2(0.5f,0.5f),texture.height);
            sprites[e.resource] = sprite; return sprite;
        }
        public void Dispose()
        { foreach(var sprite in sprites.Values) UnitArtCatalog.Release(sprite); sprites.Clear(); }
    }
}
