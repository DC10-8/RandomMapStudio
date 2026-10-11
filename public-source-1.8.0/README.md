# Random Map Studio 1.8.0

Random terrain generation for **Cities: Skylines 1**, opened through Unified UI after starting a **New Game**.

[日本語](README.ja.md) · [Workshop description](Workshop/Workshop-description-ja-en.txt)

## Install and use

One package and one DLL include both English and Japanese. Replace the old mod files with this unified package. Put its `RandomMapStudio` folder in `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods`, then enable the mod and Unified UI. Quit the game before replacing an older installation.

1. Choose a map under **New Game** and press **Start**.
2. Open the terrain icon in UUI.
3. Enter land, sea and mountain percentages totaling 100%; mountains are separate from land. Optionally select **Add a river**.
4. Click **Generate** repeatedly to preview candidates in the panel. This does not modify the world.
5. Click **Create** to apply the chosen terrain, initialize water and remove existing structures and trees in water.
6. Save using the normal game save menu.

In the mod's Options, **Language / 言語** switches between Japanese and English. Existing panels refresh immediately, including while generating or applying terrain. Input values, panel position and the current candidate are preserved. A saved preference takes priority. Without a saved preference, a Japanese system UI defaults to Japanese; other system languages default to English.

## Scope and limitations

- The UI is enabled only for New Game, not loaded saves, editors or scenarios.
- Square height grids are detected from the live buffer. Coordinates use 16 m samples and world minimum -8640 m. Includes support code for HundredTiles' positive-axis expansion and packed tree positions. It does not expand the game itself.
- Rivers stop at original water bodies or map boundaries. They use the same 40 m water level as seas; this is not a hydrological flow model. Adding a river changes the resulting area ratios. There may be no river if its candidate route contains no usable land.
- Create removes roads, rail, quays, flood walls, canals, walls, dams and buildings, including outside connection structures. It does **not** rebuild outside connections.
- Trees in water are removed; land trees are aligned to new ground. Independent props and natural resources remain.
- Restore original terrain restores the initial terrain, water and painted sand, but **does not restore deleted objects** or every transient simulation state.
- Sand and ruined-ground textures are displayed as grass, including underwater textures. The appearance override is session-only and is not saved.
- This is a development build. Compilation and automated checks passed; the latest in-game behavior and all-mod compatibility have not been verified. Keep an original save.

## Build

Requires .NET SDK, access to .NET Framework 3.5 reference assemblies (NuGet restore), and legally installed Cities: Skylines 1 Managed DLLs.

```powershell
.\build.ps1
# Alternative game location:
.\build.ps1 -GameManaged 'D:\SteamLibrary\steamapps\common\Cities_Skylines\Cities_Data\Managed'
# Generator checks only:
dotnet run --project .\Checks\Checks.csproj -c Release
```

The build creates one bilingual package plus a clean source archive under `dist`. Game DLLs are never packaged.

`Libs/UnifiedUILib.dll` is a third-party MIT-licensed library. See `LICENSE.UnifiedUI.txt` and https://github.com/ssv2/Skylines-UnifiedUI. No open-source license grant for this project's original code has been selected yet; publishing source does not itself grant unrestricted reuse rights.

## Workshop assets

The bilingual description contains 2901 Unicode characters including whitespace, under the requested combined limit of 4000. `Workshop/thumbnail-512.jpg` is a 512 x 512 upload image. Promotional artwork is AI-generated and is not an in-game screenshot; the generation prompt is included.
