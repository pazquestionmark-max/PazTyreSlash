// ox_target (ALT-eye) integration. One global vehicle option restricted to wheel bones.
// ox_target resolves the closest wheel bone to the eye ray and passes its bone index to canInteract.
// (Its `coords` argument is a Lua vector3, which the JS runtime cannot decode, so it is not used.)
const PTS_TARGET_NAME = 'paz_tyre_slashing:slash';
const PTS_TARGET_CACHE_MS = 2000;

// Last wheel canInteract approved; onSelect receives no bone, so it reuses this.
let ptsTargetCache = null;

function ptsWheelForBone(entity, bone) {
  if (typeof bone !== 'number' || bone < 0) return null;
  return PtsConfig.wheels.find((wheel) => GetEntityBoneIndexByName(entity, wheel.bone) === bone) || null;
}

function ptsRegisterTarget() {
  exports.ox_target.addGlobalVehicle([
    {
      name: PTS_TARGET_NAME,
      icon: PtsConfig.target.icon,
      label: PtsUtils.message('target_label'),
      bones: PtsConfig.wheels.map((w) => w.bone),
      distance: PtsConfig.target.distance,
      // Hide unless crouched, a blade is in hand and the aimed tyre is intact.
      canInteract: (entity, _distance, _coords, _name, bone) => {
        if (PtsUtils.crouchBlocker() || PtsUtils.weaponBlocker() || PtsUtils.playerBlocker()) return false;
        if (!PtsTyre.isVehicleAllowed(entity)) return false;

        const wheel = ptsWheelForBone(entity, bone);
        if (!wheel || PtsTyre.isBurst(entity, wheel.index)) return false;

        ptsTargetCache = { entity, index: wheel.index, at: GetGameTimer() };
        return true;
      },
      onSelect: (data) => {
        const cached = ptsTargetCache;
        if (!cached || cached.entity !== data.entity || GetGameTimer() - cached.at > PTS_TARGET_CACHE_MS) {
          return PtsUtils.notify('no_tyre', 'error');
        }
        ptsRequestSlash(data.entity, PtsWheelsByIndex.get(cached.index));
      },
    },
  ]);
}

if (PtsConfig.target.enabled) {
  if (GetResourceState('ox_target') === 'started') ptsRegisterTarget();

  // ox_target drops our option when it restarts; re-register when it comes back.
  on('onClientResourceStart', (resourceName) => {
    if (resourceName === 'ox_target') ptsRegisterTarget();
  });
}
