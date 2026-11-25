using System;
using System.Globalization;
using System.Text;

namespace IGIEditor
{
    public enum TerrainTool
    {
        Raise,
        Lower,
        Smooth
    }

    /// <summary>
    /// Import/export helpers for simple heightmap files.
    /// Byte b maps to sample (b - 64), so PGM/RAW value 128 is IGI's neutral
    /// height (64) and the usable range 0..191 covers all signed samples.
    /// </summary>
    public static class HeightmapIo
    {
        public static sbyte FromByte(byte value)
        {
            return TerrainMath.ClampToSbyte(value - TerrainHeightmap.NeutralValue);
        }

        public static byte ToByte(sbyte value)
        {
            return (byte)TerrainMath.Clamp(value + TerrainHeightmap.NeutralValue, 0, 255);
        }

        public static TerrainHeightmap ImportPgm(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            int pos = 0;
            if (NextToken(data, ref pos) != "P5") throw new FormatException("Only binary grayscale PGM (P5) files are supported.");
            int width = ParseInt(NextToken(data, ref pos));
            int height = ParseInt(NextToken(data, ref pos));
            int maxValue = ParseInt(NextToken(data, ref pos));
            if (maxValue != 255) throw new NotSupportedException("Only PGM files with maxval 255 are supported.");

            // The PGM spec requires exactly one whitespace character after maxval.
            if (pos >= data.Length || !IsWhite(data[pos])) throw new FormatException("Malformed PGM header.");
            pos++;

            if (width != height || !TerrainHeightmap.IsValidSize(width - 1))
                throw new FormatException("PGM dimensions must be (2^n + 1) x (2^n + 1).");
            int size = width - 1;
            int length = new TerrainHeightmap(size).Length;
            if (data.Length - pos < length) throw new FormatException("PGM image data is truncated.");

            TerrainHeightmap map = new TerrainHeightmap(size);
            for (int i = 0; i < length; i++) map.Samples[i] = FromByte(data[pos + i]);
            return map;
        }

        public static byte[] ExportPgm(TerrainHeightmap map)
        {
            if (map == null) throw new ArgumentNullException("map");
            string header = "P5\n" + map.Side.ToString(CultureInfo.InvariantCulture) + " " +
                            map.Side.ToString(CultureInfo.InvariantCulture) + "\n255\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] result = new byte[headerBytes.Length + map.Length];
            Array.Copy(headerBytes, result, headerBytes.Length);
            for (int i = 0; i < map.Length; i++) result[headerBytes.Length + i] = ToByte(map.Samples[i]);
            return result;
        }

        public static TerrainHeightmap ImportRaw8(byte[] data, int size)
        {
            if (data == null) throw new ArgumentNullException("data");
            if (!TerrainHeightmap.IsValidSize(size)) throw new ArgumentException("Invalid height map size.", "size");
            TerrainHeightmap map = new TerrainHeightmap(size);
            if (data.Length < map.Length) throw new FormatException("RAW image is shorter than (size+1)^2 bytes.");
            for (int i = 0; i < map.Length; i++) map.Samples[i] = FromByte(data[i]);
            return map;
        }

        public static byte[] ExportRaw8(TerrainHeightmap map)
        {
            if (map == null) throw new ArgumentNullException("map");
            byte[] result = new byte[map.Length];
            for (int i = 0; i < map.Length; i++) result[i] = ToByte(map.Samples[i]);
            return result;
        }

        private static bool IsWhite(byte b)
        {
            return b == ' ' || b == '\t' || b == '\r' || b == '\n';
        }

        private static void Skip(byte[] data, ref int pos)
        {
            while (pos < data.Length)
            {
                while (pos < data.Length && IsWhite(data[pos])) pos++;
                if (pos >= data.Length || data[pos] != '#') return;
                while (pos < data.Length && data[pos] != '\n') pos++;
            }
        }

        private static string NextToken(byte[] data, ref int pos)
        {
            Skip(data, ref pos);
            int start = pos;
            while (pos < data.Length && !IsWhite(data[pos])) pos++;
            if (start == pos) throw new FormatException("Unexpected end of PGM header.");
            return Encoding.ASCII.GetString(data, start, pos - start);
        }

        private static int ParseInt(string token)
        {
            int value;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Invalid number in PGM header: " + token);
            return value;
        }
    }

    /// <summary>Radial sculpting tools. Every edit is clamped to the IGI int8 range.</summary>
    public static class TerrainSculpt
    {
        /// <returns>The number of vertices whose value changed.</returns>
        public static int Apply(TerrainHeightmap map, TerrainTool tool, int centerX, int centerY, double radius, double strength)
        {
            if (map == null) throw new ArgumentNullException("map");
            if (radius <= 0) throw new ArgumentOutOfRangeException("radius");
            if (double.IsNaN(strength) || double.IsNaN(radius)) throw new ArgumentException("Strength and radius must be numbers.");

            int side = map.Side;
            float[] working = new float[map.Length];
            for (int i = 0; i < working.Length; i++) working[i] = map.Samples[i];

            int minX = Math.Max(0, (int)Math.Floor(centerX - radius));
            int maxX = Math.Min(side - 1, (int)Math.Ceiling(centerX + radius));
            int minY = Math.Max(0, (int)Math.Floor(centerY - radius));
            int maxY = Math.Min(side - 1, (int)Math.Ceiling(centerY + radius));
            double radiusSquared = radius * radius;
            double blend = TerrainMath.Clamp((int)Math.Round(strength * 100.0), 0, 100) / 100.0;
            int changed = 0;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    double dx = x - centerX;
                    double dy = y - centerY;
                    double distSquared = dx * dx + dy * dy;
                    if (distSquared > radiusSquared) continue;

                    double t = Math.Sqrt(distSquared) / radius;
                    double falloff = 1.0 - t * t;
                    falloff *= falloff;
                    int idx = y * side + x;

                    if (tool == TerrainTool.Smooth)
                    {
                        double sum = 0;
                        int count = 0;
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int nx = TerrainMath.Clamp(x + ox, 0, side - 1);
                                int ny = TerrainMath.Clamp(y + oy, 0, side - 1);
                                sum += working[ny * side + nx];
                                count++;
                            }
                        }
                        working[idx] += (float)((sum / count - working[idx]) * blend * falloff);
                    }
                    else
                    {
                        double delta = (tool == TerrainTool.Raise ? strength : -strength) * falloff;
                        working[idx] += (float)delta;
                    }

                    sbyte value = TerrainMath.ClampToSbyte((int)Math.Round(working[idx]));
                    if (value != map.Samples[idx]) changed++;
                    map.Samples[idx] = value;
                }
            }
            return changed;
        }
    }
}
