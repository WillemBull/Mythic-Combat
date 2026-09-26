namespace Godsbound.Presentation
{
    /// <summary>
    /// What the player is told while dragging a card, word for word from the browser (U40).
    /// </summary>
    /// <remarks>
    /// These strings are the browser's, not paraphrases of it: the deployment gesture is the first
    /// thing anyone does in this game and the wording is the only instruction they get. A test holds
    /// them to the originals.
    /// </remarks>
    public static class DeploymentPrompt
    {
        /// <summary>On picking a card up. The browser names the three buildings rather than their colours.</summary>
        public static string PickedUp(string unitName) =>
            $"Drag {unitName} through your City, Fortress or Temple, then draw a route.";

        /// <summary>Once the drag has crossed one of your buildings.</summary>
        public const string RouteNext = "Now draw the route. Release to deploy.";

        /// <summary>Released without ever crossing a building.</summary>
        public const string NoBuilding = "Route cancelled — start by dragging through one of your buildings.";

        /// <summary>Released on the building itself, with no route drawn.</summary>
        public const string NoRoute = "Draw at least one hex of route.";

        /// <summary>The cost could not be paid at release.</summary>
        public const string TooPoor = "Not enough resources.";

        /// <summary>
        /// Released somewhere a unit cannot stand. The browser has no message for this — its route
        /// only ever follows legal hexes — so this one is ours, kept in the same voice.
        /// </summary>
        public const string BadHex = "Release on an open battlefield hex.";

        /// <summary>It worked.</summary>
        public static string Training(string unitName) => $"{unitName} training…";

        /// <summary>The message for a release that failed, or null when it did not.</summary>
        public static string ForFailure(bool hadBuilding, bool hadRoute, bool legalHex, bool affordable)
        {
            if (!hadBuilding) return NoBuilding;
            if (!hadRoute) return NoRoute;
            if (!legalHex) return BadHex;
            if (!affordable) return TooPoor;
            return null;
        }
    }
}
