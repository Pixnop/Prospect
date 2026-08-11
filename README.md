# Prospect

[![CI](https://github.com/Pixnop/Prospect/actions/workflows/ci.yml/badge.svg)](https://github.com/Pixnop/Prospect/actions/workflows/ci.yml)
[![Quality gate](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Prospect&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=Pixnop_Prospect)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=Pixnop_Prospect&metric=coverage)](https://sonarcloud.io/component_measures?id=Pixnop_Prospect&metric=coverage)

Prospect is a Vintage Story launcher in the spirit of Prism Launcher.

The project is in early development. There is no build to download yet, and the desktop app currently opens to an empty window. What exists so far is the solution scaffold, the quality tooling around it, and the architecture the rest will be built against.

The MVP targets a short list of things Prism does well and VS Launcher never quite got to:

- isolated instances, each with its own game version, mods, and worlds
- multiple game versions installed side by side
- launching through Vintage Story's native `--dataPath` flag rather than juggling a single shared install
- a client for the official ModDB, for searching, installing, and updating mods
- modpacks that export and import cleanly, with no absolute paths or signed URLs baked in

VS Launcher, the launcher most of the community has relied on for years, was archived in June 2026. Prospect exists to give Vintage Story players somewhere to go next.

## Building from source

You need the .NET 10 SDK. From the repository root:

```
dotnet build
dotnet test
```

`dotnet test` runs the full suite and enforces an 80% line coverage floor on `Prospect.Core`. A drop below that fails the build, locally and in CI.

## Architecture

The design behind `Prospect.Core` and `Prospect.Desktop`, the on-disk layout, and the reasoning for each structural decision are written up in [docs/architecture.md](docs/architecture.md) (in French).

## License

GPL-3.0. See [LICENSE](LICENSE).
