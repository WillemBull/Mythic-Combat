using Godsbound.Core.Decks;
using Godsbound.Data;

namespace Godsbound.Tests
{
    /// <summary>
    /// A deck and a deck file for tests that need a match but do not care which deck it is.
    /// </summary>
    /// <remarks>
    /// <para>Found 2026-09-26: <c>MatchController.Initialize</c> falls back to <see cref="DeckStore"/>,
    /// which lives in the player's persistentDataPath. Five tests took that fallback, so the suite was
    /// quietly reading whatever deck was saved on the machine it ran on. It passed for weeks because
    /// nobody had saved one — then Willem built an Aztec deck in a playtest and two tests that assume
    /// Egypt failed, with nothing in the diff to explain it.</para>
    /// <para>A test that starts a match names its deck and its file. Both, always: the deck decides
    /// the assertions, and the scratch path keeps the test from touching the player's real save.</para>
    /// </remarks>
    public static class TestDeck
    {
        public static DeckPreset Egypt() => DeckRules.Default(GameDataLoader.Load(), "egypt");
        public static DeckPreset Of(string faction) => DeckRules.Default(GameDataLoader.Load(), faction);
        /// <summary>A throwaway deck file, so the store loads empty rather than loading the player's.</summary>
        public static string Scratch() => System.IO.Path.GetTempFileName();
    }
}
