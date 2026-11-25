using System;

namespace IGIEditor
{
    /// <summary>
    /// A single IGI height map tile. Samples are the raw signed bytes stored in a
    /// terrain.hmp file. The game converts a sample to a world Z offset with
    /// (sample - 64) * 256 world units; 4096 world units are one metre.
    /// </summary>
    /// <remarks>
    /// Format reference: https://github.com/hjcminus/Project-IGI-Terrain
    /// (MIT, hjcminus) - terrain_files.h and Terrain::CalcHMPDeltaZ.
    /// </remarks>
    public sealed class TerrainHeightmap
    {
        public const int NeutralValue = 64;
        public const int MinValue = -128;
        public const int MaxValue = 127;
        public const int MaxSize = 4096;

        private readonly sbyte[] _samples;

        public TerrainHeightmap(int size) : this(size, null)
        {
        }

        public TerrainHeightmap(int size, sbyte[] samples)
        {
            if (!IsValidSize(size)) throw new ArgumentException("Height map size must be a power of two in [1, 4096].", "size");
            Size = size;
            _samples = samples ?? new sbyte[Length];
            if (_samples.Length != Length) throw new ArgumentException("Expected " + Length + " samples.", "samples");
        }

        /// <summary>Grid dimension minus one (the value stored in a .hmp header).</summary>
        public int Size { get; private set; }

        /// <summary>Number of vertices along one axis (Size + 1).</summary>
        public int Side { get { return Size + 1; } }

        public int Length { get { return Side * Side; } }

        public sbyte[] Samples { get { return _samples; } }

        public static bool IsValidSize(int size)
        {
            return size >= 1 && size <= MaxSize && (size & (size - 1)) == 0;
        }

        public sbyte Get(int x, int y)
        {
            CheckBounds(x, y);
            return _samples[y * Side + x];
        }

        public void Set(int x, int y, int value)
        {
            CheckBounds(x, y);
            _samples[y * Side + x] = TerrainMath.ClampToSbyte(value);
        }

        public double HeightMeters(int x, int y)
        {
            return (Get(x, y) - NeutralValue) / 16.0;
        }

        public void Fill(int value)
        {
            sbyte v = TerrainMath.ClampToSbyte(value);
            for (int i = 0; i < _samples.Length; i++) _samples[i] = v;
        }

        public TerrainHeightmap Clone()
        {
            return new TerrainHeightmap(Size, (sbyte[])_samples.Clone());
        }

        private void CheckBounds(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Side || y >= Side) throw new ArgumentOutOfRangeException("x/y", "Sample is outside the height map.");
        }
    }

    /// <summary>One of the 256 fixed-size records in a terrain.hmp file.</summary>
    public sealed class HmpSlot
    {
        public uint Contents;       // pointer/placeholder written by the game tools; ignored on load
        public byte Unknown;
        public byte[] Padding = new byte[3];
        public int Size;            // 0 means the slot is unused
        public TerrainHeightmap Map;
    }

    /// <summary>
    /// Reader/writer for the IGI 1 terrain.hmp container: 256 x 12-byte headers
    /// followed by the payloads in slot order. A payload is (size+1)^2 signed bytes.
    /// </summary>
    public sealed class HmpFile
    {
        public const int MaxSlots = 256;
        public const int HeaderSize = 12;

        private readonly HmpSlot[] _slots;

        public HmpFile()
        {
            _slots = new HmpSlot[MaxSlots];
            for (int i = 0; i < MaxSlots; i++) _slots[i] = new HmpSlot();
        }

        public HmpSlot[] Slots { get { return _slots; } }

        public HmpSlot GetSlot(int id)
        {
            if (id < 0 || id >= MaxSlots) throw new ArgumentOutOfRangeException("id");
            return _slots[id];
        }

        public void SetSlot(int id, TerrainHeightmap map)
        {
            if (id < 0 || id >= MaxSlots) throw new ArgumentOutOfRangeException("id");
            if (map == null) throw new ArgumentNullException("map");
            HmpSlot slot = _slots[id];
            slot.Size = map.Size;
            slot.Map = map;
        }

        public void ClearSlot(int id)
        {
            if (id < 0 || id >= MaxSlots) throw new ArgumentOutOfRangeException("id");
            HmpSlot slot = _slots[id];
            slot.Size = 0;
            slot.Map = null;
        }

        public static HmpFile Load(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            int headerBytes = MaxSlots * HeaderSize;
            if (data.Length < headerBytes) throw new FormatException("terrain.hmp is shorter than the 256 header records.");

            HmpFile file = new HmpFile();
            long expected = headerBytes;
            int cursor = headerBytes;

            for (int i = 0; i < MaxSlots; i++)
            {
                int off = i * HeaderSize;
                uint contents = TerrainMath.ReadUInt32(data, off);
                byte unknown = data[off + 4];
                byte[] padding = new byte[] { data[off + 5], data[off + 6], data[off + 7] };
                uint rawSize = TerrainMath.ReadUInt32(data, off + 8);
                if (rawSize == 0) continue;
                if (rawSize > TerrainHeightmap.MaxSize || (rawSize & (rawSize - 1)) != 0)
                    throw new FormatException("Slot " + i + " has an invalid size " + rawSize + " (expected a power of two).");

                int size = (int)rawSize;
                long payload = (long)(size + 1) * (size + 1);
                expected += payload;
                if (expected > data.Length)
                    throw new FormatException("terrain.hmp is truncated in slot " + i + ".");

                sbyte[] samples = new sbyte[(int)payload];
                for (int k = 0; k < samples.Length; k++) samples[k] = unchecked((sbyte)data[cursor + k]);
                cursor += (int)payload;

                HmpSlot slot = file._slots[i];
                slot.Contents = contents;
                slot.Unknown = unknown;
                slot.Padding = padding;
                slot.Size = size;
                slot.Map = new TerrainHeightmap(size, samples);
            }

            if (expected != data.Length)
                throw new FormatException("terrain.hmp has " + (data.Length - expected) + " unexpected trailing bytes.");
            return file;
        }

        public byte[] Save()
        {
            long payloadLen = 0;
            for (int i = 0; i < MaxSlots; i++)
                if (_slots[i].Map != null) payloadLen += _slots[i].Map.Length;
            long total = MaxSlots * HeaderSize + payloadLen;
            if (total > int.MaxValue) throw new InvalidOperationException("Height map data is too large for a .hmp file.");

            byte[] data = new byte[(int)total];
            int cursor = MaxSlots * HeaderSize;
            for (int i = 0; i < MaxSlots; i++)
            {
                HmpSlot slot = _slots[i];
                int off = i * HeaderSize;
                TerrainMath.WriteUInt32(data, off, slot.Contents);
                data[off + 4] = slot.Unknown;
                for (int p = 0; p < 3; p++) data[off + 5 + p] = slot.Padding[p];
                if (slot.Map == null)
                {
                    TerrainMath.WriteUInt32(data, off + 8, 0);
                    continue;
                }
                TerrainMath.WriteUInt32(data, off + 8, (uint)slot.Map.Size);
                sbyte[] samples = slot.Map.Samples;
                for (int k = 0; k < samples.Length; k++) data[cursor + k] = unchecked((byte)samples[k]);
                cursor += samples.Length;
            }
            return data;
        }
    }

    internal static class TerrainMath
    {
        public static int Clamp(int value, int min, int max)
        {
            return value < min ? min : (value > max ? max : value);
        }

        public static sbyte ClampToSbyte(int value)
        {
            return unchecked((sbyte)Clamp(value, TerrainHeightmap.MinValue, TerrainHeightmap.MaxValue));
        }

        public static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        public static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
