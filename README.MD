# PulseAPK

<p align="center">
  🌍 <strong>Languages</strong><br>
  <a href="README.MD">English</a> |
  <a href="README.de.md">Deutsch</a> |
  <a href="README.es.md">Español</a> |
  <a href="README.fr.md">Français</a> |
  <a href="README.he.md">עברית</a> |
  <a href="README.ko.md">한국어</a> |
  <a href="README.be.md">Беларуская</a> |
  <a href="README.fi.md">Suomi</a> |
  <a href="README.lv.md">Latviešu</a> |
  <a href="README.et.md">Eesti</a> |
  <a href="README.lt.md">Lietuvių</a> |
  <a href="README.cs.md">Čeština</a> |
  <a href="README.sk.md">Slovenčina</a> |
  <a href="README.hu.md">Magyar</a> |
  <a href="README.ar.md">العربية</a> |
  <a href="README.pt.md">Português</a> |
  <a href="README.ru.md">Русский</a> |
  <a href="README.uk.md">Українська</a> |
  <a href="README.zh.md">中文</a>
</p>

PulseAPK is a cross-platform desktop GUI for Android APK reverse engineering workflows. It is built with **.NET 8 + Avalonia** and wraps common tasks around `apktool` and `uber-apk-signer` so you can decompile, inspect, rebuild, and sign APKs from one interface.

[Watch the demo on YouTube](https://youtu.be/Mkdt0c-7Wwg)

## What PulseAPK does today

PulseAPK is organized into tabs for each stage of the workflow:

- **Decompile**: Decode APKs into a project folder with common apktool decode options.
- **Analyser**: Run static Smali scanning using configurable regex-based rules from `smali_analysis_rules.json`.
- **Build**: Rebuild a decoded project with apktool, optionally using AAPT2, and optionally sign output using Uber APK Signer.
- **Settings**: Configure tool paths (`apktool.jar`, `uber-apk-signer`), and switch UI language.
- **About**: App metadata and project info.

### Analysis highlights

The analyzer currently focuses on practical static checks, including:

- Root detection patterns
- Emulator detection patterns
- Hardcoded credential/token patterns
- SQL query string patterns
- HTTP/HTTPS endpoint patterns

Rule packs are editable without recompiling the app. See `APK_ANALYSIS_RULES.md` for rule structure and examples.

## Requirements

### To run the app

- **Java runtime** in `PATH` (required by apktool and signing tooling).
- **apktool JAR** (set in Settings).
- **Uber APK Signer JAR/EXE** (set in Settings if you want signing).

### To build from source

- **.NET SDK 8.0+**
- OS-specific dependencies for your target runtime (Windows/macOS/Linux)

## Build and run (development)

From repo root:

```bash
dotnet restore
dotnet build PulseAPK.sln
dotnet run --project src/PulseAPK.Avalonia/PulseAPK.Avalonia.csproj
```

After first launch, open **Settings** and set paths to `apktool` and `uber-apk-signer`.

## Packaging binaries

Predefined scripts are included under `scripts/`:

- `scripts/build-windows-exe.sh` → self-contained Windows publish + zip
- `scripts/build-macos-binary.sh` → self-contained macOS publish + archive
- `scripts/build-appimage.sh` → Linux AppImage (requires `appimagetool`)

Examples:

```bash
bash scripts/build-windows-exe.sh
bash scripts/build-macos-binary.sh
bash scripts/build-appimage.sh
```

Set optional env vars like `RID` and `CONFIGURATION` when needed.

## Tests

Run unit tests:

```bash
dotnet test tests/unit/PulseAPK.Tests/PulseAPK.Tests.csproj
```

## Architecture

- `src/PulseAPK.Avalonia`: UI layer (Avalonia)
- `src/PulseAPK.Core`: ViewModels, services, business logic, analysis engine
- `tests/unit/PulseAPK.Tests`: unit tests

The project follows an MVVM structure with dependency-injected services for tool execution, dialogs, file picking, settings, and localization.

## License

Licensed under the [Apache License 2.0](LICENSE.md).

## Contributing

Contributions are welcome. Please review and sign the [Contributor License Agreement (CLA)](CLA.md) when submitting a pull request.
