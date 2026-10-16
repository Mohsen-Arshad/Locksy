# Locksy

**Turn everyday reminders into a customizable Android lock-screen wallpaper.**

Locksy is an Android app built with .NET MAUI that lets you manage reminders and display them on your lock screen as a personalized wallpaper. Customize task colors and layout, choose a light or dark app theme, and set one-time reminders for tasks you do not want to forget.

> **Project status:** Early development. Device compatibility and some Android-specific behavior are still being tested.

## Features

- **Manage reminders** - Add, edit, complete, delete, and reorder items.
- **Lock-screen wallpaper** - Render reminders into a custom wallpaper and apply it to the lock screen.
- **Automatic or manual updates** - Apply the wallpaper manually or refresh it after reminder changes.
- **Customizable layouts** - Choose different arrangements that leave room for the Android lock-screen clock, with controls for vertical placement and clock clearance.
- **Task colors** - Choose colors for individual reminders and organize the list visually.
- **Light and dark themes** - Switch the app's appearance while preserving each reminder's saved color choice.
- **One-time alarms** - Schedule reminder notifications for a selected date and time. Active reminders are visually distinguished from inactive ones on the wallpaper.
- **Local storage** - Store reminder data in the app's private storage.
- **Custom startup experience** - Includes a branded launcher icon and animated in-app splash screen.

## Tech Stack

- C#
- .NET MAUI 9
- Android SDK
- Android `WallpaperManager` API
- Android alarms and notifications

## Requirements

- Visual Studio 2022 with the .NET MAUI workload, or a compatible .NET 9 MAUI development environment
- Android SDK and build tools
- An Android device or emulator for testing

Locksy targets Android 7.0 (API 24) and later. Alarm precision, notification permissions, and lock-screen wallpaper behavior may vary by Android version and device manufacturer.

## Build and Run

1. Clone or download the repository.
2. Open `Locksy.sln` in Visual Studio.
3. Restore NuGet packages.
4. Select an Android emulator or connected device.
5. Build and run the app.

Alternatively, run these commands in the directory containing `Locksy.csproj`:

```powershell
dotnet restore .\Locksy.csproj
dotnet build .\Locksy.csproj -f net9.0-android -c Debug
```

To create a Release APK for direct installation, configure Android signing with a keystore and publish in Release mode. Keep the keystore and its passwords safe, as future updates should use the same signing key. Google Play publishing is not required for direct APK installation.

## Android Compatibility Notes

Locksy requests a lock-screen-only wallpaper update through Android's wallpaper API. Some manufacturers and custom lock-screen themes handle wallpapers differently, so behavior may vary across devices.

Android does not provide a reliable way to detect the exact lock-screen clock position on every device. Locksy therefore provides layout and spacing controls so users can adjust the wallpaper manually.

Reminder notifications require the appropriate Android notification permission. Exact alarm scheduling may require additional permission on newer Android versions; when exact alarms are unavailable, reminders may be delivered less precisely.

## Data and Privacy

Reminder data is stored locally in the app's private storage. Locksy does not require a Google account or Play Store installation when installed from a signed APK.

## Contributing

Bug reports, device compatibility notes, and focused improvements are welcome. When reporting an issue, include the device model, Android version, and steps to reproduce the problem.

## License

This project is licensed under the **GNU General Public License v2.0 (GPL-2.0)**.

See the [LICENSE](LICENSE) file for the full text.
