# HSAchieveGuide

HSAchieveGuide is an unofficial Windows desktop tool for browsing and managing
Hearthstone achievements alongside
[Firestone](https://github.com/Zero-to-Heroes/firestone). It combines the
official achievement hierarchy, account progress, card collection data, and
community-authored guides in one interface.

## Features

- Browse achievements by official category, subcategory, and class.
- Mark achievements under the official retired Classic category separately as
  retired and exclude them from incomplete items, without changing Firestone's
  original completion count or the account's current achievement points.
- Inspect progress, tier requirements, points, and related cards.
- Search and filter the local card collection.
- Read local community guides, copy deck codes, and open original sources.
- Refresh account data through Firestone when available, with an offline
  official-achievement baseline as fallback.
- No guide server, guide upload, or administrator-review functionality.

## Requirements and usage

- 64-bit Windows 10/11 and .NET Framework 4.8.
- Firestone must be installed; start Hearthstone and Firestone before
  refreshing live account data.
- Download and fully extract a release, then run `HSAchieveGuide.exe`.
- Keep `ExportMindVisionAchievements.v3.exe` next to the main executable.
- The runtime exporter is built for 64-bit Firestone/Overwolf modules.

The usual Firestone data directory is:

`%APPDATA%\Overwolf\lnknbakkpommmjjdnelmfbjjdbocfpnpbkijjnob`

Do not select `%LOCALAPPDATA%\Overwolf\Extensions\...` as the data directory.

## Build from source

```powershell
PowerShell -ExecutionPolicy Bypass -File .\scripts\compile-HSAchieveGuide.ps1
```

The build output is written to `dist/`. The script downloads a pinned Roslyn
compiler package from the official NuGet feed and uses the local .NET
Framework reference assemblies.

## Privacy and network access

The application reads local Firestone/Hearthstone data. It has no account-data
upload service. The export helper requests public achievement reference data
from `static.zerotoheroes.com`; source links open in the default browser.
Runtime exports, logs, local paths, and collection files are ignored by Git.

## License

Original source code and build scripts are available under the
[MIT License](LICENSE). Hearthstone data, trademarks, community guides, and
other third-party material are not covered by that license. See
[NOTICE.md](NOTICE.md).

This is not an official Blizzard Entertainment or Firestone product. The tool
is organized and published by community individuals, with OpenAI tools used as
development assistance.
