// Slashing flow: local pre-checks -> server request -> approach + animation + minigame -> server finish.
const PTS_REQUEST_TIMEOUT_MS = 5000;
const PTS_WATCH_INTERVAL_MS = 200;

let ptsPendingUntil = 0;
let ptsActive = null; // { sessionId, netId, tyreIndex, vehicle, weapon, watcher }

function ptsIsCurrent(sessionId) {
  return ptsActive !== null && ptsActive.sessionId === sessionId;
}

// Cheap checks so players get instant feedback; the server repeats all of them authoritatively.
function ptsLocalBlocker(vehicle, wheel) {
  const blocker = PtsUtils.playerBlocker() || PtsUtils.weaponBlocker() || PtsUtils.crouchBlocker();
  if (blocker) return blocker;
  if (!PtsTyre.isVehicleAllowed(vehicle) || !PtsWheelsByIndex.has(wheel.index)) return 'invalid_target';
  if (PtsTyre.isBurst(vehicle, wheel.index)) return 'already_punctured';
  return null;
}

function ptsRequestSlash(vehicle, wheel) {
  if (ptsActive || GetGameTimer() < ptsPendingUntil) return PtsUtils.notify('busy', 'error');

  const blocker = ptsLocalBlocker(vehicle, wheel);
  if (blocker) return PtsUtils.notify(blocker, 'error');

  ptsPendingUntil = GetGameTimer() + PTS_REQUEST_TIMEOUT_MS;
  TriggerServerEvent(PtsEvents.request, NetworkGetNetworkIdFromEntity(vehicle), wheel.index);
}

// Tear down everything local. Safe to call repeatedly and from any stage.
function ptsCleanup() {
  if (!ptsActive) return;
  clearInterval(ptsActive.watcher);
  ptsActive = null;
  PtsMinigame.finish('aborted');
  PtsAnimation.stop();
}

function ptsCancelLocally(messageKey) {
  if (!ptsActive) return;
  const { sessionId } = ptsActive;
  ptsCleanup();
  TriggerServerEvent(PtsEvents.cancel, sessionId);
  if (messageKey) PtsUtils.notify(messageKey, 'error');
}

// Cancels the action if the player swaps weapons, gets hurt, walks off or loses NUI focus.
function ptsStartWatcher(sessionId) {
  return setInterval(() => {
    if (!ptsIsCurrent(sessionId)) return;
    const { vehicle, tyreIndex, weapon, wheelPosition } = ptsActive;

    if (PtsUtils.playerBlocker() || PtsUtils.weaponBlocker() || GetSelectedPedWeapon(PlayerPedId()) >>> 0 !== weapon) {
      return ptsCancelLocally('cancelled');
    }
    if (!DoesEntityExist(vehicle) || PtsTyre.isBurst(vehicle, tyreIndex)) return ptsCancelLocally('invalid_target');
    if (PtsUtils.distance(GetEntityCoords(PlayerPedId(), false), wheelPosition) > PtsConfig.interaction.tyreDistance + 2.0) {
      return ptsCancelLocally('too_far');
    }
    if (PtsMinigame.isOpen() && !IsNuiFocused()) PtsMinigame.finish('cancelled');
  }, PTS_WATCH_INTERVAL_MS);
}

onNet(PtsEvents.start, async (sessionId, netId, tyreIndex) => {
  ptsPendingUntil = 0;
  if (ptsActive) return TriggerServerEvent(PtsEvents.cancel, sessionId);

  const vehicle = NetworkDoesNetworkIdExist(netId) ? NetToVeh(netId) : 0;
  const wheel = vehicle ? PtsTyre.wheels(vehicle).find((w) => w.index === tyreIndex) : null;
  if (!wheel) {
    TriggerServerEvent(PtsEvents.cancel, sessionId);
    return PtsUtils.notify('invalid_target', 'error');
  }

  ptsActive = {
    sessionId,
    netId,
    tyreIndex,
    vehicle,
    wheelPosition: wheel.position,
    weapon: GetSelectedPedWeapon(PlayerPedId()) >>> 0,
  };
  ptsActive.watcher = ptsStartWatcher(sessionId);
  PtsUtils.notify('started', 'inform');

  await PtsAnimation.approach(vehicle, wheel.position, () => ptsIsCurrent(sessionId));
  if (!ptsIsCurrent(sessionId)) return;

  if (!(await PtsAnimation.play())) return ptsCancelLocally('server_error');
  if (!ptsIsCurrent(sessionId)) return;

  const startedAt = GetGameTimer();
  const result = await PtsMinigame.run();
  if (!ptsIsCurrent(sessionId)) return; // aborted by watcher or server while playing

  // Let the slash animation run for at least the configured duration.
  const remaining = PtsConfig.animation.minDurationMs - (GetGameTimer() - startedAt);
  if (result === 'success' && remaining > 0) await PtsUtils.wait(remaining);
  if (!ptsIsCurrent(sessionId)) return;

  ptsCleanup();

  if (result === 'success') {
    PtsUtils.notify('minigame_success', 'success');
    TriggerServerEvent(PtsEvents.finish, sessionId, true);
  } else if (result === 'cancelled') {
    PtsUtils.notify('cancelled', 'inform');
    TriggerServerEvent(PtsEvents.cancel, sessionId);
  } else {
    TriggerServerEvent(PtsEvents.finish, sessionId, false);
  }
});

// Server ended the session (failure, expiry, cancel confirmation, vehicle deleted).
onNet(PtsEvents.abort, (sessionId, messageKey) => {
  if (ptsIsCurrent(sessionId)) ptsCleanup();
  if (messageKey) PtsUtils.notify(messageKey, messageKey === 'cancelled' ? 'inform' : 'error');
});

onNet(PtsEvents.notify, (messageKey, type) => {
  ptsPendingUntil = 0;
  PtsUtils.notify(messageKey, type);
});

// Only the network owner can change tyre state; the server picks the owner and verifies the result.
onNet(PtsEvents.applyPuncture, (netId, tyreIndex, onRim) => {
  if (!PtsWheelsByIndex.has(tyreIndex) || !NetworkDoesNetworkIdExist(netId)) return;
  const vehicle = NetToVeh(netId);
  if (!DoesEntityExist(vehicle) || !NetworkHasControlOfEntity(vehicle)) return;
  if (PtsTyre.isBurst(vehicle, tyreIndex)) return;

  const wasDriveable = IsVehicleDriveable(vehicle, false);
  SetVehicleTyreBurst(vehicle, tyreIndex, !!onRim, PtsConfig.puncture.damage);

  // A popped tyre must never leave the car undriveable; restore it if the burst flipped that state.
  if (PtsConfig.puncture.keepDriveable && wasDriveable) {
    setTimeout(() => {
      if (DoesEntityExist(vehicle) && !IsVehicleDriveable(vehicle, false)) SetVehicleUndriveable(vehicle, false);
    }, 500);
  }
});

// Positional sound from the server; volume falls off linearly to zero at the event's range.
onNet(PtsEvents.sound, (sound, x, y, z, range) => {
  if (!PtsConfig.sounds.enabled) return;
  const distance = PtsUtils.distance(GetEntityCoords(PlayerPedId(), false), [x, y, z]);
  const falloff = 1 - distance / range;
  if (falloff > 0) SendNUIMessage({ action: 'sound', sound, volume: PtsConfig.sounds.volume * falloff });
});

on('onResourceStop', (resourceName) => {
  if (resourceName !== PTS_RESOURCE) return;
  if (PtsMinigame.isOpen()) SetNuiFocus(false, false);
  ptsCleanup();
});

if (PtsConfig.command.enabled) {
  RegisterCommand(
    PtsConfig.command.name,
    () => {
      const blocker = PtsUtils.playerBlocker() || PtsUtils.weaponBlocker() || PtsUtils.crouchBlocker();
      if (blocker) return PtsUtils.notify(blocker, 'error');

      const { vehicleNearby, found } = PtsTyre.wheelsNearPlayer();
      if (!vehicleNearby) return PtsUtils.notify('no_vehicle', 'error');
      if (found.length === 0) return PtsUtils.notify('no_tyre', 'error');

      const intact = found.filter((f) => !PtsTyre.isBurst(f.vehicle, f.wheel.index));
      if (intact.length === 0) return PtsUtils.notify('already_punctured', 'error');
      if (intact.length === 1) return ptsRequestSlash(intact[0].vehicle, intact[0].wheel);

      // Several tyres in reach: let the player choose instead of guessing.
      exports.ox_lib.registerContext({
        id: 'paz_tyre_slashing_select',
        title: PtsUtils.message('select_tyre'),
        options: intact.map((f) => ({
          title: f.wheel.label,
          description: `${GetDisplayNameFromVehicleModel(GetEntityModel(f.vehicle))} · ${f.distance.toFixed(1)}m`,
          icon: PtsConfig.target.icon,
          event: PtsEvents.selectTyre,
          args: { vehicle: f.vehicle, index: f.wheel.index },
        })),
      });
      exports.ox_lib.showContext('paz_tyre_slashing_select');
    },
    false
  );
}

on(PtsEvents.selectTyre, (args) => {
  const wheel = args && PtsWheelsByIndex.get(args.index);
  if (!wheel || !DoesEntityExist(args.vehicle)) return PtsUtils.notify('invalid_target', 'error');

  const position = PtsTyre.wheels(args.vehicle).find((w) => w.index === args.index);
  if (!position) return PtsUtils.notify('invalid_target', 'error');
  if (PtsUtils.distance(GetEntityCoords(PlayerPedId(), false), position.position) > PtsConfig.interaction.tyreDistance) {
    return PtsUtils.notify('too_far', 'error');
  }
  ptsRequestSlash(args.vehicle, wheel);
});
