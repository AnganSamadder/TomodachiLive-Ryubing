# TomodachiLive-Ryubing

Rio-owned Ryubing-derived emulator integration fork/workspace for TomodachiLive.

## Purpose

This repository is the dedicated emulator-side workspace for TomodachiLive remote-control experiments. It exists so TomodachiLive can keep product/server/client code separate from emulator patches while still tracking the integration as a first-class project component.

## Policy

- Prefer clean, purpose-built integration for TomodachiLive over accidental OS-level input hacks.
- Use lawful, license-compatible source only.
- Verify the Ryubing source host, license, and commit before importing code.
- Keep all TomodachiLive-specific changes isolated behind explicit bridge/input provider boundaries.

## Target integration shape

```txt
TomodachiLive clients
  -> Auth.js + Convex durable state
  -> Cloudflare Tunnel HTTPS/WSS
  -> TomodachiLive local gateway
  -> C# Windows host bridge
  -> localhost/named-pipe IPC
  -> emulator-side BridgeGamepadDriver / BridgeTouchProvider
```

## Planned patches

1. Import/pin lawful Ryubing source once source/license are verified.
2. Add an IPC-backed gamepad input provider.
3. Add an IPC-backed touch input path if Tomodachi Life requires touch for day-one play.
4. Keep normal local controller support intact.
5. Add build documentation for the exact pinned upstream source.
6. Add minimal tests or harnesses for bridge protocol parsing where the emulator codebase allows it.

## Linked project

Main app repository: https://github.com/AnganSamadder/TomodachiLive
