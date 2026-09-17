# Changelog

This project follows [Semantic Versioning](https://semver.org/). The version in `fxmanifest.lua` matches the latest entry.

## [1.1.0] - 2026-09-17

### Added
- Player must be crouched (qbx_crouch `crouch`, scully_emotemenu `stance`, or stealth movement) for both the command and the ALT-eye option.
- Procedural stab, tyre-pop/hiss and blade-snap sounds, heard by nearby players with distance falloff.
- Small configurable chance (`breakage.chance`, default 2%) that the blade snaps and is removed from ox_inventory.
- `puncture.damage` and `puncture.keepDriveable` options.

### Fixed
- ALT-eye option never showed: ox_target's `coords` argument is a Lua vector3 the JS runtime cannot decode. The wheel is now resolved from the bone index ox_target passes.
- "puncture could not be confirmed" for rear tyres: the server `IS_VEHICLE_TYRE_BURST` reads synced wheel slots, not native tyre ids. Confirmation now counts burst slots before and after.

## [1.0.0] - 2026-09-17

### Added
- Slash an individual tyre through ox_target (ALT-eye) or the `/slashtyre` command.
- Server-authoritative C# session state machine with nonces, cooldowns, rate limiting and expiry.
- Equipped-weapon checks via ped sync state and ox_inventory.
- NUI timing minigame with configurable spots, difficulty, time limit and keys.
- Owner-applied, server-verified tyre puncture synchronisation.
