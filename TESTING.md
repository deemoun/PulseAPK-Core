# Testing Checklist

Use this quick checklist to confirm PulseAPK is functional after setup or a code change.

## 1) Environment & Launch

- [ ] `dotnet build` completes without errors.
- [ ] `dotnet run` starts the application successfully.
- [ ] The app opens with top navigation visible: **Decompile**, **Build**, **Analyser**, **Settings**, **About**.

## 2) Settings Validation

- [ ] In **Settings**, Java is detected (or a clear warning is shown if missing).
- [ ] `apktool.jar` path can be set manually.
- [ ] `uber-apk-signer.jar` path can be set manually.
- [ ] Saving settings persists after restarting the app.

## 3) Decompile Flow

- [ ] Select a test APK in **Decompile**.
- [ ] Choose an output folder.
- [ ] Run decompilation and verify it completes without crash.
- [ ] Confirm expected output files/folders are created (e.g., `AndroidManifest.xml`, `smali/`).

## 4) Analysis Flow

- [ ] Open **Analyser** and select the decompiled project folder.
- [ ] Run Smali analysis.
- [ ] Verify findings/results render in the UI.
- [ ] Export/save the analysis report successfully.

## 5) Build & Sign Flow

- [ ] In **Build**, select a valid decompiled project folder.
- [ ] Build APK and confirm output APK is generated.
- [ ] Enable signing and run build/sign again.
- [ ] Verify signed APK output is created without errors.

## 6) Basic Stability Checks

- [ ] Repeat one full cycle (**Decompile → Analyse → Build**) without restarting.
- [ ] Trigger at least one expected validation error (e.g., missing path) and confirm message is clear.
- [ ] Close and reopen app; confirm it starts cleanly and keeps prior settings.

## 7) Unit Tests (Optional but recommended)

- [ ] Run unit tests:

```bash
dotnet test
```

- [ ] Confirm all tests pass.
