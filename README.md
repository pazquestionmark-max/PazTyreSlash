# paz_tyre_slashing

A standalone QBox resource that lets players slash one specific vehicle tyre. The player needs a blade in their hands and must finish a short timing minigame. The server checks and approves every puncture.

- **Server:** C# (CitizenFX Mono, `netstandard2.0`)
- **Client:** JavaScript
- **UI:** NUI (HTML/CSS/JS)
- **Integrations:** qbx_core, ox_inventory, ox_target, ox_lib

---

## Requirements

| Dependency | Type | Tested version | Used for |
|---|---|---|---|
| FXServer with **OneSync** | required | Linux artifact 35245, game build 3751 | server entity natives (`NetworkGetEntityOwner`, `IsVehicleTyreBurst`, ...) |
| [ox_lib](https://github.com/overextended/ox_lib) | required | 3.39.0 | tyre selection menu for the command, optional notifications |
| [qbx_core](https://github.com/Qbox-project/qbx_core) | required | 1.24.0 | `isLoggedIn` state bag, `Notify` export |
| [ox_inventory](https://github.com/overextended/ox_inventory) | required | 2.47.9 | `GetItemCount`, `GetCurrentWeapon`, `SetDurability` exports |
| [ox_target](https://github.com/overextended/ox_target) | required | 1.18.1 | ALT-eye option (`addGlobalVehicle` with `bones`) |
| qbx_medical | optional | bundled with QBox | sets `Player(src).state.isDead`, which blocks dead or last-stand players |

The resource bundles no copies of these resources. It does not edit QBox core files, and it does not use a database.

---

## Installation

1. Get the resource:
   - **Release build (recommended):** download the zip from GitHub Releases. It already contains `server/bin/PazTyreSlashing.Server.net.dll`.
   - **From source:** clone the repository, then build it (see [Building](#building)).
2. Put the folder in your server's `resources` directory. The folder **must** be named `paz_tyre_slashing`.
3. Edit `config/config.json` if you need to.
4. Add the resource to `server.cfg` **after** its dependencies:

   ```cfg
   ensure ox_lib
   ensure qbx_core
   ensure ox_target
   ensure [ox]        # ox_inventory
   ensure [qbx]
   ensure paz_tyre_slashing
   ```

5. Restart the server, or run `refresh` and then `ensure paz_tyre_slashing`.

The console shows `Started resource paz_tyre_slashing` when it loads. If the config is invalid, the resource prints the reason in red and stays disabled.

---

## Building

You need the [.NET SDK](https://dotnet.microsoft.com/download) 6.0 or newer. The project targets `netstandard2.0`, so the SDK version does not matter at runtime.

### Windows (PowerShell)

```powershell
cd path\to\paz_tyre_slashing\server
dotnet build -c Release
# Output: server\bin\PazTyreSlashing.Server.net.dll
dotnet run --project tests   # optional: session state-machine checks
```

### Linux

```bash
cd paz_tyre_slashing/server
dotnet build -c Release
```

The DLL is platform-independent. You can build it on Windows and copy the whole resource folder to a Linux server. `CitizenFX.Core.Server` and `Microsoft.CSharp` are compile-time references only. FXServer provides both at runtime, so neither is copied to `bin/`.

---

## Usage

### ALT-eye (ox_target)
1. Equip a supported blade so it is in your hands, and crouch.
2. Walk up to a vehicle, hold **ALT** and aim at a tyre.
3. Choose **Slash this tyre**.

The option only appears when all of these are true:
- You are crouched.
- A supported blade is in your hand.
- You are close enough.
- The eye is pointing at a wheel bone.
- That tyre is not already punctured.

### Command
1. Equip a blade, crouch, and stand next to a tyre.
2. Run `/slashtyre`.
3. If only one intact tyre is in reach, the resource picks it. If several are in reach, an ox_lib menu lets you choose. It never picks a tyre at random.

### Minigame
A ring shrinks onto the highlighted puncture spot. Press **E** (or click the spot) while the ring is green. Hit every spot (4 by default) before the timer runs out. Press **Escape** to cancel. Too many misses or running out of time fails the attempt.

---

## Configuration (`config/config.json`)

Every key in the shipped file must stay present, because missing sections disable the resource at startup.

| Key | Default | Description |
|---|---|---|
| `debug` | `false` | Verbose server and client logging |
| `command.enabled` / `command.name` | `true` / `slashtyre` | Chat command |
| `target.enabled` / `icon` / `distance` | `true` / scissors / `1.8` | ALT-eye option |
| `crouch.required` | `true` | Player must be crouched (qbx_crouch, scully_emotemenu stance, or stealth movement) |
| `interaction.tyreDistance` | `1.6` | Client reach (metres) from the player to a wheel bone |
| `interaction.serverMaxDistance` | `8.0` | Server limit (metres) from the player to the **vehicle origin** (see limitations) |
| `notify` | `qbx_core` | `qbx_core` or `ox_lib` |
| `weapons[]` | 7 blades on, knuckles off | `weapon` = GTA weapon name, `item` = ox_inventory item name, `enabled` |
| `inventory.requireItem` | `true` | The player must own the mapped item |
| `inventory.requireEquippedInOxInventory` | `true` | ox_inventory's equipped weapon must match the weapon in hand |
| `durability.enabled` / `amount` | `false` / `2.0` | Remove this much ox_inventory durability per successful slash |
| `wheels[]` | 8 entries | `index` (tyre id for the natives), `bone`, `label` |
| `puncture.onRim` | `false` | `false` deflates the tyre and keeps it on the wheel. `true` shreds it down to the rim |
| `puncture.damage` | `1000.0` | `SET_VEHICLE_TYRE_BURST` damage (1000 = fully deflated) |
| `puncture.keepDriveable` | `true` | If the car was driveable before the puncture, force it driveable afterwards |
| `breakage.enabled` / `chance` | `true` / `0.02` | Chance (0–1) per successful slash that the blade snaps and is removed from ox_inventory |
| `sounds.enabled` / `volume` | `true` / `0.7` | Procedural Web Audio sounds (no audio files) |
| `sounds.stabRange` / `punctureRange` | `15` / `35` | Metres within which other players hear stabs/snap and the tyre pop |
| `vehicles.excludedModels` | `[]` | Model names that can never be slashed |
| `vehicles.excludedTypes` | boat, heli, plane, submarine, train | Server vehicle types that are blocked |
| `animation.dict` / `clip` / `flag` | `melee@knife@streamed_core_fps` / `ground_attack_on_spot` / `1` (loop) | Native animation |
| `animation.minDurationMs` | `1500` | Shortest time the slash animation plays after the minigame starts |
| `animation.walkToTyre` | `true` | Walk to the tyre and face it before slashing |
| `minigame.spots` | `4` | Puncture spots (3–5) |
| `minigame.difficulty` | `normal` | Key into `minigame.difficulties` |
| `minigame.difficulties.*` | easy / normal / hard | `cycleMs` (ring speed), `window` (hit tolerance), `maxMisses` |
| `minigame.timeLimitSeconds` | `10` | Minigame time limit |
| `minigame.inputKey` / `cancelKey` | `E` / `Escape` | Browser `KeyboardEvent.key` values |
| `minigame.minMsPerSpot` | `250` | Server rejects any "success" sent sooner than `spots × this` after the session starts |
| `security.cooldownSeconds` | `10` | Wait after any session ends |
| `security.sessionTimeoutSeconds` | `25` | Server expires sessions older than this. It must be ≥ the minigame limit |
| `security.rateLimitWindowSeconds` / `rateLimitMaxEvents` | `5` / `6` | Per-player limit on this resource's events |
| `security.punctureVerifyRetries` | `3` | Attempts to get the vehicle owner to apply the puncture |
| `messages.*` | English | Every string shown to players |

The ox_inventory item names in `data/weapons.lua` are the upper-case weapon names (`WEAPON_KNIFE` and so on), so the default mapping is one-to-one. If your inventory uses different names, change `item`.

---

## Architecture

```
client/utils.js      config, event names, notifications, player/weapon checks
client/tyre.js       wheel-bone discovery, nearest-tyre search
client/animation.js  walk-to, face, play/stop native animation
client/minigame.js   NUI bridge (resolves exactly once)
client/main.js       session flow, watcher, command, owner-side puncture
client/targeting.js  ox_target option
nui/                 minigame page
server/src/Main.cs            BaseScript: loads config, wires events, expiry tick, argument type checks
server/src/Config.cs          JSON config (DataContractJsonSerializer, no extra DLLs) and validation
server/src/SessionManager.cs  sessions, tyre locks, cooldowns, rate limiting
server/src/Validator.cs       player, weapon/inventory and vehicle/tyre checks, shared by start and finish
server/src/SlashingService.cs state machine orchestration
server/src/TyreService.cs     owner-applied puncture with server verification, durability
```

### Event flow

```
client                                   server (C#)
──────                                   ───────────
local pre-checks
TriggerServerEvent request(netId, tyre) ─▶ rate limit, busy, cooldown, tyre lock
                                          validate player / weapon / inventory / vehicle / tyre
                                          create session {id: GUID, state: InProgress}
        start(sessionId, netId, tyre) ◀── 
walk + face + animation
NUI minigame  (watcher cancels on weapon swap, damage, distance, focus loss)
finish(sessionId, success) ─────────────▶ resolve session (owner + nonce), minimum-time check
                                          re-validate everything
                                          state = Completed (lock released, cooldown)
                                          durability (optional)
                                          send applyPuncture to the vehicle's network owner
  owner: applyPuncture(netId, tyre)   ◀── 
  SetVehicleTyreBurst                     confirm with IsVehicleTyreBurst; retry with the current owner
            notify("punctured")       ◀── 
```

Terminal states are `Completed`, `Cancelled`, `Failed` and `Expired`. `SessionManager.End` moves a session to one of them only once. A second or replayed `finish` finds no live session and is ignored.

| Event | Direction | Arguments |
|---|---|---|
| `paz_tyre_slashing:server:request` | client → server | `netId`, `tyreIndex` |
| `paz_tyre_slashing:server:finish` | client → server | `sessionId`, `success` |
| `paz_tyre_slashing:server:cancel` | client → server | `sessionId` |
| `paz_tyre_slashing:server:stab` | client → server | `sessionId` (max `spots` relays per session) |
| `paz_tyre_slashing:client:start` | server → client | `sessionId`, `netId`, `tyreIndex` |
| `paz_tyre_slashing:client:abort` | server → client | `sessionId`, `messageKey \| null` |
| `paz_tyre_slashing:client:applyPuncture` | server → vehicle owner | `netId`, `tyreIndex`, `onRim` |
| `paz_tyre_slashing:client:notify` | server → client | `messageKey`, `type` |
| `paz_tyre_slashing:client:sound` | server → nearby clients | `sound` (`stab`/`puncture`/`snap`), `x`, `y`, `z`, `range` |
| `paz_tyre_slashing:client:selectTyre` | local only (ox_lib menu) | `{ vehicle, index }` |

---

## Natives and animation used

**Server:** `GetPlayerPed`, `DoesEntityExist`, `GetEntityHealth`, `GetVehiclePedIsIn`, `GetSelectedPedWeapon` (alias of `GET_CURRENT_PED_WEAPON` on the server), `GetHashKey`, `NetworkGetEntityFromNetworkId`, `NetworkGetNetworkIdFromEntity`, `NetworkGetEntityOwner`, `GetEntityType`, `GetVehicleType`, `GetEntityModel`, `GetEntityRoutingBucket`, `GetPlayerRoutingBucket`, `GetEntityCoords`, `IsVehicleTyreBurst`, `LoadResourceFile`, `GetCurrentResourceName`.

**Client:** `GetSelectedPedWeapon`, `GetCurrentPedWeaponEntityIndex`, `IsPedDeadOrDying`, `IsPedCuffed`, `IsPedInAnyVehicle`, `IsPedRagdoll`, `IsPedFalling`, `IsPedSwimming`, `GetGamePool('CVehicle')`, `GetEntityBoneIndexByName`, `GetWorldPositionOfEntityBone`, `IsVehicleTyreBurst`, `GetVehicleClass`, `NetworkGetEntityIsNetworked`, `NetworkGetNetworkIdFromEntity`, `NetworkDoesNetworkIdExist`, `NetToVeh`, `NetworkHasControlOfEntity`, `SetVehicleTyreBurst(vehicle, index, onRim, 1000.0)`, `GetOffsetFromEntityGivenWorldCoords`, `GetOffsetFromEntityInWorldCoords`, `GetHeadingFromVector_2d`, `TaskGoStraightToCoord`, `TaskTurnPedToFaceCoord`, `RequestAnimDict`, `HasAnimDictLoaded`, `TaskPlayAnim`, `StopAnimTask`, `ClearPedSecondaryTask`, `ClearPedTasks`, `RemoveAnimDict`, `SetNuiFocus`, `SendNUIMessage`, `RegisterNuiCallbackType`, `RegisterCommand`, `GetDisplayNameFromVehicleModel`.

**Animation:** `melee@knife@streamed_core_fps` / `ground_attack_on_spot`, with flag `1` (loop). This is a crouched downward stab. If it looks wrong on your game build, use `melee@large_wpn@streamed_core` / `ground_attack_on_spot` instead. The installed scully_emotemenu uses that pair.

**Tyre index to bone mapping** (`SET_VEHICLE_TYRE_BURST` wheel ids):

| Index | Bone | Position |
|---|---|---|
| 0 | `wheel_lf` | front left (bike front) |
| 1 | `wheel_rf` | front right |
| 2 | `wheel_lm1` | middle left (6-wheelers) |
| 3 | `wheel_rm1` | middle right |
| 4 | `wheel_lr` | rear left (bike rear) |
| 5 | `wheel_rr` | rear right |
| 45 | `wheel_lm2` | second middle left (trailers) |
| 47 | `wheel_rm2` | second middle right |

A vehicle only offers the wheels whose bone exists (`GetEntityBoneIndexByName` does not return `-1`). The resource never assumes four wheels.

---

## Security model

- Every client event is untrusted. Arguments arrive as `object` and are type-checked, so malformed payloads are dropped without exceptions.
- All events share a per-player rate limit. Each player can have only one live session, and each tyre can have only one worker (tyre lock).
- Each session has a random GUID nonce. Only the player who started a session can finish or cancel it, and a session can finish only once.
- The server checks everything again when the minigame finishes: player state, the same weapon still in hand, inventory, vehicle existence, routing bucket, distance, and tyre state.
- The client cannot choose what gets punctured. The server sends `applyPuncture` only to the vehicle's current network owner, and only for the session's own net id and tyre. Then it confirms the result from synced state.
- Sessions are removed on expiry, `playerDropped` and `entityRemoved`. They live in memory only, so a resource restart clears all of them, and clients clean up their NUI and animation on `onResourceStop`.

---

## Known limitations and assumptions

- **Weapon state comes from the client.** The server reads the weapon from the ped sync tree (`GetSelectedPedWeapon`) and from ox_inventory's record of the equipped slot. Both come from the client. They stop normal clients and simple event forging, but not a fully modified client.
- **The minigame is not a security boundary.** The server only enforces session ownership, timing (`spots × minMsPerSpot`) and re-validation. A cheater who fakes a "success" still has to pass every server check and wait the minimum time.
- **Server distance is approximate.** FXServer cannot read bone positions, so the server measures to the vehicle origin with `serverMaxDistance` (8 m by default, which covers long trucks). The precise 1.6 m wheel check runs on the client.
- **Crouch is checked on the client only.** qbx_crouch does not replicate its state bag, so the server cannot verify it.
- **Server tyre state uses sync slots, not tyre ids.** FXServer's `IS_VEHICLE_TYRE_BURST` indexes wheels in sync order (a car's rear tyres are ids 4/5 but slots 2/3). The server therefore has no reliable "already punctured" check per tyre; it confirms a puncture by counting burst slots before and after.
- **ox_target `coords` is unusable from JS.** It is a Lua `vector3`, which the JS runtime receives as an undecoded msgpack extension. The option uses the bone index ox_target passes instead.
- **The server cannot count wheels.** A forged request for a wheel index the vehicle does not have passes the server checks. Nothing gets punctured, and the puncture confirmation then fails.
- **The vehicle needs a network owner.** If no player owns the vehicle, nobody can apply the puncture, and the player sees `server_error`.
- **No database persistence.** A punctured tyre is part of GTA's synced vehicle state, so it survives a restart of this resource. It does not survive a server restart or a vehicle despawn and respawn. Whether qbx_vehicles or garages keep tyre damage depends on those resources. A persistence adapter was left out on purpose, so the resource needs no database schema.
- **Hash comparisons are unsigned** (`>>> 0` on the client, `unchecked((int)...)` on the server). This avoids sign mismatches between natives.
- **Wheel ids 45 and 47** follow the native documentation and still need checking in-game on a 6-wheel trailer.

---

## Testing

### Performed (2026-09-17, Linux FXServer 35245, isolated instance on 127.0.0.1)

| Test | Result |
|---|---|
| `dotnet build -c Release` (.NET SDK 8.0.425, `CitizenFX.Core.Server` 1.0.26803) | ✅ 0 warnings, 0 errors |
| FXServer loads the manifest, the Mono assembly instantiates `PazTyreSlashing.Server.Main`, and the config parses | ✅ no config error, `Started resource paz_tyre_slashing` |
| Forged or malformed server events with no player source or wrong argument types (`request`, `finish`, `cancel`, `entityRemoved`, `playerDropped`) | ✅ rejected, no exceptions |
| `server/tests`: 18 state-machine checks (rate limit, forged/foreign session id, replayed completion, double completion, tyre lock, cooldown, expiry, disconnect cleanup) | ✅ all pass |
| `node --check` on all client JS | ✅ |

The smoke test used stub ox_lib, qbx_core, ox_inventory and ox_target resources. It did not use the real ones.

### Not yet performed (needs a GTA V client connected to the server)

Nothing below has been run in-game yet:

- **Weapon:** no knife, knife holstered, knife in hand, unsupported weapon, weapon swap during the animation.
- **Targeting:** each of the 4 standard tyres, a 6-wheel truck or trailer, out of range, a missing wheel.
- **Minigame:** success punctures only the chosen tyre, failure, cancel, timeout, key mashing, NUI focus loss.
- **Security with real players:** out-of-range and invalid net ids, a cooldown bypass attempt.
- **Multiplayer:** two players on the same tyre, disconnect mid-slash, vehicle deleted mid-slash, owner migration, other players see the puncture.
- **Framework:** the real ox_inventory exports, ox_target option visibility, and a resource restart while a player is mid-slash.

---

## Contributing and issues

Open a GitHub issue with:
- Your FXServer artifact, game build, and the versions of qbx_core, ox_inventory, ox_target and ox_lib.
- Your `config.json` changes.
- Server and F8 console output with `"debug": true`.

Pull requests are welcome. Keep the server authoritative and add a check in `server/tests` when you change session logic. Record your changes in `CHANGELOG.md` and bump `version` in `fxmanifest.lua`.

## License

MIT. See [LICENSE](LICENSE).
