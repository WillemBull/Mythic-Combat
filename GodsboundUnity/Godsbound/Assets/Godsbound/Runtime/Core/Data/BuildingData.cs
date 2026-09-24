using System;

namespace Godsbound.Core.Data
{
    /// <summary>Per-type building stats: display name and hp. Three of these.</summary>
    [Serializable]
    public class BuildingTypeData
    {
        public string type;
        public string name;
        public string emoji;
        public float hp;

        public override string ToString() => $"{type} ({hp:0} hp)";
    }

    /// <summary>One building's footprint in ABSOLUTE board rows.</summary>
    [Serializable]
    public class BuildingLayoutData
    {
        public string type;
        public int c0;
        public int r0;
        public int c1;
        public int r1;

        public override string ToString() => $"{type} ({c0},{r0})-({c1},{r1})";
    }

    /// <summary>
    /// A resolved starting building: which side, its type, footprint and hp.
    /// </summary>
    /// <remarks>
    /// Not exported directly. There is no canonical layout to export — the player's comes
    /// from the deck preset and the AI's is RANDOMIZED at match start — so this is DERIVED
    /// by <c>GameDatabase.Buildings</c> from the per-type stats plus the player's default
    /// layout, mirrored for the AI. See that property for why.
    /// </remarks>
    [Serializable]
    public class BuildingData
    {
        public int side;
        public string type;
        public string name;
        public string emoji;
        public int c0;
        public int r0;
        public int c1;
        public int r1;
        public float hp;
        public float maxHp;

        public override string ToString() => $"side {side} {type} ({c0},{r0})-({c1},{r1})";
    }
}
