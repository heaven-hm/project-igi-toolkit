using System;
using System.Globalization;
using System.IO;
using IGIEditor;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage();
            string command = args[0];
            if (command == "info") return Info(args);
            if (command == "roundtrip") return RoundTrip(args);
            if (command == "render") return Render(args);
            if (command == "sculpt") return Sculpt(args);
            if (command == "pgm-export") return PgmExport(args);
            return Usage();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("terrain-cli <command> [args]");
        Console.WriteLine("  info <file.hmp>                       list non-empty height map slots");
        Console.WriteLine("  roundtrip <in.hmp> <out.hmp>          save/load and report byte identity");
        Console.WriteLine("  render <file.hmp> <slot> <out.svg>    write a 3D SVG preview");
        Console.WriteLine("  sculpt <in.hmp> <slot> <raise|lower|smooth> <x> <y> <radius> <strength> <out.hmp>");
        Console.WriteLine("  pgm-export <file.hmp> <slot> <out.pgm>");
        return 2;
    }

    private static HmpFile Load(string path)
    {
        return HmpFile.Load(File.ReadAllBytes(path));
    }

    private static int Info(string[] args)
    {
        if (args.Length != 2) return Usage();
        HmpFile file = Load(args[1]);
        for (int i = 0; i < HmpFile.MaxSlots; i++)
        {
            HmpSlot slot = file.GetSlot(i);
            if (slot.Map == null) continue;
            Console.WriteLine("slot {0}: {1}x{2} samples, contents=0x{3:X8}, neutral={4}",
                i, slot.Map.Side, slot.Map.Side, slot.Contents, slot.Map.Get(0, 0));
        }
        return 0;
    }

    private static int RoundTrip(string[] args)
    {
        if (args.Length != 3) return Usage();
        byte[] before = File.ReadAllBytes(args[1]);
        byte[] after = Load(args[1]).Save();
        File.WriteAllBytes(args[2], after);
        bool identical = before.Length == after.Length;
        for (int i = 0; identical && i < before.Length; i++) identical = before[i] == after[i];
        Console.WriteLine("input bytes: {0}, output bytes: {1}, byte-identical: {2}",
            before.Length, after.Length, identical);
        return identical ? 0 : 1;
    }

    private static int Render(string[] args)
    {
        if (args.Length != 4) return Usage();
        HmpFile file = Load(args[1]);
        int slotId = int.Parse(args[2], CultureInfo.InvariantCulture);
        TerrainHeightmap map = file.GetSlot(slotId).Map;
        if (map == null) throw new InvalidOperationException("Slot " + slotId + " is empty.");

        TerrainCamera camera = new TerrainCamera();
        camera.Width = 960;
        camera.Height = 640;
        camera.Center = map.Size / 2.0;
        camera.Zoom = 1.35;
        camera.YawDegrees = 35;
        camera.PitchDegrees = 50;
        File.WriteAllText(args[3], TerrainRenderer.RenderSvg(map, camera), System.Text.Encoding.UTF8);
        Console.WriteLine("wrote {0} ({1} triangles)", args[3], map.Size * map.Size * 2);
        return 0;
    }

    private static int Sculpt(string[] args)
    {
        if (args.Length != 9) return Usage();
        HmpFile file = Load(args[1]);
        int slotId = int.Parse(args[2], CultureInfo.InvariantCulture);
        TerrainHeightmap map = file.GetSlot(slotId).Map;
        if (map == null) throw new InvalidOperationException("Slot " + slotId + " is empty.");
        TerrainTool tool;
        if (args[3] == "raise") tool = TerrainTool.Raise;
        else if (args[3] == "lower") tool = TerrainTool.Lower;
        else if (args[3] == "smooth") tool = TerrainTool.Smooth;
        else throw new ArgumentException("Unknown tool: " + args[3]);

        int x = int.Parse(args[4], CultureInfo.InvariantCulture);
        int y = int.Parse(args[5], CultureInfo.InvariantCulture);
        double radius = double.Parse(args[6], CultureInfo.InvariantCulture);
        double strength = double.Parse(args[7], CultureInfo.InvariantCulture);
        if (tool == TerrainTool.Smooth) strength /= 100.0;
        int changed = TerrainSculpt.Apply(map, tool, x, y, radius, strength);
        File.WriteAllBytes(args[8], file.Save());
        Console.WriteLine("changed {0} vertices; wrote {1}", changed, args[8]);
        return 0;
    }

    private static int PgmExport(string[] args)
    {
        if (args.Length != 4) return Usage();
        HmpFile file = Load(args[1]);
        int slotId = int.Parse(args[2], CultureInfo.InvariantCulture);
        TerrainHeightmap map = file.GetSlot(slotId).Map;
        if (map == null) throw new InvalidOperationException("Slot " + slotId + " is empty.");
        File.WriteAllBytes(args[3], HeightmapIo.ExportPgm(map));
        Console.WriteLine("wrote {0} ({1}x{2})", args[3], map.Side, map.Side);
        return 0;
    }
}
