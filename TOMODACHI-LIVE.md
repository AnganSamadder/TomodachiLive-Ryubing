# TomodachiLive Integration Notes

This branch is Rio's TomodachiLive integration branch for Ryubing.

## Source baseline

- Upstream source: `https://git.ryujinx.app/projects/Ryubing.git`
- Baseline commit: `153d632ee26a80e1de48cf49e599f628ebb9b4cc`
- License observed at baseline: MIT (`LICENSE.txt`)

## Goal

Add a clean, local-only input bridge for TomodachiLive so the main app can control Ryubing without sending arbitrary OS keyboard/mouse input.

Target shape:

```txt
TomodachiLive clients
  -> Auth.js + Convex control state
  -> local gateway
  -> C# Windows host bridge
  -> local IPC / named pipe
  -> Ryubing input provider
```

## Rules

- Keep normal Ryubing controller support intact.
- Keep TomodachiLive-specific behavior isolated behind explicit bridge/input-provider files.
- Prefer local-only named pipe or localhost IPC.
- Do not expose emulator control directly to the public network.
- Preserve MIT license notices and upstream attribution.

## First implementation targets

1. Inspect existing input driver/provider interfaces.
2. Identify where SDL gamepad drivers are registered.
3. Add a disabled-by-default bridge input provider behind config/CLI flag.
4. Add support for normalized button/stick/touch events.
5. Verify with a dry-run local bridge before using real remote clients.
