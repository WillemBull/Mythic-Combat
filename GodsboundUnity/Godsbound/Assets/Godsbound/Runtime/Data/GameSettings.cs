using UnityEngine;

namespace Godsbound.Data
{
    /// <summary>
    /// Per-device preferences that belong to the player rather than to a deck.
    /// </summary>
    /// <remarks>
    /// PlayerPrefs, not the deck file: a deck is content the player builds and could one day travel
    /// with them, while this is a single switch about this install. Default on, as roadmap 7.3 says
    /// ("default on for now").
    /// </remarks>
    public static class GameSettings
    {
        private const string TutorialKey = "godsbound.tutorial";

        public static bool Tutorial
        {
            get => PlayerPrefs.GetInt(TutorialKey, 1) != 0;
            set { PlayerPrefs.SetInt(TutorialKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
