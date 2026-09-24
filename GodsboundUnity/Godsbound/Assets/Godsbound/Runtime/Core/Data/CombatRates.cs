using System;
using System.Collections.Generic;
using System.Linq;

namespace Godsbound.Core.Data
{
    [Serializable] public sealed class CombatNumber { public string key; public float value; }
    [Serializable] public sealed class CombatMatchup { public string key; public string target; }

    /// <summary>Rules exported from the browser's combat functions, including the armour switch.</summary>
    [Serializable]
    public sealed class CombatRates
    {
        public bool armorDamageMultipliers;
        public CombatNumber[] numbers;
        public CombatMatchup[] categoryBeats, damageStrong, damageWeak;

        // Built on first use. CalculateDamage asks for two dozen of these per hit and the target
        // scan asks per candidate per frame, so a linear string search here was the hottest
        // thing in the simulation. Not serialized: JsonUtility ignores non-public, non-marked fields.
        [NonSerialized] private Dictionary<string, float> _byKey;

        /// <summary>A named combat constant. Throws on an unknown key, as the export is complete by contract.</summary>
        public float Value(string key)
        {
            if (_byKey == null)
            {
                var map = new Dictionary<string, float>(numbers?.Length ?? 0);
                if (numbers != null)
                    foreach (var n in numbers)
                        if (!map.ContainsKey(n.key)) map.Add(n.key, n.value); // first wins, as the linear search did
                _byKey = map;
            }
            if (_byKey.TryGetValue(key, out var value)) return value;
            throw new InvalidOperationException($"combat export has no constant '{key}'");
        }

        public static bool Matches(CombatMatchup[] table, string key, string target) =>
            table.Any(p => p.key == key && p.target == target);
    }
}
