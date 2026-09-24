using UnityEngine;
using Godsbound.Core.Data;

namespace Godsbound.Data
{
    /// <summary>
    /// Loads the exported tables into a <see cref="GameDatabase"/>.
    /// </summary>
    /// <remarks>
    /// The only piece of the data path that touches UnityEngine. The POCOs and the database
    /// live in <see cref="Godsbound.Core.Data"/> so the simulation stays engine-free and
    /// testable without a scene; <c>JsonUtility</c> can still populate them because they are
    /// plain public-field classes.
    /// </remarks>
    public static class GameDataLoader
    {
        /// <summary>Path under any <c>Resources</c> folder, without the extension.</summary>
        public const string ResourcePath = "GameData/game_data";

        private static GameDatabase _cached;

        /// <summary>
        /// The database, loaded once and cached. Throws rather than returning null: a missing
        /// or malformed export is a build problem, and failing at the point of loading is far
        /// easier to diagnose than a null reference somewhere in a match.
        /// </summary>
        public static GameDatabase Load()
        {
            if (_cached != null) return _cached;

            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
                throw new System.IO.FileNotFoundException(
                    $"Could not load Resources/{ResourcePath}.json. " +
                    "Run 'node tools/export_unity_data.js' from the repository root.");

            _cached = Parse(asset.text);
            return _cached;
        }

        /// <summary>Parse an export from raw JSON. Used by tests and by <see cref="Load"/>.</summary>
        public static GameDatabase Parse(string json)
        {
            var root = JsonUtility.FromJson<GameDataRoot>(json);

            if (root == null)
                throw new System.IO.InvalidDataException("Game data JSON failed to parse.");
            if (root.units == null || root.units.Count == 0)
                throw new System.IO.InvalidDataException(
                    "Game data parsed but contains no units — the export may have failed.");

            return new GameDatabase(root);
        }

        /// <summary>Drop the cache. For tests and for reloading after a re-export.</summary>
        public static void Invalidate() => _cached = null;
    }
}
