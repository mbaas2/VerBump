# VerBump

**Version file manager for Windows developers**

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![GitHub release](https://img.shields.io/github/v/release/mbaas2/VerBump)](https://github.com/mbaas2/VerBump/releases/latest)
[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-pink?logo=github-sponsors)](https://github.com/sponsors/mbaas2)

Keep all your projects' `VERSION` files in one place. Bump major, minor, or patch
with a single keystroke — before every `git push`, open VerBump and see the status
of all your repos at a glance.

→ **[Website & full documentation](https://mbaas2.github.io/VerBump/)**

## Features

- **Staleness detection** — highlights projects where source files are newer than the current VERSION
- **Keyboard-driven** — jump to any project with Alt+A–Z, bump with Ctrl+1–4
- **Multiple schemes** — SemVer, CalVer, and custom sequential schemes
- **Flexible ignore rules** — global + per-project, with `!`-prefix exclusion
- **Multilingual** — English and German; add more by dropping a `lang.xx.json` next to the exe
- **Zero dependencies** — single self-contained `.exe`, no .NET runtime installation needed

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

VerBump is free and open source (MIT). If it saves you time, consider buying me a coffee:

[![Sponsor on GitHub](https://img.shields.io/badge/Sponsor%20on%20GitHub-%E2%9D%A4-pink?logo=github-sponsors&style=for-the-badge)](https://github.com/sponsors/mbaas2)

## License

[MIT](LICENSE) © 2025 Michael Baas
