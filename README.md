# VerBump

**Version file manager for Windows developers**

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/mbaas2/VerBump)](https://github.com/mbaas2/VerBump/releases/latest)
[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-pink?logo=github-sponsors)](https://github.com/sponsors/mbaas2)

Ever pushed code only to realize you forgot to bump the VERSION file — again?
VerBump keeps all your projects' version state in one view, highlights stale
ones in orange, and lets you bump with a single keystroke — before every push.

→ **[Website & full documentation](https://mbaas2.github.io/VerBump/)**

![VerBump main window](docs/screenshots/main-en.png)

![VerBump Git pre-commit hook](docs/screenshots/hook-en.png)

## Features

- **Staleness detection** — highlights projects where source files are newer than the current VERSION or were updated after last commit
- **Keyboard-driven** — jump to any project with Alt+A–Z, bump with Ctrl+1–4
- **Multiple schemes** — SemVer, CalVer, and custom sequential schemes
- **Flexible ignore rules** — global + per-project, with `!`-prefix exclusion
- **Multilingual** — English and German; add more by dropping a `lang.xx.json` next to the exe
- **Zero dependencies** — single self-contained `.exe`, no .NET runtime installation needed
- **Git pre-commit hook** — install per project from Settings; blocks commits when VERSION is stale
- **Explorer context menu** — right-click a folder or `VERSION` file to open VerBump or silently bump Major/Minor/Patch; optional installer task registers double-click support for extension-less files
- **CLI arguments** — `--check`, `--bump=N`, `--settings=<path>`, or pass a project path directly

## Download

→ **[Latest release](https://github.com/mbaas2/VerBump/releases/latest)**

Windows 10/11 · x64 · Self-contained

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
# Debug build
dotnet build src/VerBump.csproj

# Release — single self-contained exe
dotnet publish src/VerBump.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output: `src/bin/Release/net8.0-windows/win-x64/publish/VerBump.exe`

## Support

Found a bug or have a feature request? [Open an issue](https://github.com/mbaas2/VerBump/issues) or contact me directly at [verbump@mbaas.de](mailto:verbump@mbaas.de) — I'm available for questions, feedback, and the occasional VerBump war story.

VerBump is free and open source (MIT). If it saves you time, consider buying me a coffee or a coffe machine:

[![Sponsor on GitHub](https://img.shields.io/badge/Sponsor%20on%20GitHub-%E2%9D%A4-pink?logo=github-sponsors&style=for-the-badge)](https://github.com/sponsors/mbaas2)

## License

[MIT](LICENSE) © 2025 Michael Baas
