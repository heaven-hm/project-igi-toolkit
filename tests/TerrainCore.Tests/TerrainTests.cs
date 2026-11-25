using System;
using System.IO;
using System.Text;
using IGIEditor;
using Xunit;

namespace TerrainCore.Tests
{
    public class HmpFormatTests
    {
        private static byte[] BuildSample()
        {
            HmpFile file = new HmpFile();
            TerrainHeightmap first = new TerrainHeightmap(8);
            first.Fill(70);
            first.Set(4, 4, 100);
            file.SetSlot(2, first);

            TerrainHeightmap second = new TerrainHeightmap(16);
            second.Fill(TerrainHeightmap.NeutralValue);
            file.GetSlot(7).Contents = 0x12345678;
            file.GetSlot(7).Unknown = 3;
            file.GetSlot(7).Padding = new byte[] { 1, 2, 3 };
            file.SetSlot(7, second);
            return file.Save();
        }

        [Fact]
        public void Save_WritesFixedHeaderBlockThenPayloads()
        {
            byte[] data = BuildSample();
            // 256 slots x 12 bytes + 9x9 + 17x17 samples
            Assert.Equal(256 * 12 + 81 + 289, data.Length);
            Assert.Equal(0, data[0]);                       // empty slot 0
            Assert.Equal(8, data[2 * 12 + 8]);               // slot 2 size
            Assert.Equal(16, data[7 * 12 + 8]);              // slot 7 size
            Assert.Equal(0x78, data[7 * 12 + 0]);            // little-endian contents
            Assert.Equal(3, data[7 * 12 + 4]);
            Assert.Equal(new byte[] { 1, 2, 3 }, new[] { data[7 * 12 + 5], data[7 * 12 + 6], data[7 * 12 + 7] });
            Assert.Equal(unchecked((byte)100), data[256 * 12 + 4 * 9 + 4]);
        }

        [Fact]
        public void Load_RoundTripsEverySlotByteForByte()
        {
            byte[] data = BuildSample();
            HmpFile loaded = HmpFile.Load(data);
            Assert.Equal(data, loaded.Save());
            Assert.Null(loaded.GetSlot(0).Map);
            Assert.Equal(0, loaded.GetSlot(0).Size);
            Assert.Equal(8, loaded.GetSlot(2).Size);
            Assert.Equal(100, loaded.GetSlot(2).Map.Get(4, 4));
            Assert.Equal(70, loaded.GetSlot(2).Map.Get(0, 0));
            Assert.Equal(9, loaded.GetSlot(2).Map.Side);
            Assert.Equal(289, loaded.GetSlot(7).Map.Length);
            Assert.Equal(0x12345678u, loaded.GetSlot(7).Contents);
            Assert.Equal(3, loaded.GetSlot(7).Unknown);
        }

        [Fact]
        public void Load_UsesSignedHeightSamples()
        {
            byte[] data = new byte[256 * 12 + 4];
            data[8] = 1;                      // 2x2 map in slot 0
            data[256 * 12 + 0] = 0x80;        // -128
            data[256 * 12 + 1] = 0x7f;        // +127
            data[256 * 12 + 2] = 0x40;        // neutral
            data[256 * 12 + 3] = 0x00;        // sample 0
            TerrainHeightmap map = HmpFile.Load(data).GetSlot(0).Map;
            Assert.Equal(-128, map.Get(0, 0));
            Assert.Equal(127, map.Get(1, 0));
            Assert.Equal(64, map.Get(0, 1));
            Assert.Equal(0, map.Get(1, 1));
            Assert.Equal(0.0, map.HeightMeters(0, 1));  // (64 - 64) / 16
            Assert.Equal(-4.0, map.HeightMeters(1, 1)); // (0 - 64) / 16
        }

        [Fact]
        public void Load_RejectsTruncatedAndNonPowerOfTwoSlots()
        {
            Assert.Throws<FormatException>(() => HmpFile.Load(new byte[10]));
            byte[] badSize = new byte[256 * 12];
            badSize[8] = 3;
            Assert.Throws<FormatException>(() => HmpFile.Load(badSize));
            byte[] truncated = new byte[256 * 12 + 4];
            truncated[8] = 8; // needs 81 bytes
            Assert.Throws<FormatException>(() => HmpFile.Load(truncated));
            byte[] trailing = new byte[256 * 12 + 5];
            trailing[8] = 1;
            Assert.Throws<FormatException>(() => HmpFile.Load(trailing));
        }

        [Fact]
        public void HeightMap_RejectsInvalidDimensions()
        {
            Assert.Throws<ArgumentException>(() => new TerrainHeightmap(3));
            Assert.Throws<ArgumentException>(() => new TerrainHeightmap(8192));
        }
    }

    public class HeightmapIoTests
    {
        [Fact]
        public void Pgm_RoundTripsAllSignedSamples()
        {
            TerrainHeightmap map = new TerrainHeightmap(4);
            for (int i = 0; i < map.Length; i++) map.Samples[i] = TerrainMath.ClampToSbyte(i * 7 - 60);
            byte[] pgm = HeightmapIo.ExportPgm(map);
            Assert.StartsWith("P5\n5 5\n255\n", Encoding.ASCII.GetString(pgm, 0, 12));
            TerrainHeightmap loaded = HeightmapIo.ImportPgm(pgm);
            Assert.Equal(map.Samples, loaded.Samples);
        }

        [Fact]
        public void Pgm_NeutralIsMidGrayAndCommentsAreAllowed()
        {
            string header = "P5\n# generated\n3 3\n255\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] data = new byte[headerBytes.Length + 9];
            headerBytes.CopyTo(data, 0);
            for (int i = headerBytes.Length; i < data.Length; i++) data[i] = 128;
            TerrainHeightmap map = HeightmapIo.ImportPgm(data);
            Assert.Equal(3, map.Side);
            Assert.All(map.Samples, s => Assert.Equal(TerrainHeightmap.NeutralValue, s));
        }

        [Fact]
        public void Raw8_RoundTripsUsingCurrentMapSize()
        {
            TerrainHeightmap map = new TerrainHeightmap(8);
            map.Fill(-10);
            byte[] raw = HeightmapIo.ExportRaw8(map);
            Assert.Equal(81, raw.Length);
            Assert.Equal(map.Samples, HeightmapIo.ImportRaw8(raw, 8).Samples);
            Assert.Throws<FormatException>(() => HeightmapIo.ImportRaw8(raw, 16));
        }

        [Fact]
        public void Pgm_RejectsUnsupportedFormats()
        {
            Assert.Throws<FormatException>(() => HeightmapIo.ImportPgm(Encoding.ASCII.GetBytes("P6\n3 3\n255\n")));
            Assert.Throws<NotSupportedException>(() => HeightmapIo.ImportPgm(Encoding.ASCII.GetBytes("P5\n3 3\n15\n")));
        }
    }

    public class TerrainSculptTests
    {
        private static TerrainHeightmap Flat()
        {
            TerrainHeightmap map = new TerrainHeightmap(16);
            map.Fill(TerrainHeightmap.NeutralValue);
            return map;
        }

        [Fact]
        public void RaiseAndLower_UseSmoothFalloffAndStayInsideBrush()
        {
            TerrainHeightmap raised = Flat();
            int changed = TerrainSculpt.Apply(raised, TerrainTool.Raise, 8, 8, 4, 20);
            Assert.True(changed > 0);
            Assert.Equal(84, raised.Get(8, 8));
            Assert.Equal(TerrainHeightmap.NeutralValue, raised.Get(0, 0));
            Assert.True(raised.Get(9, 8) > TerrainHeightmap.NeutralValue);
            Assert.True(raised.Get(9, 8) < 84);

            TerrainHeightmap lowered = Flat();
            TerrainSculpt.Apply(lowered, TerrainTool.Lower, 8, 8, 4, 20);
            Assert.Equal(44, lowered.Get(8, 8));
        }

        [Fact]
        public void Raise_ClampsToIgiSignedByteRange()
        {
            TerrainHeightmap map = Flat();
            map.Fill(120);
            TerrainSculpt.Apply(map, TerrainTool.Raise, 8, 8, 6, 100);
            Assert.Equal(TerrainHeightmap.MaxValue, map.Get(8, 8));
            map.Fill(-120);
            TerrainSculpt.Apply(map, TerrainTool.Lower, 8, 8, 6, 100);
            Assert.Equal(TerrainHeightmap.MinValue, map.Get(8, 8));
        }

        [Fact]
        public void Smooth_ReducesSpikesAndLeavesFlatAreasUntouched()
        {
            TerrainHeightmap map = Flat();
            map.Set(8, 8, 127);
            int changed = TerrainSculpt.Apply(map, TerrainTool.Smooth, 8, 8, 3, 1.0);
            Assert.True(changed > 0);
            Assert.True(map.Get(8, 8) < 127);
            Assert.True(map.Get(8, 8) > 64);

            TerrainHeightmap flat = Flat();
            Assert.Equal(0, TerrainSculpt.Apply(flat, TerrainTool.Smooth, 8, 8, 4, 1.0));
        }

        [Fact]
        public void Sculpt_IsDeterministicAndPreservesDataOutsideBrush()
        {
            TerrainHeightmap a = Flat();
            TerrainHeightmap b = a.Clone();
            TerrainSculpt.Apply(a, TerrainTool.Raise, 4, 4, 5, 13);
            TerrainSculpt.Apply(b, TerrainTool.Raise, 4, 4, 5, 13);
            Assert.Equal(a.Samples, b.Samples);
            Assert.Equal(TerrainHeightmap.NeutralValue, a.Get(15, 15));
        }
    }

    public class TerrainViewTests
    {
        [Fact]
        public void ProjectAndPick_AreInverseOnThePlane()
        {
            TerrainCamera camera = new TerrainCamera();
            camera.Width = 720;
            camera.Height = 480;
            camera.Center = 8;
            TerrainPoint p = camera.Project(10, 5, 0);
            TerrainWorldPoint picked = camera.ScreenToTerrain(p.X, p.Y, 0);
            Assert.InRange(picked.X, 9.999, 10.001);
            Assert.InRange(picked.Y, 4.999, 5.001);
        }

        [Fact]
        public void SvgPreview_ContainsEveryTriangleInBackToFrontOrder()
        {
            TerrainHeightmap map = new TerrainHeightmap(4);
            map.Fill(TerrainHeightmap.NeutralValue);
            map.Set(2, 2, 100);
            TerrainCamera camera = new TerrainCamera();
            camera.Width = 400;
            camera.Height = 300;

            string svg = TerrainRenderer.RenderSvg(map, camera);
            Assert.Contains("<svg", svg);
            Assert.Equal(2 * 4 * 4, CountOccurrences(svg, "<polygon "));
            Assert.Contains("</svg>", svg);

            var faces = TerrainRenderer.BuildFaces(map, camera);
            for (int i = 1; i < faces.Count; i++) Assert.True(faces[i - 1].Depth >= faces[i].Depth);
            Assert.All(faces, f => Assert.InRange(f.Shade, 0, 255));
            Assert.Equal(svg, TerrainRenderer.RenderSvg(map, camera)); // deterministic
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }
    }
}
