using Godsbound.Core.Data;

namespace Godsbound.Core.Buildings
{
    /// <summary>The three building types. Each side has exactly one of each.</summary>
    public enum BuildingType
    {
        City,
        Temple,
        Fortress
    }

    /// <summary>
    /// A building on the board. Occupies TWO adjacent hexes.
    /// </summary>
    /// <remarks>
    /// <para>Destroying all three of a side's buildings wins immediately; at time, buildings
    /// destroyed decides, with total building damage as the tiebreaker. So <see cref="Dead"/>
    /// and <see cref="DamageTaken"/> are both load-bearing beyond rendering.</para>
    /// <para>A destroyed building stops blocking routes but keeps occupying its hexes for
    /// rendering — the rubble is walkable. <see cref="BuildingMap"/> owns that distinction.</para>
    /// </remarks>
    public sealed class Building
    {
        public int Side { get; }
        public BuildingType Type { get; }
        public string Name { get; }

        /// <summary>The first of the two hexes. Order matches the export.</summary>
        public Hex HexA { get; }

        /// <summary>The second hex, always adjacent to <see cref="HexA"/>.</summary>
        public Hex HexB { get; }

        public float MaxHp { get; private set; }

        /// <summary>Max hp as built, restored by <see cref="Reset"/> after any in-match boost.</summary>
        public float BaseMaxHp { get; }
        public float Hp { get; private set; }
        public bool Dead => Hp <= 0f;
        public float InvulnerableUntil { get; set; }
        /// <summary>When this building was last damaged. Zero means never hit — what the AI's support
        /// trigger reads as "not under attack", standing in for the browser's undefined.</summary>
        public float LastHitAt { get; set; }
        public float AttackTimer { get; set; }

        /// <summary>Damage absorbed so far — the at-time tiebreaker.</summary>
        public float DamageTaken => MaxHp - Hp;

        public Building(int side, BuildingType type, string name, Hex a, Hex b, float maxHp)
        {
            if (!Board.InBounds(a)) throw new System.ArgumentOutOfRangeException(nameof(a), $"{a} is off the board");
            if (!Board.InBounds(b)) throw new System.ArgumentOutOfRangeException(nameof(b), $"{b} is off the board");
            if (Hex.Distance(a, b) != 1)
                throw new System.ArgumentException($"a building's hexes must be adjacent; {a} and {b} are {Hex.Distance(a, b)} apart");
            if (maxHp <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(maxHp), maxHp, "a building needs positive hp");

            Side = side;
            Type = type;
            Name = name;
            HexA = a;
            HexB = b;
            MaxHp = BaseMaxHp = maxHp;
            Hp = maxHp;
        }

        /// <summary>Build from an exported definition.</summary>
        public static Building From(BuildingData d) => new Building(
            d.side, ParseType(d.type), d.name,
            new Hex(d.c0, d.r0), new Hex(d.c1, d.r1), d.maxHp > 0f ? d.maxHp : d.hp);

        public static BuildingType ParseType(string type)
        {
            switch (type)
            {
                case "city": return BuildingType.City;
                case "temple": return BuildingType.Temple;
                case "fortress": return BuildingType.Fortress;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(type), type, "Unknown building type");
            }
        }

        /// <summary>The export's lowercase key for a type — the inverse of <see cref="ParseType"/>.</summary>
        /// <remarks>Constants rather than <c>ToString().ToLowerInvariant()</c>: the views call this
        /// per building per frame, and that version allocated two strings each time.</remarks>
        public static string ToKey(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.City: return "city";
                case BuildingType.Temple: return "temple";
                case BuildingType.Fortress: return "fortress";
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(t), t, "Unknown building type");
            }
        }

        /// <summary>True when this building stands on the given hex, alive or not.</summary>
        public bool Occupies(Hex h) => h == HexA || h == HexB;

        /// <summary>Both hexes, in export order.</summary>
        public System.Collections.Generic.IEnumerable<Hex> Hexes()
        {
            yield return HexA;
            yield return HexB;
        }

        /// <summary>Apply damage, floored at zero. Returns the amount actually absorbed.</summary>
        public float TakeDamage(float amount)
        {
            if (amount < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(amount), amount, "damage cannot be negative");
            float before = Hp;
            Hp = Hp - amount < 0f ? 0f : Hp - amount;
            return before - Hp;
        }

        /// <summary>
        /// Multiply max hp, keeping the health FRACTION — Ptah's Creator's Word at unlock.
        /// </summary>
        public void ScaleMaxHp(float factor)
        {
            if (factor <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(factor), factor, "max hp factor must be positive");
            float fraction = Hp / MaxHp;
            MaxHp *= factor;
            Hp = MaxHp * fraction;
        }

        /// <summary>Nu Wa's Five Colored Stones: back to full health at the current max hp.</summary>
        public void RepairFull()
        {
            if (!Dead) Hp = MaxHp;
        }

        /// <summary>Restore to full at the as-built max hp. Used by match reset.</summary>
        /// <remarks>
        /// The browser's <c>startMatch</c> resets <c>hp</c> but never <c>maxhp</c>, so a Ptah boost
        /// compounds across matches there. The port deliberately does not reproduce that.
        /// </remarks>
        public void Reset()
        {
            MaxHp = BaseMaxHp;
            Hp = MaxHp;
            InvulnerableUntil = LastHitAt = AttackTimer = 0f;
        }

        public override string ToString() =>
            $"side {Side} {Type} {HexA}-{HexB} {Hp:0}/{MaxHp:0}{(Dead ? " DEAD" : "")}";
    }
}
