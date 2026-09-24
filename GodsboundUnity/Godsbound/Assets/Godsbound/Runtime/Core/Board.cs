namespace Godsbound.Core
{
    /// <summary>Which half of the board a row belongs to.</summary>
    public enum BoardSide
    {
        /// <summary>The player's half — the bottom rows.</summary>
        Player = 0,
        /// <summary>The AI's half — the top rows.</summary>
        Ai = 1,
        /// <summary>No-man's land between the halves.</summary>
        Neutral = -1
    }

    /// <summary>
    /// Board dimensions and row ownership, ported from <c>godsbound_beta.html</c>.
    /// </summary>
    /// <remarks>
    /// <para>The browser game's HANDOFF is emphatic that <c>ROWS/2</c> was a bug: it
    /// conflated three different questions — how deep is a half, where does the player's
    /// half start, and which row belongs to whom — and those stop being the same number
    /// the moment a neutral band exists. Keep them as the four separate constants below
    /// and never reintroduce a halving.</para>
    /// <para>Absolute row layout, top to bottom:
    /// rows <c>0 .. AiRows-1</c> are the AI's half;
    /// rows <c>AiRows .. PlayerRow0-1</c> are no-man's land;
    /// rows <c>PlayerRow0 .. Rows-1</c> are the player's half.</para>
    /// </remarks>
    public static class Board
    {
        /// <summary>Columns on the board.</summary>
        public const int Cols = 9;

        /// <summary>Depth of the AI's half, in rows, starting at row 0.</summary>
        public const int AiRows = 6;

        /// <summary>Depth of the open no-man's land between the halves. May be 0.</summary>
        public const int NeutralRows = 1;

        /// <summary>Depth of the player's half, in rows.</summary>
        public const int PlayerRows = 6;

        /// <summary>Absolute row where the player's half starts.</summary>
        public const int PlayerRow0 = AiRows + NeutralRows;

        /// <summary>Total rows on the board.</summary>
        public const int Rows = AiRows + NeutralRows + PlayerRows;

        /// <summary>Total cells on the board.</summary>
        public const int CellCount = Cols * Rows;

        /// <summary>
        /// One unit per hex per layer. One ground and one flying unit may share a hex;
        /// two ground or two flying units may not.
        /// </summary>
        public const int HexCapacity = 1;

        public static bool InBounds(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;

        public static bool InBounds(Hex h) => InBounds(h.C, h.R);

        /// <summary>True when the row is open no-man's land owned by neither side.</summary>
        public static bool IsNeutralRow(int r) => r >= AiRows && r < PlayerRow0;

        /// <summary>Which side owns a row. Ports <c>sideForRow</c>.</summary>
        public static BoardSide SideForRow(int r)
        {
            if (IsNeutralRow(r)) return BoardSide.Neutral;
            return r < AiRows ? BoardSide.Ai : BoardSide.Player;
        }

        /// <summary>
        /// Stable index for a cell, <c>r * Cols + c</c>. Used for flat arrays and for
        /// indexing the reference fixture's distance matrix.
        /// </summary>
        public static int Index(int c, int r) => r * Cols + c;

        public static int Index(Hex h) => Index(h.C, h.R);

        /// <summary>Every cell on the board, row-major, matching <see cref="Index(int,int)"/>.</summary>
        public static System.Collections.Generic.IEnumerable<Hex> AllCells()
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    yield return new Hex(c, r);
        }
    }
}
