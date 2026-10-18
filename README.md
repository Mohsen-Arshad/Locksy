# Locksy

**Official release: v1.3.0**

Locksy is an Android lock-screen task list built with .NET MAUI 9. This repository is the first published GitHub baseline for the current, fully patched application.

## Features

- Add, edit, complete, delete, and reorder tasks.
- Configure task colors and one-time reminders.
- Choose the app theme and language, plus a custom wallpaper background.
- Select a responsive wallpaper layout around the system clock and tune placement.
- Apply wallpaper manually or queue an automatic refresh for background execution.
- Store tasks locally as JSON in the app's private storage.
- Use a custom launcher icon, splash screen, and in-app brand treatment.
- Use responsive in-app popups that size to content and scroll when required.

## Architecture

The project uses MVVM with SOLID-oriented dependency boundaries. `TodoItem` is persistent data; `TaskItemViewModel` exposes bindable presentation properties; `MainViewModel`, `WallpaperViewModel`, `SettingsViewModel`, `ReminderViewModel`, and `NewTaskSettingsViewModel` own independent UI state and workflows. Service contracts are under `Services/Abstractions`, persistence is under `Services/Persistence`, and dependency registration/composition is centralized in `MauiProgram` and `MainPageFactory`.

Custom UI handlers remain in `Views/CodeBehind` by responsibility. These handlers are for modal rendering, visual transitions, dynamic control construction, and Android gesture integration; persistence and the main wallpaper/settings workflows belong in ViewModels and services.

See [`docs/architecture/ARCHITECTURE-v1.3.0-FA.md`](docs/architecture/ARCHITECTURE-v1.3.0-FA.md), [`RELEASE-NOTES-v1.3.0.md`](RELEASE-NOTES-v1.3.0.md), and [`INSTALL-v1.3.0-FA.md`](INSTALL-v1.3.0-FA.md).

## Build and run

Requirements: Visual Studio with the .NET MAUI workload and Android SDK.

```powershell
dotnet restore .\Locksy.csproj
dotnet build .\Locksy.csproj -f net9.0-android -c Debug
```

For the project's full source-presence and platform-guard checks followed by a Debug build, run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\VERIFY-AND-BUILD-v1.3.0.ps1
```

Open `Locksy.sln`, restore packages, select an Android device/emulator, and run.

## Android release metadata

- Package identifier: `com.mohsen.locksy`.
- Official display version: `1.3.0`.
- Android `versionCode`: `25`.

The display version is the public product version. The internal Android `versionCode` is intentionally kept at 25, the highest number used by the pre-release builds, rather than being reset to 20. `versionCode` is an internal monotonically increasing build number, independent from the user-facing semantic version.

## Compatibility notes

- The persistent task JSON field names and app-private storage path are preserved.
- The package identifier is unchanged.
- Navigation-bar behavior uses API-version guards and the native insets API for newer Android versions.
- Android does not expose a reliable cross-device API for reading the exact lock-screen clock position. Wallpaper placement is a responsive profile, not guaranteed clock detection.
- `StorageHelper` remains as a compatibility facade for Android receiver paths; the main application uses `ITaskRepository` through dependency injection.

## Release history note

The labels `1.3.1` through `1.3.5` were internal, pre-publication repair iterations used while validating the code, not official published product releases. Their fixes are integrated into this release, which is intentionally labeled **v1.3.0**. Notes from those iterations are kept in `docs/history/pre-release-iterations/` for traceability. No runtime source was rolled back to relabel the release.
