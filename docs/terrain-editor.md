# Terrain Editor (vertical slice)

The **3D Toolkit** tab in Project I.G.I ToolKit now opens a native terrain editor:

- `Start 3D Editor` — opens a new 32x32 (33x33 vertex) height map in slot 0.
- `Resume 3D Editor` — asks for a `terrain.hmp` and opens its first populated slot.

The window shows an orbitable 3D shaded surface. Left-drag applies the selected
tool, right-drag lowers, the mouse wheel zooms, and the Rotate L/R buttons orbit
the camera. `1`/`2`/`3` select raise/lower/smooth.

## File support

| Operation | Format | Notes |
|---|---|---|
| Open / Save | IGI `terrain.hmp` | All 256 fixed 12-byte headers and every `(size+1)^2` signed-byte payload are preserved byte-for-byte when unedited. |
| Import | 8-bit PGM (P5) or RAW | `P5` images may contain comments; RAW uses the current slot size. Value 128 maps to IGI neutral height 64; values 0..191 cover the full signed range. |
| Export | 8-bit PGM (P5) | Useful for editing a height map in an external paint program. |

### IGI height-map encoding

`terrain.hmp` is 256 records of 12 bytes (`contents` uint32, `unknown` byte,
3 padding bytes, `size` uint32) followed by the payloads in slot order. A slot
with `size = 0` is empty; otherwise its payload has `(size + 1)^2` signed bytes
and `size` is a power of two. The game converts each sample to a world Z offset
using `(sample - 64) * 256` world units (4096 units = 1 metre).

Format source: [hjcminus/Project-IGI-Terrain](https://github.com/hjcminus/Project-IGI-Terrain),
MIT licensed, `terrain_files.h` and `Terrain::CalcHMPDeltaZ`. The C# implementation
in `IGIEditor/QTerrain*.cs` is an independent implementation of that documented
format; no third-party code was copied into this repository.

A height map in a `.hmp` file only affects the game when a matching
`Task_New(..., "HeightMap", ...)` entry in that level's `objects.qsc` references
the same slot ID and size. The editor does not modify `objects.qsc` for you.

## Layout

- `IGIEditor/QTerrainCore.cs` — height field and `.hmp` container.
- `IGIEditor/QTerrainTools.cs` — PGM/RAW import/export and sculpt tools.
- `IGIEditor/QTerrainView.cs` — camera, shaded triangles, headless SVG preview.
- `IGIEditor/QTerrainEditorForm.cs` — WinForms editor window.
- `tests/TerrainCore.Tests` — cross-platform unit tests for the portable core.
- `tools/TerrainCli` — headless CLI used for verification and batch conversion.

```sh
dotnet test tests/TerrainCore.Tests/TerrainCore.Tests.csproj
dotnet run --project tools/TerrainCli -- info path/to/terrain.hmp
dotnet run --project tools/TerrainCli -- roundtrip path/to/terrain.hmp copy.hmp
dotnet run --project tools/TerrainCli -- render path/to/terrain.hmp 0 preview.svg
```

## Known gaps (not yet accepted as a complete terrain editor)

- Only `.hmp` height data is edited; `.ctr`, `.cmd`, `.bit`, `.lmp` and texture
  modifiers are not decoded or saved.
- No `objects.qsc` `HeightMap` task creation/update, so the result is not
  automatically wired into a level.
- No undo/redo, no layer/mask painting, no live in-game preview.
- The WinForms window requires Windows/.NET Framework 4.8. On macOS the portable
  core, tests, CLI and SVG renderer are verifiable, but the GUI cannot be run.
