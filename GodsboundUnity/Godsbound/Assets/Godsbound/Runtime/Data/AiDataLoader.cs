using UnityEngine;
using Godsbound.Core.AI;

namespace Godsbound.Data
{
    /// <summary>Loads the exported AI profiles and rules. Same contract as <see cref="GameDataLoader"/>:
    /// cached after the first load, and a missing export throws at the load rather than as a null
    /// reference somewhere inside the AI's first tick.</summary>
    public static class AiDataLoader
    {
        public const string ResourcePath = "GameData/ai";

        private static AiData _cached;

        public static AiData Load()
        {
            if (_cached != null) return _cached;

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new System.IO.FileNotFoundException(
                    $"Could not load Resources/{ResourcePath}.json. " +
                    "Run 'node tools/export_unity_ai.js' from the repository root.");

            var data = JsonUtility.FromJson<AiData>(asset.text);
            if (data == null || data.profiles == null || data.profiles.Length == 0 || data.rules == null)
                throw new System.IO.InvalidDataException(
                    "AI data parsed but holds no profiles or rules — the export may have failed.");

            _cached = data;
            return _cached;
        }

        /// <summary>Drop the cache. For tests and for reloading after a re-export.</summary>
        public static void Invalidate() => _cached = null;
    }
}
