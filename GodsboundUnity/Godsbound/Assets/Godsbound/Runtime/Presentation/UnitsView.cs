using System.Collections.Generic;
using Godsbound.Core;
using Godsbound.Core.Match;
using Godsbound.Core.Units;
using UnityEngine;

namespace Godsbound.Presentation
{
    public sealed class UnitsView : MonoBehaviour
    {
        private sealed class Body
        {
            public GameObject Visual;
            public SpriteRenderer Art, Health;
            public float LastAttack = float.NaN, FlashUntil;
            // Resolved once at creation. A unit's key and side change only through a transformation
            // or conscription, and Sync rebuilds the body when they do.
            public string Key;
            public int Side;
            public UnitArtEntry Entry;
            public bool Fallback;
            // Corpse bookkeeping: >0 once the unit has left the field, and the frame's visibility
            // so a unit that died while hidden does not announce where it was standing.
            public float CorpseUntil;
            public bool Shown;
            // The art scale the unit died at, so the settle is computed from it each frame rather
            // than compounding off the previous frame's squash.
            public Vector3 CorpseScale;
        }

        /// <summary>
        /// How long a body lies where it fell. The browser keeps one unit — the spearman — for
        /// <c>SPEAR_DEATH_DURATION</c> (0.6s) because it is the only one with a death frame;
        /// <c>MatchLoop</c> left that retention to presentation on purpose. Unity has a single
        /// sprite per unit, so every corpse gets the same 0.6s to fade and sink instead.
        /// </summary>
        public const float CorpseSeconds = 0.6f;
        private MatchState state;
        private readonly UnitUpdate visibility = new UnitUpdate();
        private UnitArtCatalog catalog;
        private Sprite marker;
        private Texture2D markerTexture;
        private readonly Dictionary<int, Body> bodies = new Dictionary<int, Body>();
        // Reused across frames so Sync allocates nothing in the steady state.
        private readonly HashSet<int> alive = new HashSet<int>();
        private readonly List<int> gone = new List<int>();
        /// <summary>Living bodies. Corpses are counted by <see cref="CorpseCount"/>.</summary>
        public int Count { get { int n = 0; foreach (var b in bodies.Values) if (b.CorpseUntil <= 0f) n++; return n; } }
        public int CorpseCount { get { int n = 0; foreach (var b in bodies.Values) if (b.CorpseUntil > 0f) n++; return n; } }
        public Transform BodyOf(int id) => bodies.TryGetValue(id, out var b) ? b.Visual.transform : null;
        public void Bind(MatchState match) { Clear(); state = match; Sync(); }
        // No LateUpdate: MatchController.Advance syncs once per frame after the simulation step.
        private void EnsureArt()
        {
            if (catalog != null) return;
            catalog = new UnitArtCatalog(PresentationData.Load());
            markerTexture = new Texture2D(1, 1); markerTexture.SetPixel(0, 0, Color.white); markerTexture.Apply();
            marker = Sprite.Create(markerTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
        }
        public void Sync()
        {
            if (state == null) return;
            EnsureArt();
            alive.Clear();
            foreach (var unit in state.Units.All)
            {
                if (unit.Dead) continue;
                alive.Add(unit.Id);
                // Sun Wukong's transformation and the Celestial Decree change a unit's art in place.
                if (bodies.TryGetValue(unit.Id, out var stale) && (stale.Key != unit.Key || stale.Side != unit.Side))
                {
                    UnitArtCatalog.Release(stale.Visual);
                    bodies.Remove(unit.Id);
                }
                if (!bodies.TryGetValue(unit.Id, out var body))
                {
                    body = new Body { Visual = new GameObject("Unit_" + unit.Id + "_" + unit.Key), Key = unit.Key, Side = unit.Side };
                    body.Visual.transform.SetParent(transform, false);
                    var art = catalog.Resolve(unit.Key, unit.Side);
                    body.Entry = catalog.Entry(unit.Key, unit.Side);
                    body.Fallback = art == null;
                    body.Art = AddSprite(body.Visual.transform, "Art", art ?? marker);
                    body.Health = AddSprite(body.Visual.transform, "Health", marker);
                    bodies.Add(unit.Id, body);
                }
                bool visible = unit.Side == 0 || !visibility.HiddenFromSide(state, unit, 0, state.Elapsed);
                body.Visual.SetActive(visible);
                body.Shown = visible;
                if (!visible) continue;
                // Roadmap 9.3: engaged units lean into what they are hitting. Render-side only —
                // unit.Position is untouched, and the whole body (art, bar) moves together.
                var lean = FormationOffset.For(unit, state.Units.Layout);
                var drawn = new BoardPoint(unit.Position.X + lean.X, unit.Position.Y + lean.Y);
                var p = BoardWorld.ToWorld(drawn);
                p.z = -0.2f;
                body.Visual.transform.localPosition = p;
                var entry = body.Entry;
                bool fallback = body.Fallback;
                float height = fallback ? (unit.Flying ? 0.7f : 0.55f) : entry.height;
                body.Art.transform.localScale = BoardWorld.UprightScale(height);
                body.Art.transform.localPosition = new Vector3(0, fallback ? 0 : -catalog.Data.anchorDown, 0);
                body.Art.flipX = !fallback && entry.mirror;
                int order = Mathf.RoundToInt(drawn.Y * 100) + (unit.Flying ? 10000 : 0);
                body.Art.sortingOrder = order;
                // A timer reset occurs only when the ordinary scheduler lands a hit.
                if (!float.IsNaN(body.LastAttack) && unit.AttackTimer > body.LastAttack + 0.001f)
                    body.FlashUntil = state.Elapsed + 0.12f;
                body.LastAttack = unit.AttackTimer;
                Color side = unit.Side == 0 ? new Color(1f, 0.78f, 0.25f) : new Color(0.95f, 0.28f, 0.28f);
                body.Art.color = state.Elapsed < body.FlashUntil ? new Color(1f, 0.6f, 0.3f) : (fallback ? side : Color.white) * StatusTint(unit, state.Elapsed);
                body.Health.color = side;
                body.Health.sortingOrder = order + 1;
                body.Health.transform.localScale = new Vector3(0.7f * Mathf.Clamp01(unit.Hp / unit.MaxHp), 0.065f, 1);
                body.Health.transform.localPosition = new Vector3(0, fallback ? 0.5f : height - catalog.Data.anchorDown + 0.07f, -0.01f);
            }
            gone.Clear();
            foreach (var pair in bodies) if (!alive.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (int id in gone) Corpse(id);
        }

        /// <summary>
        /// A body whose unit has left the field: it stops being updated, fades and settles for
        /// <see cref="CorpseSeconds"/>, then goes back to the pool. Core drops dead units the frame
        /// they die, so this list is the only record a viewer gets that something fell here.
        /// </summary>
        private void Corpse(int id)
        {
            var body = bodies[id];
            // Never a corpse for a body that was hidden, transformed away, or already released.
            if (body.CorpseUntil <= 0f)
            {
                if (!body.Shown) { UnitArtCatalog.Release(body.Visual); bodies.Remove(id); return; }
                body.CorpseUntil = state.Elapsed + CorpseSeconds;
                body.CorpseScale = body.Art.transform.localScale;
                body.Health.gameObject.SetActive(false);
            }
            float left = body.CorpseUntil - state.Elapsed;
            if (left <= 0f) { UnitArtCatalog.Release(body.Visual); bodies.Remove(id); return; }
            float fade = 1f - Mathf.Clamp01(left / CorpseSeconds);
            var colour = body.Art.color; colour.a = 1f - fade; body.Art.color = colour;
            // Settle: the sprite loses height as it fades, so a corpse reads as down rather than idle.
            var scale = body.CorpseScale;
            body.Art.transform.localScale = new Vector3(scale.x, scale.y * (1f - 0.6f * fade), scale.z);
        }
        /// <summary>
        /// One colour cue per god-effect state, strongest first: untouchable, frenzied, poisoned,
        /// stunned, slowed. Presentation only; reads the Core timers.
        /// </summary>
        public static Color StatusTint(Unit unit, float elapsed)
        {
            if (elapsed < unit.Combat.InvulnerableUntil) return new Color(1f, 1f, 0.6f);
            if (elapsed < unit.Combat.FrenzyUntil) return new Color(1f, 0.5f, 0.5f);
            if (elapsed < unit.Combat.PoisonUntil) return new Color(0.6f, 1f, 0.6f);
            if (elapsed < unit.StunUntil) return new Color(0.6f, 0.6f, 0.6f);
            if (elapsed < unit.SlowUntil) return new Color(0.7f, 0.8f, 1f);
            return Color.white;
        }
        private static SpriteRenderer AddSprite(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            return renderer;
        }
        private void Clear()
        {
            foreach (var body in bodies.Values) { body.CorpseUntil = 0f; body.Visual.SetActive(false); UnitArtCatalog.Release(body.Visual); }
            bodies.Clear();
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        {
            Clear(); catalog?.Dispose(); UnitArtCatalog.Release(marker); UnitArtCatalog.Release(markerTexture);
        }
    }
}
