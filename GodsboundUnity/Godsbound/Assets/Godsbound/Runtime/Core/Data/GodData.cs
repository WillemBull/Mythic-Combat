using System;
using System.Collections.Generic;

namespace Godsbound.Core.Data
{
    /// <summary>A god: its unlock cost, active power, cooldown, myth unit and passive.</summary>
    [Serializable]
    public class GodData
    {
        public string faction;
        public string key;
        public string name;
        public string emoji;

        /// <summary>Favor to unlock the god.</summary>
        public int cost;

        public string power;

        /// <summary>Favor to cast the power.</summary>
        public int pcost;

        /// <summary>Power cooldown in seconds.</summary>
        public float cd;

        /// <summary>Unit key of the myth this god makes buyable once unlocked.</summary>
        public string myth;

        public string desc;
        public string passiveKey;
        public string passive;

        public List<ExtraValue> extras = new List<ExtraValue>();

        /// <summary>True when this god makes a myth buyable once unlocked.</summary>
        public bool HasMyth => !string.IsNullOrEmpty(myth);

        /// <summary>True when this god carries a passive, active only while unlocked.</summary>
        public bool HasPassiveKey => !string.IsNullOrEmpty(passiveKey);

        public bool Has(string field) => Find(field) != null;

        public float GetFloat(string field, float fallback = 0f)
        {
            var e = Find(field);
            return e != null && float.TryParse(e.value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private ExtraValue Find(string field)
        {
            if (extras == null) return null;
            for (int i = 0; i < extras.Count; i++)
                if (extras[i].key == field) return extras[i];
            return null;
        }

        public override string ToString() => $"{faction}/{key} ({name})";
    }

    /// <summary>
    /// God constants that live in browser function bodies rather than in the god tables,
    /// MEASURED by <c>tools/export_unity_data.js</c> (U23). Never edit these here.
    /// </summary>
    [Serializable]
    public class GodRates
    {
        /// <summary>Long Wang's Lord of the Four Seas: favor/second per water tile on the owner's half.</summary>
        public float waterFavorPerTile;

        /// <summary>Ptah's Creator's Word: standing buildings' max hp is multiplied by this at unlock.</summary>
        public float ptahBuildingHpMultiplier;

        /// <summary>The passive key whose unlock triggers the building boost.</summary>
        public string ptahPassiveKey;
    }
}
