// Shared client helpers. FiveM loads every client script into one global scope, so names are prefixed.
const PTS_RESOURCE = GetCurrentResourceName();
const PtsConfig = JSON.parse(LoadResourceFile(PTS_RESOURCE, 'config/config.json'));

const PtsEvents = {
  // Client -> server
  request: 'paz_tyre_slashing:server:request',
  finish: 'paz_tyre_slashing:server:finish',
  cancel: 'paz_tyre_slashing:server:cancel',
  stab: 'paz_tyre_slashing:server:stab',
  // Server -> client
  start: 'paz_tyre_slashing:client:start',
  abort: 'paz_tyre_slashing:client:abort',
  applyPuncture: 'paz_tyre_slashing:client:applyPuncture',
  notify: 'paz_tyre_slashing:client:notify',
  sound: 'paz_tyre_slashing:client:sound',
  // Local only (ox_lib context menu selection)
  selectTyre: 'paz_tyre_slashing:client:selectTyre',
};

// GTA hashes come back signed or unsigned depending on the native; compare unsigned.
const PtsAllowedWeapons = new Set(
  PtsConfig.weapons.filter((w) => w.enabled).map((w) => GetHashKey(w.weapon) >>> 0)
);
const PtsWheelsByIndex = new Map(PtsConfig.wheels.map((w) => [w.index, w]));

const PtsUtils = {
  wait: (ms) => new Promise((resolve) => setTimeout(resolve, ms)),

  message: (key) => PtsConfig.messages[key] || key,

  notify(key, type = 'inform') {
    const text = PtsUtils.message(key);
    try {
      if (PtsConfig.notify === 'ox_lib') exports.ox_lib.notify({ description: text, type });
      else exports.qbx_core.Notify(text, type);
    } catch (err) {
      console.log(`[paz_tyre_slashing] notify failed: ${err}`);
    }
  },

  debug(...args) {
    if (PtsConfig.debug) console.log('[paz_tyre_slashing]', ...args);
  },

  distance(a, b) {
    return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]);
  },

  // qbx_crouch sets state.crouch (not replicated); scully_emotemenu sets state.stance (1 = stealth, 2 = crouch).
  isCrouched() {
    const state = LocalPlayer.state;
    return state.crouch === true || state.stance === 1 || state.stance === 2 || GetPedStealthMovement(PlayerPedId());
  },

  crouchBlocker: () => (PtsConfig.crouch.required && !PtsUtils.isCrouched() ? 'not_crouched' : null),

  // Returns an error message key, or null when the player may act.
  playerBlocker() {
    const ped = PlayerPedId();
    if (LocalPlayer.state.isDead || IsPedDeadOrDying(ped, true) || IsPedCuffed(ped)) return 'incapacitated';
    if (IsPedInAnyVehicle(ped, false) || IsPedRagdoll(ped) || IsPedFalling(ped) || IsPedSwimming(ped)) return 'incapacitated';
    if (LocalPlayer.state.invBusy) return 'busy';
    return null;
  },

  // A supported blade must be selected AND physically in the hand (weapon object exists).
  weaponBlocker() {
    const ped = PlayerPedId();
    const selected = GetSelectedPedWeapon(ped) >>> 0;
    if (selected === GetHashKey('WEAPON_UNARMED') >>> 0) return 'no_weapon';
    if (!PtsAllowedWeapons.has(selected)) return 'weapon_not_equipped';
    if (GetCurrentPedWeaponEntityIndex(ped, 0) === 0) return 'weapon_not_equipped';
    return null;
  },
};
