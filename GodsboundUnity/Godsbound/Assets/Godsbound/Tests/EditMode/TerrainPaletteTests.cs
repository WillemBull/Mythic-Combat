using NUnit.Framework;
using UnityEngine;
using Godsbound.Core;
using Godsbound.Presentation;

namespace Godsbound.Tests
{
    /// <summary>Board colours against the browser game's TINFO fills.</summary>
    public class TerrainPaletteTests
    {
        [Test]
        public void PaletteMatchesReferenceForEveryTerrainCode()
        {
            foreach (var want in HexReference.Data.terrain)
            {
                Assert.IsNotNull(want.fill, $"fixture has no fill for '{want.code}' — re-run the exporter");
                var type = TerrainTable.FromCode(want.code[0]);
                Assert.AreEqual(want.fill, TerrainPalette.FillHex(type), $"fill for {want.name}");
                Assert.AreEqual(want.fill2, TerrainPalette.Fill2Hex(type), $"fill2 for {want.name}");
            }
        }

        [Test]
        public void EveryColourParses()
        {
            foreach (var t in TerrainTable.AllTypes)
            {
                Assert.AreNotEqual(default(Color), TerrainPalette.Fill(t), $"fill for {t}");
                Assert.AreNotEqual(default(Color), TerrainPalette.Fill2(t), $"fill2 for {t}");
            }
        }

        [Test]
        public void AlternatingShadeChequersNeighbouringCells()
        {
            var t = TerrainType.Plains;
            Assert.AreEqual(TerrainPalette.Fill(t), TerrainPalette.For(t, 0, 0));
            Assert.AreEqual(TerrainPalette.Fill2(t), TerrainPalette.For(t, 1, 0));
            Assert.AreEqual(TerrainPalette.Fill2(t), TerrainPalette.For(t, 0, 1));
            Assert.AreEqual(TerrainPalette.Fill(t), TerrainPalette.For(t, 1, 1));
        }
    }
}
