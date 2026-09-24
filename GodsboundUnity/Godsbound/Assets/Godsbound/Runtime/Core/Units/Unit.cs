using Godsbound.Core.Data;
using System.Collections.Generic;
using Godsbound.Core.Movement;

namespace Godsbound.Core.Units
{
    /// <summary>
    /// A live unit on the board.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately lean.</b> The browser game's unit record carries roughly fifty
    /// fields, but the great majority are per-ability timers — poison, frenzy, aegis,
    /// last-stand, snipe windup, form chains — that belong to the combat and god-power
    /// steps. Porting them now would mean fifty fields nothing reads, and the fields whose
    /// semantics matter most (which of several clocks counts up, which counts down) would be
    /// guessed rather than derived from working code. This holds what U9 through U11 need
    /// and grows a field at a time as each system lands.</para>
    /// <para><see cref="Hex"/> is the authoritative cell for occupancy; <see cref="Position"/>
    /// is the unsquashed logical position used for movement and radius effects. HANDOFF is
    /// explicit that visible-radius effects must select on POSITION, not hex, because the
    /// hex advances before the sprite finishes moving.</para>
    /// </remarks>
    public sealed class Unit
    {
        public int Id { get; }

        /// <summary>0 = player, 1 = AI.</summary>
        public int Side { get; private set; }

        /// <summary>The side this unit belongs to while Jade Emperor's Celestial Decree holds it.</summary>
        public int OriginalSide { get; private set; }

        /// <summary>When the conscription ends (<c>conscriptedUntil</c>); 0 = not conscripted.</summary>
        public float ConscriptedUntil { get; private set; }

        /// <summary>Sun Wukong has already changed this unit; it cannot change again.</summary>
        public bool Transformed { get; private set; }

        /// <summary>The unit key in its faction's table, e.g. "spear".</summary>
        public string Key { get; private set; }

        /// <summary>The exported definition this unit was built from.</summary>
        public UnitData Def { get; private set; }

        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public bool Dead => Hp <= 0f;

        public int Size { get; private set; }

        /// <summary>
        /// Flying units occupy a separate capacity layer and ignore terrain when routing.
        /// </summary>
        public bool Flying { get; private set; }

        /// <summary>The cell this unit occupies, for capacity and targeting.</summary>
        public Hex Hex { get; private set; }

        /// <summary>Unsquashed logical position. Never a world or screen coordinate.</summary>
        public BoardPoint Position { get; private set; }

        /// <summary>
        /// True when the unit has no drawn route and simply stands — how emergency spawns
        /// and conjured units arrive. Ports the browser's <c>free</c> flag.
        /// </summary>
        public bool Free { get; set; }

        internal List<Hex> RoutePath;
        public IReadOnlyList<Hex> Route => RoutePath;
        public int RouteIndex { get; internal set; }
        public bool HasRoute => RoutePath != null && RouteIndex < RoutePath.Count;
        public bool Arrived { get; internal set; } = true;
        public Hex? LastHex { get; internal set; }
        public Hex? AssistFrom { get; internal set; }
        public float WaitSeconds { get; internal set; }
        public float DeviateSeconds { get; internal set; }
        public bool Sidestepped { get; internal set; }
        public bool Hold { get; set; }
        public string Duty { get; set; }
        public MovementContact Engagement { get; set; }
        public Buildings.Building MarchGoal { get; internal set; }
        public Buildings.Building LockedGoal { get; set; }
        /// <summary>Seconds until the next hit; NaN requests the browser's initial random delay.</summary>
        public float AttackTimer { get; set; } = float.NaN;
        internal bool FinishingSidestep;

        // Elapsed match-time deadlines; supplied by the later power/combat systems.
        public float StunUntil { get; set; }
        public float RootUntil { get; set; }
        public float SlowUntil { get; set; }
        public float BuffUntil { get; set; }
        public float BuffSpeed { get; set; } = 1f;

        /// <summary>Elapsed time at which a conjured unit quietly vanishes (<c>despawnAt</c>); 0 = never.</summary>
        public float DespawnAt { get; set; }

        /// <summary>Hou Yi's Sun-Shooter: counting down to the next shot, then the windup itself.</summary>
        public float SnipeTimer { get; set; }
        public bool Sniping { get; set; }
        public float SnipeFireAt { get; set; }

        /// <summary>A healer's own cadence (<c>healT</c>), independent of its attack timer.</summary>
        public float HealTimer { get; set; }

        /// <summary>Orpheus's shades and Heracles's rage, both counting UP to their interval.</summary>
        public float SummonTimer { get; set; }
        public float RageTimer { get; set; }

        /// <summary>Once-per-life hero abilities: Perseus's head, Odysseus's veil, Ariadne's split.</summary>
        public bool GorgonUsed { get; set; }
        public bool StalkHung { get; set; }
        public bool StalkSpent { get; set; }
        public bool Split { get; set; }

        /// <summary>Hephaestus's Forge was spent on this body when it was created.</summary>
        public bool Forged { get; set; }

        /// <summary>A form chain has already resolved this body (<c>formed</c>): it leaves one successor, never two.</summary>
        public bool Formed { get; set; }

        /// <summary>Tepoztecatl has already been swallowed once; he is not an endless rebirth.</summary>
        public bool SwallowUsed { get; set; }
        public Combat.CombatStatus Combat { get; } = new Combat.CombatStatus();

        internal void RestoreHealth(float amount) => Hp = System.Math.Min(MaxHp, System.Math.Max(0f, amount));

        /// <summary>Re-scale the body itself — Hephaestus's Forge and Ariadne's split, both permanent.</summary>
        public void SetMaxHp(float max, float hp)
        {
            if (max <= 0f) throw new System.ArgumentOutOfRangeException(nameof(max), max, "a body needs positive health");
            MaxHp = max;
            Hp = System.Math.Min(max, System.Math.Max(0f, hp));
        }

        /// <summary>Copy the drawn route; congestion must not mutate the caller's list.</summary>
        public void SetRoute(IEnumerable<Hex> route)
        {
            var copy = route == null ? null : new List<Hex>(route);
            if (copy != null)
                foreach (var hex in copy)
                    if (!Board.InBounds(hex))
                        throw new System.ArgumentOutOfRangeException(nameof(route));
            RoutePath = copy;
            RouteIndex = 0;
            Free = copy == null || copy.Count == 0;
            WaitSeconds = DeviateSeconds = 0f;
            Sidestepped = false;
        }

        /// <summary>Set a route and the position already walked along it — a unit placed mid-march.</summary>
        public void SetRoute(IEnumerable<Hex> route, int walked)
        {
            SetRoute(route);
            if (walked < 0 || walked > (RoutePath?.Count ?? 0))
                throw new System.ArgumentOutOfRangeException(nameof(walked));
            RouteIndex = walked;
        }

        /// <summary>Match time at which this body appeared, in seconds elapsed.</summary>
        public float BornAt { get; }

        public Unit(int id, int side, UnitData def, Hex hex, HexLayout layout,
                    bool free = true, float bornAt = 0f)
        {
            if (def == null) throw new System.ArgumentNullException(nameof(def));
            if (layout == null) throw new System.ArgumentNullException(nameof(layout));
            if (!Board.InBounds(hex))
                throw new System.ArgumentOutOfRangeException(nameof(hex), $"{hex} is off the board");

            Id = id;
            Side = side;
            Key = def.key;
            Def = def;
            MaxHp = def.hp;
            Hp = def.hp;
            Size = def.size;
            Flying = def.IsFlying;
            Hex = hex;
            Position = layout.Center(hex);
            Free = free;
            BornAt = bornAt;
            SnipeTimer = def.GetFloat("snipeEvery"); // starts at a full interval: the first shot is at +snipeEvery
        }

        /// <summary>Move to a cell, snapping the logical position to its centre.</summary>
        public void PlaceAt(Hex hex, HexLayout layout)
        {
            if (!Board.InBounds(hex))
                throw new System.ArgumentOutOfRangeException(nameof(hex), $"{hex} is off the board");
            Hex = hex;
            Position = layout.Center(hex);
        }

        /// <summary>Set the logical position without changing the occupied cell.</summary>
        public void MoveTo(BoardPoint position) => Position = position;

        /// <summary>Set the occupied cell without snapping the position — mid-step advance.</summary>
        public void EnterHex(Hex hex)
        {
            if (!Board.InBounds(hex))
                throw new System.ArgumentOutOfRangeException(nameof(hex), $"{hex} is off the board");
            Hex = hex;
        }

        /// <summary>Apply damage, floored at zero. Returns the amount absorbed.</summary>
        public float TakeDamage(float amount)
        {
            if (amount < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(amount), amount, "damage cannot be negative");
            float before = Hp;
            Hp = Hp - amount < 0f ? 0f : Hp - amount;
            return before - Hp;
        }

        /// <summary>Heal, capped at <see cref="MaxHp"/>. Never revives a dead unit.</summary>
        public float Heal(float amount)
        {
            if (amount < 0f)
                throw new System.ArgumentOutOfRangeException(nameof(amount), amount, "healing cannot be negative");
            if (Dead) return 0f;
            float before = Hp;
            Hp = Hp + amount > MaxHp ? MaxHp : Hp + amount;
            return Hp - before;
        }

        /// <summary>Celestial Decree: fight for <paramref name="side"/> until <paramref name="until"/>, then return.</summary>
        public void Conscript(int side, float until)
        {
            OriginalSide = Side;
            Side = side;
            ConscriptedUntil = until;
            SetRoute(null);
            Engagement = null;
        }

        /// <summary><c>releaseConscript</c>: back to the original side, standing free with no route.</summary>
        public void ReleaseConscript()
        {
            if (ConscriptedUntil == 0f) return;
            Side = OriginalSide;
            ConscriptedUntil = 0f;
            SetRoute(null);
            Engagement = null;
        }

        /// <summary>
        /// <c>transformUnit</c>: become another unit for good, keeping the health FRACTION (never
        /// below 1 hp). Refused once already transformed.
        /// </summary>
        public bool Transform(UnitData into)
        {
            if (into == null || Transformed) return false;
            float frac = MaxHp > 0f ? Hp / MaxHp : 1f;
            Key = into.key;
            Def = into;
            MaxHp = into.hp;
            Hp = System.Math.Max(1f, into.hp * frac);
            Size = into.size;
            Flying = into.IsFlying;
            Transformed = true;
            Engagement = null;
            return true;
        }

        /// <summary>Kill outright, for alternate death paths.</summary>
        public void Kill() => Hp = 0f;

        public override string ToString() =>
            $"#{Id} side {Side} {Key}{(Flying ? " (flying)" : "")} at {Hex} {Hp:0}/{MaxHp:0}" +
            (Dead ? " DEAD" : "");
    }
}
