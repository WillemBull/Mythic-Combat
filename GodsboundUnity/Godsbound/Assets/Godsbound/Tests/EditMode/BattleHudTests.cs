using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Core.Economy;
using Godsbound.Core.Gods;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    public class BattleHudTests
    {
        [Serializable] public class Rotation { public int handSize; public string[] start, used, after; }
        [Serializable] public class RotationFixture { public Rotation[] rotations; }

        /// <summary>
        /// Rewritten for U25. The cards used to fill everything below the hint (band height − 42,
        /// asserted ≥ 110). The bar now also holds the four god tiles, as the browser's does, so
        /// the cards keep at least the browser's <c>.card{min-height:68px}</c> and the god row its
        /// own fixed height — all inside the same fixed band.
        /// </summary>
        [TestCase(393, 852)] [TestCase(1080, 1920)] [TestCase(1280, 720)]
        public void HudHasRoomForCardsGodsAndLabelsAtEveryAspect(int width, int height)
        {
            float scale = BattleHud.HudScale(width, height);
            float logicalHeight = height / scale;
            Assert.That(width / scale, Is.GreaterThanOrEqualTo(BoardViewport.DesignWidthPx));
            Assert.That(logicalHeight * BoardViewport.DefaultTopFraction, Is.GreaterThanOrEqualTo(61.999f));
            var band = BattleHud.BottomBand(width / scale, logicalHeight);
            var layout = BattleHud.Layout(band);
            Assert.That(layout.Cards.height, Is.GreaterThanOrEqualTo(68f));
            Assert.That(layout.Gods.height, Is.EqualTo(BattleHud.GodRowHeight));
            foreach (var r in new[] { layout.Hint, layout.Cards, layout.Gods })
            {
                Assert.That(r.yMin, Is.GreaterThanOrEqualTo(band.yMin));
                Assert.That(r.yMax, Is.LessThanOrEqualTo(band.yMax + 0.001f));
            }
            Assert.That(layout.Hint.yMax, Is.LessThanOrEqualTo(layout.Cards.yMin));
            Assert.That(layout.Cards.yMax, Is.LessThanOrEqualTo(layout.Gods.yMin));
        }

        [Test] public void ArmingCancellingAndCastingGoThroughThePowerSystem()
        {
            var go = new GameObject("PowerAimTest");
            try
            {
                var controller = go.AddComponent<MatchController>();
                controller.Initialize(enableAi: false);
                var hud = go.GetComponent<BattleHud>();
                hud.Bind(controller);
                var state = controller.State;
                controller.Begin();
                state.Objectives.Clock.Advance(30f);
                var horus = state.Gods[0].Slot("horus");
                Assert.That(horus, Is.Not.Null, "Egypt's default four include Horus");
                state.Resources[0] = new Purse(0f, 250f);
                state.Gods[0].Grant("horus");
                string armed = null; int casts = 0;
                hud.PowerArmRequested += k => armed = k;
                hud.PowerCast += (k, applied) => casts++;

                Assert.That(hud.GodTaps.Tap(state, "horus"), Is.EqualTo(GodTapResult.ArmRequested));
                Assert.That(hud.ToggleArm(state, "horus"), Does.Contain("Tap a target"));
                Assert.That(hud.Aim.Armed, Is.EqualTo("horus")); Assert.That(armed, Is.EqualTo("horus"));
                Assert.That(hud.ToggleArm(state, "horus"), Is.EqualTo("Power cancelled."), "tap again cancels");
                Assert.That(hud.Aim.IsArmed, Is.False);
                Assert.That(state.Resources[0].Favor, Is.EqualTo(250f), "arming and cancelling cost nothing");

                hud.ToggleArm(state, "horus");
                var enemy = state.Units.Spawn(1, state.Database.Unit("china", "ji"), new Hex(3, 4));
                Assert.That(hud.CastArmedAt(state, new Hex(0, 4)), Does.Contain("cast"));
                Assert.That(hud.Aim.IsArmed, Is.False, "a cast disarms");
                Assert.That(casts, Is.EqualTo(1));
                Assert.That(state.Resources[0].Favor, Is.EqualTo(250f - state.GodPowerCost(0, horus.Def)).Within(1e-3));
                Assert.That(enemy.Hp, Is.LessThan(enemy.MaxHp), "the row strike landed through PowerSystem");
                Assert.That(go.GetComponent<PowerFx>().ActiveCount, Is.EqualTo(Board.Cols), "one burst per hex of the row");

                // On cooldown: arming is refused by the tap flow, and a stale arm pays nothing.
                Assert.That(hud.GodTaps.Tap(state, "horus"), Is.EqualTo(GodTapResult.Recharging));
                hud.Aim.Toggle("horus");
                Assert.That(hud.CastArmedAt(state, new Hex(0, 4)), Is.EqualTo("Power unavailable."));
                Assert.That(casts, Is.EqualTo(1));

                // Isis picks an enemy god.
                state.Gods[0].Grant("isis");
                state.Gods[1].Grant(state.Gods[1].Selected[0].Key);
                var target = state.Gods[1].Selected[0];
                hud.ToggleArm(state, "isis");
                Assert.That(PowerAim.LockableEnemyGods(state).Select(g => g.Key), Is.EqualTo(new[] { target.Key }));
                Assert.That(hud.CastArmedOnGod(state, state.Gods[1].Selected[1].Key), Does.Contain("not unlocked"));
                Assert.That(hud.Aim.Armed, Is.EqualTo("isis"), "an illegal pick keeps the power armed");
                Assert.That(hud.CastArmedOnGod(state, target.Key), Does.Contain("cast"));
                Assert.That(target.LockedUntil, Is.GreaterThan(state.Elapsed));
                Assert.That(PowerAim.LockableEnemyGods(state), Is.Empty, "a locked god cannot be locked again");

                hud.ToggleArm(state, "horus");
                controller.NewMatch();
                Assert.That(hud.Aim.IsArmed, Is.False, "reset disarms");
                Assert.That(go.GetComponent<PowerFx>().ActiveCount, Is.Zero, "reset clears the effects");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test] public void FootprintsFollowTheExportedShapes()
        {
            var db = Godsbound.Data.GameDataLoader.Load();
            var state = new Godsbound.Core.Match.MatchState(db);
            var at = new Hex(4, 5);
            Assert.That(PowerAim.Footprint(state, "horus", at).All(h => h.R == 5), Is.True);
            Assert.That(PowerAim.Footprint(state, "horus", at).Count(), Is.EqualTo(Board.Cols));
            Assert.That(PowerAim.Footprint(state, "ra", at).All(h => h.C == 4), Is.True);
            Assert.That(PowerAim.Footprint(state, "nephthys", at).All(h => Board.IsNeutralRow(h.R)), Is.True);
            int flood = (int)db.Powers.Value("floodRadius");
            Assert.That(PowerAim.Footprint(state, "longwang", at).Max(h => Hex.Distance(h, at)), Is.EqualTo(flood));
            Assert.That(PowerAim.Footprint(state, "bastet", at), Is.EqualTo(new[] { at }));
        }

        [Test] public void EveryPortedPowerHasAHint()
        {
            var data = PresentationData.Load();
            var db = Godsbound.Data.GameDataLoader.Load();
            foreach (var f in new[] { "egypt", "china" })
                foreach (var g in db.Roster(f))
                    Assert.That(data.HintFor(g.key), Is.Not.Empty, g.key);
        }

        [Test] public void StatusTintsFollowTheTimers()
        {
            var db = Godsbound.Data.GameDataLoader.Load();
            var u = new Godsbound.Core.Units.Unit(1, 0, db.Unit("egypt", "spear"), new Hex(0, 0), HexLayout.Unit());
            Assert.That(UnitsView.StatusTint(u, 10f), Is.EqualTo(Color.white));
            u.SlowUntil = 12f;
            Assert.That(UnitsView.StatusTint(u, 10f), Is.Not.EqualTo(Color.white));
            Assert.That(UnitsView.StatusTint(u, 12f), Is.EqualTo(Color.white), "cues end with their timers");
            u.Combat.InvulnerableUntil = 12f;
            Assert.That(UnitsView.StatusTint(u, 10f), Is.EqualTo(new Color(1f, 1f, 0.6f)), "untouchable outranks slowed");
        }

        [Test] public void HandRotationMatchesBrowserUseCard()
        {
            var fixture = JsonUtility.FromJson<RotationFixture>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Godsbound/Tests/EditMode/Fixtures/deployment_reference.json")));
            Assert.That(fixture.rotations, Is.Not.Empty);
            Assert.That(PresentationData.Load().handSize, Is.EqualTo(fixture.rotations[0].handSize));
            foreach (var r in fixture.rotations)
            {
                var deck = r.start.ToList();
                for (int i = 0; i < r.used.Length; i++)
                {
                    BattleHud.RotateHand(deck, r.used[i], r.handSize);
                    Assert.That(string.Join(",", deck), Is.EqualTo(r.after[i]), string.Join(",", r.start) + " step " + i);
                }
            }
            var hidden = new[] { "a", "b", "c", "d", "e", "f", "g" }.ToList();
            BattleHud.RotateHand(hidden, "g", 6);
            Assert.That(string.Join(",", hidden), Is.EqualTo("a,b,c,d,e,f,g"), "a card waiting beyond the hand cannot be played");
        }

        [Test] public void GodTilesMirrorGodStateAndTheBarNeverMoves()
        {
            var go = new GameObject("GodTileTest");
            try
            {
                var controller = go.AddComponent<MatchController>();
                controller.Initialize(enableAi: false);
                var hud = go.GetComponent<BattleHud>();
                hud.Bind(controller); // OnEnable does not run in EditMode
                var state = controller.State;
                controller.Begin();
                var band = BattleHud.BottomBand(393, 852);
                var before = BattleHud.Layout(band);
                hud.EnsureDeck(state);
                int deckBefore = hud.Deck.Count;
                Assert.That(state.Gods[0].Selected.Count, Is.EqualTo(GodState.SelectionSize));

                var first = state.Gods[0].Selected[0];
                int cost = state.GodUnlockCost(0, first.Def);
                state.Resources[0] = new Purse(0f, cost + 5);
                Assert.That(BattleHud.TileView(state, hud.GodTaps, 0).Unlocked, Is.False);
                Assert.That(BattleHud.TileView(state, hud.GodTaps, 0).Affordable, Is.True);

                Assert.That(hud.GodTaps.Tap(state, first.Key), Is.EqualTo(GodTapResult.Previewed));
                Assert.That(state.Resources[0].Favor, Is.EqualTo(cost + 5), "the preview tap pays nothing");
                Assert.That(BattleHud.TileView(state, hud.GodTaps, 0).Previewed, Is.True);
                Assert.That(BattleHud.GodHint(state, GodTapResult.Previewed, first.Key), Does.Contain("Tap again to unlock"));

                Assert.That(hud.GodTaps.Tap(state, first.Key), Is.EqualTo(GodTapResult.Unlocked));
                Assert.That(state.Resources[0].Favor, Is.EqualTo(5f));
                var tile = BattleHud.TileView(state, hud.GodTaps, 0);
                Assert.That(tile.Unlocked && !tile.Previewed, Is.True);
                Assert.That(tile.Label, Does.Contain(first.Def.power));
                Assert.That(hud.Deck.Count, Is.EqualTo(deckBefore + 1), "the myth joined the rotation");
                Assert.That(hud.Deck.Last(), Is.EqualTo(first.Def.myth));
                Assert.That(hud.Deck.Take(hud.HandSize), Does.Not.Contain(first.Def.myth), "it waits beyond the visible hand");

                // Unlocked, ready, but unaffordable power; then affordable -> the U30 arming seam.
                Assert.That(hud.GodTaps.Tap(state, first.Key), Is.EqualTo(GodTapResult.CannotAffordPower));
                state.Resources[0] = new Purse(0f, 250f);
                Assert.That(hud.GodTaps.Tap(state, first.Key), Is.EqualTo(GodTapResult.ArmRequested));
                Assert.That(state.Resources[0].Favor, Is.EqualTo(250f), "arming casts nothing yet");
                first.CooldownUntil = state.Elapsed + 3f;
                Assert.That(hud.GodTaps.Tap(state, first.Key), Is.EqualTo(GodTapResult.Recharging));
                Assert.That(BattleHud.TileView(state, hud.GodTaps, 0).Recharging, Is.True);

                // Too poor to confirm: the preview clears and nothing is paid.
                var second = state.Gods[0].Selected[1];
                state.Resources[0] = new Purse(0f, 0f);
                Assert.That(hud.GodTaps.Tap(state, second.Key), Is.EqualTo(GodTapResult.Previewed));
                Assert.That(hud.GodTaps.Tap(state, second.Key), Is.EqualTo(GodTapResult.CannotAffordUnlock));
                Assert.That(hud.GodTaps.Preview, Is.Null);
                Assert.That(hud.GodTaps.Tap(state, second.Key), Is.EqualTo(GodTapResult.Previewed));

                var after = BattleHud.Layout(band);
                Assert.That(after.Hint, Is.EqualTo(before.Hint));
                Assert.That(after.Cards, Is.EqualTo(before.Cards));
                Assert.That(after.Gods, Is.EqualTo(before.Gods), "the bar's rows are identical before and after unlocking");

                controller.NewMatch();
                Assert.That(hud.GodTaps.Preview, Is.Null, "reset clears the preview");
                for (int i = 0; i < GodState.SelectionSize; i++)
                    Assert.That(BattleHud.TileView(state, hud.GodTaps, i).Unlocked, Is.False);
                hud.EnsureDeck(state);
                Assert.That(hud.Deck.Count, Is.EqualTo(deckBefore), "reset deals the starting deck without myths");
                Assert.That(BattleHud.Layout(band).Gods, Is.EqualTo(before.Gods));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        [TestCase(393, 852)] [TestCase(1080, 1920)] [TestCase(1280, 720)]
        public void EveryHexRoundTripsAndControlBandsAreExcluded(int width, int height)
        {
            var go = new GameObject("CameraTest");
            // Render to a texture of the size under test. Without one, an editor camera's pixelRect
            // is clamped to the Game view, so this test quietly measured the editor window instead
            // of the resolution named in the TestCase — and failed at 1080x1920 whenever someone
            // set the Game view to the phone size the game is actually designed for.
            RenderTexture target = null;
            try
            {
                target = new RenderTexture(width, height, 0);
                var cam = go.AddComponent<Camera>(); cam.orthographic = true;
                cam.targetTexture = target;
                cam.pixelRect = new Rect(0, 0, width, height);
                var framing = BoardViewport.Frame(BoardWorld.BoardBounds(BoardWorld.UnitLayout()), (float)width / height,
                    BoardViewport.DefaultTopFraction, BoardViewport.DefaultBottomFraction);
                cam.orthographicSize = framing.OrthographicSize;
                cam.transform.position = new Vector3(framing.Center.x, framing.Center.y, -10);
                foreach (var h in Board.AllCells())
                {
                    var point = cam.WorldToScreenPoint(BoardWorld.CenterOf(h, BoardWorld.UnitLayout()));
                    Assert.That(BattleHud.TryHex(cam, point, out var picked), Is.True, h.ToString());
                    Assert.That(picked, Is.EqualTo(h));
                }
                Assert.That(BattleHud.TryHex(cam, new Vector2(width / 2f, 1), out _), Is.False);
                Assert.That(BattleHud.TryHex(cam, new Vector2(width / 2f, height - 1), out _), Is.False);
                var band = BattleHud.BottomBand(width, height);
                Assert.That(band.height, Is.EqualTo(height * BoardViewport.DefaultBottomFraction));
                Assert.That(band.yMax, Is.EqualTo(height).Within(0.001));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
