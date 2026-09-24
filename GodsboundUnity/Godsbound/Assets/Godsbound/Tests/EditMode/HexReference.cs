using System;
using System.IO;
using UnityEngine;

namespace Godsbound.Tests
{
    /// <summary>
    /// The browser game's own answers, loaded from
    /// <c>Fixtures/hex_reference.json</c>. Regenerate with
    /// <c>node tools/export_unity_reference.js</c> from the repository root whenever the
    /// HTML game's board rules change.
    /// </summary>
    /// <remarks>
    /// These tests compare the C# port against the reference rather than against
    /// hand-written expectations on purpose: a retyped expectation can be wrong in
    /// exactly the same way as the code it checks, and then the suite is green and the
    /// port is broken. The fixture cannot be wrong that way — it is the shipping game's
    /// output.
    /// </remarks>
    public static class HexReference
    {
        [Serializable] public class NeighborRef { public int c; public int r; }

        [Serializable]
        public class CellRef
        {
            public int c;
            public int r;
            public string terrain;
            public NeighborRef[] neighbors;
            public bool passableGround;
            public bool passableFlying;
            public bool neutralRow;
            public int side;
        }

        [Serializable]
        public class TerrainRef
        {
            public string code;
            public string name;
            public float speed;
            public bool block;
            public float cover;
            public bool highground;
            /// <summary>Board fill colours from TINFO — presentation data, verified here.</summary>
            public string fill;
            public string fill2;
        }

        [Serializable]
        public class TerrainPassabilityRef
        {
            public string code;
            public bool ground;
            public bool flying;
        }

        [Serializable]
        public class Root
        {
            public string generated;
            public string source;
            public int cols;
            public int rows;
            public int aiRows;
            public int neutralRows;
            public int playerRows;
            public int playerRow0;
            public string[] tmapInitial;
            public float tiltSquash;
            public int hexCapacity;
            public float geomHex;
            public float geomOx;
            public float geomOy;
            /// <summary>Flat [c, r, x, y, ...].</summary>
            public float[] centers;
            public TerrainRef[] terrain;
            /// <summary>Passability per terrain code, from the game's own passable().</summary>
            public TerrainPassabilityRef[] terrainPassability;
            public CellRef[] cells;
            /// <summary>Flat cellCount x cellCount matrix; index = a * cellCount + b.</summary>
            public int[] distMatrix;
        }

        private static Root _cached;

        public static Root Data
        {
            get
            {
                if (_cached != null) return _cached;

                string path = Path.Combine(Application.dataPath,
                    "Godsbound", "Tests", "EditMode", "Fixtures", "hex_reference.json");

                if (!File.Exists(path))
                    throw new FileNotFoundException(
                        "Reference fixture missing. Run 'node tools/export_unity_reference.js' " +
                        "from the repository root to regenerate it.", path);

                _cached = JsonUtility.FromJson<Root>(File.ReadAllText(path));

                if (_cached == null || _cached.cells == null || _cached.cells.Length == 0)
                    throw new InvalidDataException(
                        "Reference fixture parsed but is empty — the exporter may have failed.");

                return _cached;
            }
        }
    }
}
