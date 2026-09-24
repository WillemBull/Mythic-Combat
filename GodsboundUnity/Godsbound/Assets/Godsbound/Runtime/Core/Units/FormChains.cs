using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Match;

namespace Godsbound.Core.Units
{
    /// <summary>
    /// <c>tickFormChains</c>: a body that becomes another body, and Tepoztecatl's detour through
    /// a myth's ribs.
    /// </summary>
    /// <remarks>
    /// <para>Two triggers reach the same successor: a DEATH (Bata's bull, Nezha's fall) and a TIMER
    /// on the form itself (Nezha's lotus, which nobody has to earn). <c>becomesAfter</c> is measured
    /// from the body's OWN <see cref="Unit.BornAt"/>, so a mid-match spawn is correct.</para>
    /// <para>The swallow is checked FIRST because it CONSUMES the death: Tepoztecatl leaves no
    /// successor on the board, he leaves the board entirely until the myth lets him go. He is not
    /// stored on the host — the host is dropped from the field the frame it dies, and the whole point
    /// is that his exit is triggered BY that death.</para>
    /// </remarks>
    public sealed class FormChains
    {
        /// <summary>One body inside a myth: where it was last seen, and when it comes out regardless.</summary>
        public sealed class Swallow
        {
            public string Key;
            public int Side;
            public Unit Host;
            public Hex Hex;
            public float Until;
        }

        private readonly MatchState _state;
        private readonly List<Swallow> _swallowed = new List<Swallow>();

        public FormChains(MatchState state) { _state = state ?? throw new ArgumentNullException(nameof(state)); }

        /// <summary>Bodies currently inside a myth (<c>S.swallowed</c>).</summary>
        public IReadOnlyList<Swallow> Swallowed => _swallowed;

        private float R(string key) => _state.Database.Powers.Value(key);

        public void Reset() => _swallowed.Clear();

        /// <summary><c>tickFormChains</c>: the swallowed first, then the form chains, newest body first.</summary>
        public void Tick(float elapsed)
        {
            AdvanceSwallows(elapsed);
            var units = _state.Units.All;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Formed) continue;
                if (u.Dead && u.Def.GetFloat("swallowedByMyth") != 0f && BeginSwallow(u, elapsed))
                {
                    u.Formed = true;
                    continue;
                }
                string key = u.Def.GetString("becomesOnDeath");
                float after = u.Def.GetFloat("becomesAfter");
                bool byDeath = u.Dead && key != "";
                bool byTimer = !u.Dead && after != 0f && elapsed - u.BornAt >= after;
                if (!byDeath && !byTimer) continue;
                u.Formed = true;
                if (byTimer) u.Kill(); // the form is spent; the successor takes its place this frame
                // Resolved through the registry, not the roster: a form chain is a property of the
                // UNIT, so it must not break just because the successor is off-faction.
                var def = _state.Database.ResolveUnit(_state.FactionFor(u.Side), key);
                if (def == null) continue;
                _state.Units.Spawn(u.Side, def, Room(u.Hex), free: true, bornAt: elapsed);
            }
        }

        /// <summary>
        /// The host is the nearest enemy MYTH within reach of where he fell — which covers the case
        /// everyone pictures without needing an attacker, and there frequently is none: poison, auras
        /// and half the god powers kill with nobody holding the weapon. NO myth in reach means he
        /// simply dies, which is the cost of the ability and the reason it is not a free extra life.
        /// </summary>
        private bool BeginSwallow(Unit u, float elapsed)
        {
            if (u.SwallowUsed) return false;
            var host = _state.Units.All
                .Where(m => !m.Dead && m.Side != u.Side && m.Def.cat == "myth" &&
                            Hex.Distance(m.Hex, u.Hex) <= R("swallowRadius"))
                .OrderBy(m => Hex.Distance(m.Hex, u.Hex)).FirstOrDefault();
            if (host == null) return false;
            _swallowed.Add(new Swallow
            {
                Key = u.Key, Side = u.Side, Host = host, Hex = host.Hex,
                Until = elapsed + u.Def.GetFloat("swallowedByMyth", R("swallowDefault"))
            });
            return true;
        }

        /// <summary>
        /// The recorded hex trails the host while it lives, so he bursts out wherever it got to. If the
        /// timer runs out first he cuts his way out and the myth comes apart with him.
        /// </summary>
        private void AdvanceSwallows(float elapsed)
        {
            for (int i = _swallowed.Count - 1; i >= 0; i--)
            {
                var w = _swallowed[i];
                if (w.Host != null && !w.Host.Dead)
                {
                    w.Hex = w.Host.Hex;
                    if (elapsed < w.Until) continue;
                    _state.Combat.KillUnit(w.Host, elapsed);
                }
                _swallowed.RemoveAt(i);
                var def = _state.Database.ResolveUnit(_state.FactionFor(w.Side), w.Key);
                if (def == null) continue;
                var back = _state.Units.Spawn(w.Side, def, Room(w.Hex), free: true, bornAt: elapsed);
                back.RestoreHealth(back.MaxHp * R("swallowReturnHealth"));
                back.SwallowUsed = true; // once per life: he is not an infinite loop of rebirths
            }
        }

        /// <summary>This hex if a body fits, else the first neighbour that takes one, else this hex anyway.</summary>
        private Hex Room(Hex hex)
        {
            if (_state.Units.HasCapacity(hex, false)) return hex;
            foreach (var h in hex.Neighbors())
                if (_state.Terrain.RouteOk(h, false, _state.Buildings.RouteBlocker) &&
                    _state.Units.HasCapacity(h, false)) return h;
            return hex;
        }
    }
}
