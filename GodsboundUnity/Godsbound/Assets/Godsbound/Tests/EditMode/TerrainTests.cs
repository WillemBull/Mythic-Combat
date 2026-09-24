using System.Linq;
using NUnit.Framework;
using Godsbound.Core;

namespace Godsbound.Tests
{
    /// <summary>Terrain table, base map, and route legality against the browser reference.</summary>
    public class TerrainTests
    {
        [Test]
        public void TerrainTable_MatchesReferenceForEveryCode()
        {
            var refTerrain = HexReference.Data.terrain;
            Assert.AreEqual(refTerrain.Length, TerrainTable.AllTypes.Count(),
                "terrain code count");

            foreach (var t in refTerrain)
            {
                var type = TerrainTable.FromCode(t.code[0]);
                var info = TerrainTable.Info(type);

                Assert.AreEqual(t.code[0], info.Code, $"code for {t.name}");
                Assert.AreEqual(t.name, info.Name, $"name for {t.code}");
                Assert.AreEqual(t.speed, info.Speed, 1e-6f, $"speed for {t.code}");
                Assert.AreEqual(t.block, info.Block, $"block flag for {t.code}");
                Assert.AreEqual(t.cover, info.Cover, 1e-6f, $"cover for {t.code}");
                Assert.AreEqual(t.highground, info.HighGround, $"high ground flag for {t.code}");
            }
        }

        /// <summary>Plains and Desert are faction-themed skins of the same tile.</summary>
        [Test]
        public void PlainsAndDesert_AreMechanicallyIdentical()
        {
            var p = TerrainTable.Info(TerrainType.Plains);
            var d = TerrainTable.Info(TerrainType.Desert);
            Assert.AreEqual(p.Speed, d.Speed, 1e-6f);
            Assert.AreEqual(p.Block, d.Block);
            Assert.AreEqual(p.Cover, d.Cover, 1e-6f);
            Assert.AreEqual(p.HighGround, d.HighGround);
        }

        [Test]
        public void UnknownTerrainCode_ThrowsRatherThanDefaulting()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => TerrainTable.FromCode('X'));
        }

        [Test]
        public void BaseMap_MatchesReference()
        {
            var r = HexReference.Data;
            Assert.AreEqual(r.tmapInitial.Length, TerrainMap.Initial.Length, "base map row count");
            for (int i = 0; i < r.tmapInitial.Length; i++)
                Assert.AreEqual(r.tmapInitial[i], TerrainMap.Initial[i], $"base map row {i}");
        }

        [Test]
        public void LiveMap_StartsAtEveryReferenceCell()
        {
            var map = new TerrainMap();
            foreach (var cell in HexReference.Data.cells)
                Assert.AreEqual(cell.terrain[0], map.CodeAt(cell.c, cell.r),
                    $"terrain at ({cell.c},{cell.r})");
        }

        /// <summary>
        /// Passability for every terrain code, against values the browser game itself
        /// produced by planting each code on a scratch hex. The base map contains only
        /// Plains and Desert, so the per-cell sweep below can never exercise a mountain —
        /// and blocking terrain is the entire point of the rule. This covers it.
        /// </summary>
        [Test]
        public void Passability_MatchesReferenceForEveryTerrainCode()
        {
            var expected = HexReference.Data.terrainPassability;
            Assert.IsNotNull(expected, "fixture is missing terrainPassability — re-run the exporter");
            Assert.AreEqual(TerrainTable.AllTypes.Count(), expected.Length, "per-code passability count");

            var map = new TerrainMap();
            foreach (var e in expected)
            {
                map[4, 4] = TerrainTable.FromCode(e.code[0]);
                Assert.AreEqual(e.ground, map.Passable(4, 4, false), $"ground passability on '{e.code}'");
                Assert.AreEqual(e.flying, map.Passable(4, 4, true), $"flying passability on '{e.code}'");
            }
        }

        [Test]
        public void Passability_MatchesReferenceForBothLayers()
        {
            var map = new TerrainMap();
            foreach (var cell in HexReference.Data.cells)
            {
                Assert.AreEqual(cell.passableGround, map.Passable(cell.c, cell.r, false),
                    $"ground passability at ({cell.c},{cell.r})");
                Assert.AreEqual(cell.passableFlying, map.Passable(cell.c, cell.r, true),
                    $"flying passability at ({cell.c},{cell.r})");
            }
        }

        [Test]
        public void Mountains_BlockGroundButNotFlying()
        {
            var map = new TerrainMap();
            map[4, 4] = TerrainType.Mountain;
            Assert.IsFalse(map.Passable(4, 4, false), "mountain must block ground");
            Assert.IsTrue(map.Passable(4, 4, true), "mountain must not block flying");
            Assert.IsFalse(map.RouteOk(4, 4, false), "mountain must fail ground route legality");
            Assert.IsTrue(map.RouteOk(4, 4, true), "mountain must pass flying route legality");
            Assert.IsTrue(map.BlocksSightAt(4, 4), "mountain must block ranged line of sight");
        }

        [Test]
        public void EveryNonMountainTerrain_IsGroundRoutable()
        {
            var map = new TerrainMap();
            foreach (var type in TerrainTable.AllTypes)
            {
                map[4, 4] = type;
                bool expected = type != TerrainType.Mountain;
                Assert.AreEqual(expected, map.RouteOk(4, 4, false),
                    $"ground route legality on {TerrainTable.Info(type).Name}");
                Assert.IsTrue(map.RouteOk(4, 4, true),
                    $"flying route legality on {TerrainTable.Info(type).Name}");
            }
        }

        [Test]
        public void OffBoardHexes_AreNeverRoutable()
        {
            var map = new TerrainMap();
            Assert.IsFalse(map.RouteOk(-1, 0, false));
            Assert.IsFalse(map.RouteOk(-1, 0, true), "flying does not make off-board legal");
            Assert.IsFalse(map.RouteOk(Board.Cols, 0, true));
            Assert.IsFalse(map.RouteOk(0, Board.Rows, true));
        }

        /// <summary>Standing buildings block routes; destroyed buildings do not.</summary>
        [Test]
        public void StandingBuildings_BlockRoutes_DestroyedOnesDoNot()
        {
            var map = new TerrainMap();
            var occupied = new Hex(2, 3);

            StandingBuildingAt standing = (c, r) => c == occupied.C && r == occupied.R;
            StandingBuildingAt rubble = (c, r) => false;

            Assert.IsFalse(map.RouteOk(occupied, false, standing),
                "a standing building must block a ground route");
            Assert.IsFalse(map.RouteOk(occupied, true, standing),
                "a standing building must block a flying route too");
            Assert.IsTrue(map.RouteOk(occupied, false, rubble),
                "a destroyed building must not block a route");
            Assert.IsTrue(map.RouteOk(occupied, true, rubble));
        }

        [Test]
        public void Reset_RestoresTheBaseMap()
        {
            var map = new TerrainMap();
            map[0, 0] = TerrainType.Mountain;
            map[8, 12] = TerrainType.Water;
            map.Reset();
            Assert.AreEqual(TerrainMap.Initial[0][0], map.CodeAt(0, 0));
            Assert.AreEqual(TerrainMap.Initial[12][8], map.CodeAt(8, 12));
        }

        [Test]
        public void OutOfRangeAccess_Throws()
        {
            var map = new TerrainMap();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => { var _ = map[-1, 0]; });
            Assert.Throws<System.ArgumentOutOfRangeException>(() => { map[0, Board.Rows] = TerrainType.Road; });
        }
    }
}
