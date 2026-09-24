using System.Collections.Generic;
using Godsbound.Core;
using Godsbound.Core.Combat;
using Godsbound.Core.Match;
using UnityEngine;

namespace Godsbound.Presentation
{
    /// <summary>
    /// Roadmap 6.5: a rising, fading "-12" over every landed hit, units and buildings alike.
    /// </summary>
    /// <remarks>
    /// The numbers come from <see cref="CombatSystem.Hit"/>. Watching health bars instead would have
    /// been presentation-only but dishonest: poison, splash, auras and god powers all move HP, and
    /// the browser pushes its number from <c>dealDamage</c> alone.
    /// </remarks>
    [AddComponentMenu("Godsbound/Damage Numbers")]
    public sealed class DamageNumbers : MonoBehaviour
    {
        [Tooltip("World units per hex circumradius. Match the Board View.")]
        [SerializeField] private float unitsPerHex = 1f;

        private MatchState match;
        private readonly DamageNumberList numbers = new DamageNumberList();
        private readonly List<TextMesh> labels = new List<TextMesh>();

        public DamageNumberList Numbers => numbers;
        public IReadOnlyList<DamageNumberList.Entry> Live => numbers.Live;
        public int Count => numbers.Count;
        /// <summary>Labels currently showing a number. Everything past this is a parked pool entry.</summary>
        public int VisibleCount
        {
            get { int n = 0; foreach (var l in labels) if (l.gameObject.activeSelf) n++; return n; }
        }

        public void Bind(MatchState state)
        {
            Unsubscribe();
            numbers.Clear();
            match = state;
            Subscribe();
            Sync();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() { Unsubscribe(); numbers.Clear(); Sync(); }
        private void OnDestroy() { Unsubscribe(); Clear(); }
        private void Subscribe() { Unsubscribe(); if (match != null) match.Combat.Hit += OnHit; }
        private void Unsubscribe() { if (match != null) match.Combat.Hit -= OnHit; }

        private void OnHit(HitReport hit)
        {
            if (hit.Amount <= 0f) return;
            BoardPoint at;
            int side;
            if (hit.OnBuilding)
            {
                var layout = match.Units.Layout;
                var a = layout.Center(hit.Building.HexA);
                var b = layout.Center(hit.Building.HexB);
                at = new BoardPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
                side = hit.Building.Side;
            }
            else { at = hit.Victim.Position; side = hit.Victim.Side; }
            // The Side carried here is the VICTIM's, so a number is coloured like the health bar it
            // just took a bite out of. Add drops it on the floor at the cap, which is the point.
            numbers.Add(at.X, at.Y, hit.Amount, side, hit.At);
        }

        // No Update: MatchController.Advance syncs the views once per frame after the step.
        public void Sync()
        {
            float elapsed = match?.Elapsed ?? 0f;
            numbers.Expire(elapsed);
            var live = numbers.Live;
            while (labels.Count < live.Count) labels.Add(NewLabel());
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (i >= live.Count) { label.gameObject.SetActive(false); continue; }
                var entry = live[i];
                float p = DamageNumberList.Progress(entry, elapsed);
                var world = BoardWorld.ToWorld(new BoardPoint(entry.X, entry.Y), unitsPerHex);
                world.y += DamageNumberList.Rise * unitsPerHex * p;
                world.z = -0.6f;
                label.gameObject.SetActive(true);
                label.transform.localPosition = world;
                label.transform.localScale = BoardWorld.UprightScale(unitsPerHex);
                label.text = DamageNumberList.Text(entry.Amount);
                var tint = entry.Side == 0 ? new Color(1f, 0.78f, 0.25f) : new Color(0.98f, 0.42f, 0.36f);
                tint.a = 1f - p;
                label.color = tint;
            }
        }

        private TextMesh NewLabel()
        {
            var go = new GameObject("DamageNumber") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 48;
            // The building name label's 48/0.075 pairing is already tuned to read at phone size;
            // a damage number wants the same weight, and UprightScale carries the board's zoom.
            text.characterSize = 0.075f;
            text.GetComponent<MeshRenderer>().sortingOrder = 30500;
            return text;
        }

        private void Clear()
        {
            numbers.Clear();
            foreach (var label in labels) if (label != null) UnitArtCatalog.Release(label.gameObject);
            labels.Clear();
        }
    }
}
