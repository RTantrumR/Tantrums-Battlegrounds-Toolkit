# Building Tantrum's Battlegrounds Toolkit

The project and its DLL are named `HsbgCardLookup`, the name the toolkit had as an HDT plugin.

## Stack
- **Language**: C# 7.3
- **Framework**: .NET Framework 4.7.2, WPF
- **Toolchain**: Visual Studio 2022 (MSBuild)
- **Program**: a standalone tray program (`Standalone/`) that hosts `HsbgCardLookup.dll`

## Building from Source
1. Clone the repository. Everything the build needs is in it.
2. Build `HsbgCardLookup.sln`, then `Standalone/Standalone.csproj`, in Visual Studio 2022 or with its MSBuild.
3. Run `Standalone\bin\Release\TantrumsToolkitStandalone.exe`.

`installer/build.ps1` does the same build and makes the Setup; the GitHub Actions workflow in `.github/workflows/build.yml` runs it.

*Note: The `dotnet` CLI cannot build this project as it uses the legacy WPF markup compiler for .NET Framework 4.7.2.*

## Scripts
- `.\installer\build.ps1`: Builds Release and the Setup wizard into `installer\out\` (needs Inno Setup 6).
- `.\package.ps1`: Runs it and copies the Setup into `dist/`.

## Project Structure
- `HsbgCardLookup/`: Main project source code.
  - `Config/`: Persisted XML settings.
  - `Game/`: Game state reading and HUD logic.
  - `Ui/`: WPF overlays, floating cards, and settings UI.
  - `Net/`: API and update notification clients.
  - `Localization/`: the language service (`CardLanguage`: game content and the plugin's own texts), the
    locale registry, the vocabulary table and the snapshot sync client.
  - `data/ui/<language>.json`: the plugin's UI strings; `en.json` is the key source, every other file
    is a partial overlay (a missing key renders English).
  - `data/locale-terms.json`: the game's words per language (tribes, keywords, card types, spell
    schools, trinket tiers, categories) for the search and the filter labels.

## Localization tooling
- `node tools/gen-locale-terms.js <path-to-hsbgcards-checkout>` regenerates `data/locale-terms.json`
  from the website's `data/production/terms.json` and UI catalogs. Run it after a site import that
  changes the terms table.
- `node tools/check-ui-strings.js [--strict]` holds every `data/ui/<language>.json` to `en.json`: valid
  JSON, no unknown keys, identical `{n}` placeholders; `--strict` also fails on missing keys.
- `libs/`: The bundled WebP decoder (ImageSharp + its `System.*` closure) and Json.NET, all committed.
- `dist/`: Build artifacts (after running scripts).
