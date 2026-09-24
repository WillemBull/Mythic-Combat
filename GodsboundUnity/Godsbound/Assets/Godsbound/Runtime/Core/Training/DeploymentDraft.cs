using System;
using System.Collections.Generic;
using System.Linq;
using Godsbound.Core.Buildings;
using Godsbound.Core.Data;
using Godsbound.Core.Match;

namespace Godsbound.Core.Training
{
    /// <summary>A drag is unpaid until committed. Hex input follows the browser pointermove rules.</summary>
    public sealed class DeploymentDraft
    {
        private readonly MatchState state;
        private readonly List<Hex> route = new List<Hex>();
        public IReadOnlyList<Hex> Route => route;
        public IReadOnlyList<Hex> Continuation { get; private set; } = Array.Empty<Hex>();
        public UnitData Definition { get; }
        public Building Source { get; private set; }
        public Building Goal { get; private set; }
        public float StartedAt { get; private set; }
        public bool Cancelled { get; private set; }
        public DeploymentDraft(MatchState match, UnitData def)
        {
            state = match ?? throw new ArgumentNullException(nameof(match));
            Definition = def ?? throw new ArgumentNullException(nameof(def));
        }
        private bool Legal(Hex h) => Board.InBounds(h) && state.Terrain.RouteOk(h, Definition.IsFlying, state.Buildings.RouteBlocker);
        public void Visit(Hex h)
        {
            if (Cancelled || !state.Live || !Board.InBounds(h)) return;
            var building = state.Buildings.StandingAt(h);
            if (Source == null)
            {
                if (building != null && building.Side == 0) { Source = building; StartedAt = state.Elapsed; }
                return;
            }
            if (Source.Dead) { Cancel(); return; }
            if (building == Source) { route.Clear(); RefreshPreview(); return; }
            int existing = route.IndexOf(h);
            if (existing >= 0)
            {
                if (existing < route.Count - 1) { route.RemoveRange(existing + 1, route.Count - existing - 1); RefreshPreview(); }
                return;
            }
            if (building != null || !Legal(h)) return;
            int countBefore = route.Count;
            if (route.Count == 0)
            {
                if (Source.Hexes().Any(bh => Hex.Distance(bh, h) == 1)) route.Add(h);
                else
                    foreach (var n in h.Neighbors())
                        if (Legal(n) && Source.Hexes().Any(bh => Hex.Distance(bh, n) == 1))
                        { route.Add(n); route.Add(h); break; }
            }
            else
            {
                var last = route[route.Count - 1];
                int distance = Hex.Distance(last, h);
                if (distance == 1) route.Add(h);
                else if (distance == 2)
                    foreach (var n in last.Neighbors())
                        if (Hex.Distance(n, h) == 1 && Legal(n) && !route.Contains(n))
                        { route.Add(n); route.Add(h); break; }
            }
            if (route.Count != countBefore) RefreshPreview();
        }
        private void RefreshPreview()
        {
            Goal = null; Continuation = Array.Empty<Hex>();
            if (route.Count == 0) return;
            int length = int.MaxValue;
            foreach (var b in state.Buildings.All)
            {
                if (b.Dead || b.Side == 0) continue;
                var path = state.Paths.FindPath(route[route.Count - 1],
                    state.Buildings.AdjacentRoutableHexes(b, Definition.IsFlying, state.Terrain), Definition.IsFlying);
                if (path != null && path.Count < length) { Goal = b; Continuation = path; length = path.Count; }
            }
        }
        public bool Commit(bool releasedOnBoard)
        {
            if (Cancelled) return false;
            Cancelled = true;
            if (!releasedOnBoard || !state.Live || Source == null || Source.Dead || route.Count == 0 ||
                route.Any(h => !Legal(h))) return false;
            if (!state.Training.TryEnqueue(state.Resources[0], 0, Definition, route[0], Source,
                state.TrainingModifiersFor(0), state.Elapsed, out var remaining, out var order, route, Goal, StartedAt)) return false;
            if (state.Powers.ConsumePtahCharge(0, Definition)) order.SpawnCount = 2;
            state.Resources[0] = remaining;
            return true;
        }
        public void Cancel() { Cancelled = true; route.Clear(); Goal = null; Continuation = Array.Empty<Hex>(); }
    }
}
