# Locksy

Locksy is an Android task-list and lock-screen wallpaper companion that helps keep everyday priorities visible at a glance. It brings task management and personalized wallpaper creation together in a single, focused experience.

## What Locksy Does

- Create, edit, complete, delete, and reorder tasks.
- Use task colors to make priorities easier to recognize.
- Schedule one-time reminders for tasks that need attention later.
- Turn the task list into a lock-screen wallpaper, with layout options designed to keep content readable around the system clock.
- Adjust wallpaper content placement and preview layout choices before applying them.
- Apply wallpaper changes manually or use automatic refresh behavior.
- Personalize the interface with light and dark themes, language settings, and custom wallpaper backgrounds.
- Use adaptive in-app dialogs and settings panels designed to fit their content and available screen space.

## Product Focus

Locksy is built around a simple idea: useful tasks should be easy to capture and stay visible without adding unnecessary friction. Task organization, reminders, and wallpaper customization work together so users can keep their priorities in view while tailoring the interface to their preferences.

## Technical Overview

Locksy is built with .NET MAUI and uses an MVVM-oriented architecture with SOLID-inspired separation of responsibilities.

- **Models** represent task data independently of presentation details.
- **ViewModels** manage bindable UI state and task, settings, reminder, and wallpaper workflows.
- **Service abstractions** separate application workflows from concrete persistence, preferences, localization, navigation, media selection, reminders, and wallpaper operations.
- **Local persistence** stores task data in JSON in the app's private storage.
- **Android integrations** handle platform-specific wallpaper rendering, reminder scheduling, notifications, and lifecycle behavior.
- **Views** present the interface and keep visual-only interactions close to the controls they affect.

This structure keeps task data, application workflows, platform-specific behavior, and presentation concerns more clearly separated, making the codebase easier to understand and maintain.
