# Sv³ — Stardew Valley Second View

SMAPI mod for Stardew Valley 1.6 that renders a second, vertical camera view of the player into a separate window — made for capturing a portrait feed (phone-format stream) alongside normal gameplay.

The mirror window is display-only: it takes no input, never blocks the game thread, and presents via a shared OpenGL context with a GPU-to-GPU blit (no pixel readback, no vsync).

## Features

- Independent camera: follows the player, clamps to map edges, own zoom level.
- Independent UI scale, with the HUD's health/energy bars and notifications lifted above the toolbar for narrow layouts.
- Menus and dialogue re-composited to fit the vertical view (enlarged, centered, full-screen fade).
- Frame pacing, window size and spawn position all configurable.

## Install

1. Install [SMAPI](https://smapi.io).
2. Build with `dotnet build -c Release` (auto-deploys to your game's `Mods` folder), or drop a release zip into `Mods/`.
3. Launch via SMAPI. The second window appears at the title screen and starts rendering once you're in a save.

## Config (`Mods/SecondView/config.json`)

| Key | Default | Meaning |
| --- | --- | --- |
| `WindowWidth` / `WindowHeight` | 1080 x 1312 | Mirror window size in pixels. |
| `WindowX` / `WindowY` | unset | Spawn position (use your monitor layout's coordinates); unset = anywhere. |
| `Zoom` | 0.75 | Mirror camera zoom. 1.0 = 100%; below 0.75 goes past the game's own max zoom-out. |
| `UiScale` | 0.75 | Mirror HUD scale. On-screen UI size is `UiScale x Zoom`. |
| `HudLift` | 112 | How far (UI px) to lift the health/energy bars and notifications above the toolbar. |
| `FrameInterval` | 1 | Render the mirror every Nth frame (2 = 30fps mirror at 60fps game). |
| `ShowHud` | true | Draw the HUD in the mirror at all. |

Edit the config and restart the game — no rebuild needed.

## OBS

Capture the window `Stardew Valley - Second View` with Window Capture. The window can be ignored/minimized-behind — it keeps rendering.
