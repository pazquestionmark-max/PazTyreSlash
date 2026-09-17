// Tyre discovery via wheel bones. Vehicles expose only the wheel bones they actually have,
// so trucks, trailers and bikes are handled by skipping bones that resolve to -1.
const PTS_VEHICLE_SCAN_RADIUS = 10.0;
const PTS_EXCLUDED_MODELS = new Set(PtsConfig.vehicles.excludedModels.map((m) => GetHashKey(m) >>> 0));

// Client-side mirror of vehicles.excludedTypes using vehicle classes (the server re-checks by type).
const PTS_CLASS_TYPES = { 14: 'boat', 15: 'heli', 16: 'plane', 21: 'train' };

const PtsTyre = {
  isVehicleAllowed(vehicle) {
    if (!DoesEntityExist(vehicle) || GetEntityType(vehicle) !== 2) return false;
    if (!NetworkGetEntityIsNetworked(vehicle)) return false;
    if (PTS_EXCLUDED_MODELS.has(GetEntityModel(vehicle) >>> 0)) return false;
    const type = PTS_CLASS_TYPES[GetVehicleClass(vehicle)];
    return !(type && PtsConfig.vehicles.excludedTypes.includes(type));
  },

  // Every configured wheel this vehicle really has, with its world position.
  wheels(vehicle) {
    const result = [];
    for (const wheel of PtsConfig.wheels) {
      const bone = GetEntityBoneIndexByName(vehicle, wheel.bone);
      if (bone === -1) continue;
      result.push({ ...wheel, position: GetWorldPositionOfEntityBone(vehicle, bone) });
    }
    return result;
  },

  isBurst: (vehicle, index) => IsVehicleTyreBurst(vehicle, index, false),

  // Closest wheel of one vehicle to a world point (used by ox_target hit coords).
  closestWheel(vehicle, coords, maxDistance) {
    let best = null;
    for (const wheel of PtsTyre.wheels(vehicle)) {
      const distance = PtsUtils.distance(wheel.position, coords);
      if (distance <= maxDistance && (!best || distance < best.distance)) best = { ...wheel, distance };
    }
    return best;
  },

  // All wheels within reach of the player across nearby vehicles, closest first.
  wheelsNearPlayer() {
    const playerCoords = GetEntityCoords(PlayerPedId(), false);
    const found = [];
    let vehicleNearby = false;

    for (const vehicle of GetGamePool('CVehicle')) {
      if (PtsUtils.distance(GetEntityCoords(vehicle, false), playerCoords) > PTS_VEHICLE_SCAN_RADIUS) continue;
      if (!PtsTyre.isVehicleAllowed(vehicle)) continue;
      vehicleNearby = true;

      for (const wheel of PtsTyre.wheels(vehicle)) {
        const distance = PtsUtils.distance(wheel.position, playerCoords);
        if (distance <= PtsConfig.interaction.tyreDistance) found.push({ vehicle, wheel, distance });
      }
    }

    found.sort((a, b) => a.distance - b.distance);
    return { vehicleNearby, found };
  },
};
