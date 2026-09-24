using System;
using System.Collections.Generic;

namespace Godsbound.Core.Data
{
    /// <summary>One of Ra's three times of day: damage applies while elapsed time is below <see cref="until"/>.</summary>
    [Serializable] public sealed class RaAspect { public string name; public float until; public float damage; }

    /// <summary>
    /// God-power coefficients read by <c>tools/export_unity_data.js</c> from the owning case of
    /// the browser's <c>applyGodPower</c> (and its helpers). Never edit these here.
    /// </summary>
    [Serializable]
    public sealed class PowerRates
    {
        public CombatNumber[] numbers;
        public RaAspect[] raAspects;

        /// <summary>The one non-human unit Ptah's Creator's Word may double (<c>ptahCanShape</c>).</summary>
        public string ptahExtraKey;

        [NonSerialized] private Dictionary<string, float> _byKey;

        /// <summary>A named power constant. Throws on an unknown key; the export is complete by contract.</summary>
        public float Value(string key)
        {
            if (_byKey == null)
            {
                var map = new Dictionary<string, float>();
                if (numbers != null)
                    foreach (var n in numbers)
                        if (!map.ContainsKey(n.key)) map.Add(n.key, n.value);
                _byKey = map;
            }
            if (_byKey.TryGetValue(key, out var value)) return value;
            throw new InvalidOperationException($"power export has no constant '{key}'");
        }

        /// <summary><c>raSolarDamage()</c>: the first aspect whose window has not closed.</summary>
        public float RaDamage(float elapsed)
        {
            foreach (var a in raAspects)
                if (elapsed < a.until) return a.damage;
            throw new InvalidOperationException("Ra has no aspect for elapsed " + elapsed);
        }
    }
}
