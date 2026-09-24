using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Buildings;
using Godsbound.Core.Combat;
using Godsbound.Core.Match;
using Godsbound.Core.Movement;
using Godsbound.Core.Units;
using Godsbound.Data;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>U35: floating damage numbers, corpses, the engaged lean and building damage states.</summary>
    public class ReadabilityTests
    {
        private GameObject root;
        private MatchState state;
        private DamageNumbers numbers;
        private UnitsView units;

        [SetUp] public void Setup()
        {
            state = new MatchState(GameDataLoader.Load());
            root = new GameObject("ReadabilityTest");
            numbers = root.AddComponent<DamageNumbers>(); numbers.Bind(state);
            units = root.AddComponent<UnitsView>(); units.Bind(state);
        }
        [TearDown] public void Cleanup() => Object.DestroyImmediate(root);

        private Unit Spawn(int side, string faction, string key, Hex at) =>
            state.Units.Spawn(side, state.Database.Unit(faction, key), at);

        [Test] public void EveryHitPushesOneNumberWithNoDamageFloor()
        {
            var attacker = Spawn(0, "egypt", "spear", new Hex(4, 6));
            var target = Spawn(1, "china", "dao", new Hex(4, 7));
            int raised = 0; state.Combat.Hit += _ => raised++;
            float dealt = state.Combat.DealDamage(attacker, target, state.Elapsed);
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(dealt, Is.GreaterThan(0f));
            Assert.That(numbers.Count, Is.EqualTo(1));
            // No threshold: even damage that rounds to nothing is still shown, as "-1".
            Assert.That(DamageNumberList.Text(0.4f), Is.EqualTo("-1"));
            Assert.That(DamageNumberList.Text(12.6f), Is.EqualTo("-13"));
        }

        [Test] public void CapHoldsUnderFortyHitsInAFrameAndKeepsTheOldest()
        {
            var attacker = Spawn(0, "egypt", "spear", new Hex(4, 6));
            var target = Spawn(1, "china", "dao", new Hex(4, 7));
            for (int i = 0; i < 40; i++)
            {
                state.Combat.DealDamage(attacker, target, state.Elapsed);
                target.Heal(target.MaxHp);             // keep it alive; the cap is what is on trial
            }
            Assert.That(numbers.Count, Is.EqualTo(DamageNumberList.Cap));
            numbers.Sync();
            Assert.That(numbers.VisibleCount, Is.EqualTo(DamageNumberList.Cap));
            // Dropped, not evicted: everything live was pushed at the same instant, and the first
            // one is still there.
            Assert.That(numbers.Live.All(e => e.At == 0f));
        }

        [Test] public void NumbersRiseFadeAndExpire()
        {
            var list = new DamageNumberList();
            Assert.That(list.Add(1f, 2f, 10f, 1, 0f), Is.True);
            var entry = list.Live[0];
            Assert.That(DamageNumberList.Progress(entry, 0f), Is.EqualTo(0f));
            Assert.That(DamageNumberList.Progress(entry, DamageNumberList.Life * 0.5f), Is.EqualTo(0.5f).Within(0.0001));
            Assert.That(DamageNumberList.Progress(entry, 99f), Is.EqualTo(1f));
            list.Expire(DamageNumberList.Life - 0.01f); Assert.That(list.Count, Is.EqualTo(1));
            list.Expire(DamageNumberList.Life); Assert.That(list.Count, Is.Zero);
            for (int i = 0; i < DamageNumberList.Cap; i++) list.Add(0f, 0f, 5f, 0, 1f);
            Assert.That(list.Add(0f, 0f, 5f, 0, 1f), Is.False);
            list.Clear(); Assert.That(list.Count, Is.Zero);
        }

        [Test] public void BuildingHitsAreNumberedAtTheWallAndResetClearsTheBoard()
        {
            var attacker = Spawn(0, "egypt", "spear", new Hex(4, 6));
            var wall = state.Buildings.All.First(b => b.Side == 1);
            state.Combat.DealDamage(attacker, wall, state.Elapsed);
            Assert.That(numbers.Count, Is.EqualTo(1));
            var entry = numbers.Live[0];
            Assert.That(entry.Side, Is.EqualTo(1));
            var a = state.Units.Layout.Center(wall.HexA); var b = state.Units.Layout.Center(wall.HexB);
            Assert.That(entry.X, Is.EqualTo((a.X + b.X) * 0.5f).Within(0.0001));
            numbers.Bind(new MatchState(state.Database));
            Assert.That(numbers.Count, Is.Zero);
            numbers.Sync(); Assert.That(numbers.VisibleCount, Is.Zero);
        }

        [Test] public void DeadBodiesLingerBrieflyThenGo()
        {
            var unit = Spawn(1, "china", "dao", new Hex(4, 7));
            units.Sync(); Assert.That(units.Count, Is.EqualTo(1));
            unit.Kill(); units.Sync();
            Assert.That(units.Count, Is.Zero, "the dead are not counted among the living");
            Assert.That(units.CorpseCount, Is.EqualTo(1));
            Assert.That(units.BodyOf(unit.Id), Is.Not.Null);
            state.Objectives.Clock.Advance(UnitsView.CorpseSeconds * 0.5f); units.Sync();
            Assert.That(units.CorpseCount, Is.EqualTo(1));
            state.Objectives.Clock.Advance(UnitsView.CorpseSeconds); units.Sync();
            Assert.That(units.CorpseCount, Is.Zero);
            Assert.That(units.BodyOf(unit.Id), Is.Null);
        }

        [Test] public void AUnitThatDiesUnseenLeavesNoCorpse()
        {
            var unit = Spawn(1, "china", "dao", new Hex(4, 7));
            unit.Combat.Invisible = true; units.Sync();
            unit.Kill(); units.Sync();
            Assert.That(units.CorpseCount, Is.Zero);
            Assert.That(units.BodyOf(unit.Id), Is.Null);
        }

        [Test] public void EngagedUnitsLeanTowardWhatTheyAreHitting()
        {
            var layout = state.Units.Layout;
            var attacker = Spawn(0, "egypt", "spear", new Hex(4, 6));
            var target = Spawn(1, "china", "dao", new Hex(4, 8));
            Assert.That(FormationOffset.For(attacker, layout).X, Is.EqualTo(0f));
            Assert.That(FormationOffset.For(attacker, layout).Y, Is.EqualTo(0f), "an idle unit stands on its hex");
            attacker.Engagement = new MovementContact(target);
            var lean = FormationOffset.For(attacker, layout);
            float length = Mathf.Sqrt(lean.X * lean.X + lean.Y * lean.Y);
            Assert.That(length, Is.EqualTo(FormationOffset.Lean * layout.Hex).Within(0.0001));
            Assert.That(Mathf.Sign(lean.Y), Is.EqualTo(Mathf.Sign(target.Position.Y - attacker.Position.Y)));
            // A dead target is not leaned into, and neither is one that has been let go.
            target.Kill(); Assert.That(FormationOffset.For(attacker, layout).Y, Is.EqualTo(0f));
            attacker.Engagement = null; Assert.That(FormationOffset.For(attacker, layout).X, Is.EqualTo(0f));
        }

        [Test] public void LeaningIntoAWallAimsAtItsMiddle()
        {
            var layout = state.Units.Layout;
            var wall = state.Buildings.All.First(b => b.Side == 1);
            var attacker = Spawn(0, "egypt", "spear", new Hex(0, 0));
            attacker.Engagement = new MovementContact(wall);
            var lean = FormationOffset.For(attacker, layout);
            var a = layout.Center(wall.HexA); var b = layout.Center(wall.HexB);
            var mid = new BoardPoint((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
            float dx = mid.X - attacker.Position.X, dy = mid.Y - attacker.Position.Y;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            Assert.That(lean.X, Is.EqualTo(dx / length * FormationOffset.Lean * layout.Hex).Within(0.0001));
            Assert.That(lean.Y, Is.EqualTo(dy / length * FormationOffset.Lean * layout.Hex).Within(0.0001));
        }

        [Test] public void BuildingsCrackAtHalfAndSmokeAtAQuarter()
        {
            Assert.That(BuildingDamageState.For(100f, 100f), Is.EqualTo(BuildingWear.Whole));
            Assert.That(BuildingDamageState.For(50f, 100f), Is.EqualTo(BuildingWear.Whole), "the threshold is below half");
            Assert.That(BuildingDamageState.For(49f, 100f), Is.EqualTo(BuildingWear.Cracked));
            Assert.That(BuildingDamageState.For(25f, 100f), Is.EqualTo(BuildingWear.Cracked));
            Assert.That(BuildingDamageState.For(24f, 100f), Is.EqualTo(BuildingWear.Burning));
            var wall = state.Buildings.All.First();
            wall.TakeDamage(wall.MaxHp * 0.6f);
            Assert.That(BuildingDamageState.For(wall), Is.EqualTo(BuildingWear.Cracked));
            // Deterministic and in range, so cracks stay put frame to frame.
            for (int i = 0; i < BuildingDamageState.Cracks; i++)
                for (int c = 0; c < 5; c++)
                {
                    float j = BuildingDamageState.Jitter(wall, i, c);
                    Assert.That(j, Is.InRange(0f, 1f));
                    Assert.That(BuildingDamageState.Jitter(wall, i, c), Is.EqualTo(j));
                }
            var other = state.Buildings.All.Last();
            Assert.That(BuildingDamageState.Jitter(other, 0, 0), Is.Not.EqualTo(BuildingDamageState.Jitter(wall, 0, 0)));
            wall.TakeDamage(wall.Hp); Assert.That(BuildingDamageState.For(wall), Is.EqualTo(BuildingWear.Whole),
                "rubble has its own drawing; it is not a burning building");
        }

        [Test] public void DamagedWallsShowCracksAndSmokeInTheView()
        {
            var view = root.AddComponent<BuildingsView>();
            // Named factions, because the damage states are drawn over the ARTWORK: a bare
            // MatchState names no faction, the sprites do not resolve, and only the fallback
            // footprint markers are left to draw.
            view.Bind(state, side => side == 0 ? "egypt" : "china");
            var wall = state.Buildings.All.First(b => b.Side == 0);
            var body = view.BodyOf(wall);
            Assert.That(Shown(body, "Crack"), Is.Zero);
            wall.TakeDamage(wall.MaxHp * 0.6f); view.Sync();
            Assert.That(Shown(body, "Crack"), Is.EqualTo(BuildingDamageState.Cracks));
            Assert.That(Shown(body, "Smoke"), Is.Zero);
            wall.TakeDamage(wall.MaxHp * 0.2f); view.Sync();
            Assert.That(Shown(body, "Smoke"), Is.EqualTo(BuildingDamageState.Smokes));
        }

        private static int Shown(Transform body, string prefix)
        {
            int n = 0;
            for (int i = 0; i < body.childCount; i++)
            {
                var child = body.GetChild(i);
                if (child.name.StartsWith(prefix) && child.gameObject.activeSelf) n++;
            }
            return n;
        }
    }
}
