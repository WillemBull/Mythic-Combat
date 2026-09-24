using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Data;
using Godsbound.Core.Decks;

namespace Godsbound.Core.Setup
{
    /// <summary>
    /// One side's own board, edited on its own during setup and composed into the arena at the start
    /// of the match.
    /// </summary>
    /// <remarks>
    /// <para>Willem, 2026-07-17: the terrain step edits your OWN half as a standalone board, not a
    /// crop of a bigger arena — "think about the arena as the sum of two halves that come together".
    /// So rows here are HALF-LOCAL (row 0 is the half's own first row, nearest no-man's land) and
    /// nothing in this type knows which side of the board it will end up on.</para>
    /// <para>It carries what the browser keeps in three places at once: <c>playerHalf.terrain</c>,
    /// <c>playerHalf.buildings</c> and <c>S.paintMap</c>/<c>S.painted</c>. The paint map is not
    /// derivable from the terrain — a forest the player painted and a forest the map shipped with
    /// look identical, and only the painted one costs budget and can be erased.</para>
    /// </remarks>
    public sealed class BoardHalf
    {
        private readonly char[] cells = new char[Board.Cols * Board.PlayerRows];
        private readonly Dictionary<Hex, char> painted = new Dictionary<Hex, char>();

        public string Faction { get; }
        public List<DeckBuilding> Buildings { get; }
        public int Rows => Board.PlayerRows;
        public int Cols => Board.Cols;

        public BoardHalf(string faction, IEnumerable<DeckBuilding> buildings = null)
        {
            Faction = faction ?? "";
            Buildings = buildings == null ? new List<DeckBuilding>() : buildings.Select(b => b.Copy()).ToList();
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    cells[Index(c, r)] = FreshCode(Faction, c, r);
        }

        private static int Index(int c, int r) => r * Board.Cols + c;

        /// <summary>
        /// <c>origTerrain</c> for a half-local cell: the shipped map's feature where it has one, and
        /// the faction's own filler (Egypt's desert, everyone else's plains) where it does not.
        /// </summary>
        public static char FreshCode(string faction, int c, int localRow)
        {
            char shipped = TerrainMap.Initial[Board.PlayerRow0 + localRow][c];
            char filler = faction == "egypt" ? 'D' : 'P';
            return shipped == 'P' || shipped == 'D' ? filler : shipped;
        }

        public bool Contains(Hex h) => h.C >= 0 && h.C < Cols && h.R >= 0 && h.R < Rows;

        public char this[Hex h]
        {
            get => cells[Index(h.C, h.R)];
            internal set => cells[Index(h.C, h.R)] = value;
        }

        public char this[int c, int r] => cells[Index(c, r)];

        public TerrainType TerrainAt(Hex h) => TerrainTable.FromCode(this[h]);

        /// <summary>The building standing on a hex — the browser's <c>bldAt</c> during the terrain step.</summary>
        public DeckBuilding At(Hex h) => Buildings.FirstOrDefault(b => b.A == h || b.B == h);

        /// <summary>What the player painted here, or 0 for untouched ground.</summary>
        public char PaintedAt(Hex h) => painted.TryGetValue(h, out var code) ? code : '\0';

        public int PaintedCount(char code) => painted.Values.Count(v => v == code);

        internal void SetPaint(Hex h, char code) { if (code == '\0') painted.Remove(h); else painted[h] = code; }

        /// <summary>One string per row, as the browser stores and as a deck saves.</summary>
        public string[] RowStrings()
        {
            var rows = new string[Rows];
            for (int r = 0; r < Rows; r++)
            {
                var row = new char[Cols];
                for (int c = 0; c < Cols; c++) row[c] = cells[Index(c, r)];
                rows[r] = new string(row);
            }
            return rows;
        }

        /// <summary>The half as a deck preset — what the player saves.</summary>
        public DeckPreset ToPreset(GameDatabase db, DeckPreset deck)
        {
            var preset = deck.Copy();
            preset.faction = Faction;
            preset.terrain = RowStrings();
            preset.buildings = Buildings.Select(b => b.Copy()).ToArray();
            return preset;
        }

        /// <summary>
        /// The half a saved deck describes. Terrain that differs from the fresh map is treated as
        /// PAINTED, which is what the browser reconstructs in <c>applyDeckPreset</c>.
        /// </summary>
        public static BoardHalf FromPreset(DeckPreset preset)
        {
            var half = new BoardHalf(preset.faction, preset.buildings);
            for (int r = 0; r < half.Rows; r++)
                for (int c = 0; c < half.Cols; c++)
                {
                    char code = preset.terrain[r][c];
                    var h = new Hex(c, r);
                    half[h] = code;
                    if (code != FreshCode(preset.faction, c, r)) half.SetPaint(h, code);
                }
            return half;
        }
    }
}
