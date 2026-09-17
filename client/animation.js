// Positioning and the native slash animation. Nothing here freezes the ped, so a stop always restores movement.
const PTS_APPROACH_TIMEOUT_MS = 3000;
const PTS_STAND_OFF = 0.75;

const PtsAnimation = {
  // Walk to a point just outside the wheel, then face it.
  async approach(vehicle, wheelPosition, isStillActive) {
    const ped = PlayerPedId();
    const [wx, wy, wz] = wheelPosition;

    if (PtsConfig.animation.walkToTyre) {
      // Push the stand point outward on the side of the vehicle the wheel is on.
      const local = GetOffsetFromEntityGivenWorldCoords(vehicle, wx, wy, wz);
      const side = local[0] >= 0 ? 1 : -1;
      const [sx, sy, sz] = GetOffsetFromEntityInWorldCoords(vehicle, local[0] + side * PTS_STAND_OFF, local[1], local[2]);

      if (PtsUtils.distance(GetEntityCoords(ped, false), [sx, sy, sz]) > 0.4) {
        const heading = GetHeadingFromVector_2d(wx - sx, wy - sy);
        TaskGoStraightToCoord(ped, sx, sy, sz, 1.0, PTS_APPROACH_TIMEOUT_MS, heading, 0.1);

        const deadline = GetGameTimer() + PTS_APPROACH_TIMEOUT_MS;
        while (GetGameTimer() < deadline && isStillActive()) {
          if (PtsUtils.distance(GetEntityCoords(ped, false), [sx, sy, sz]) <= 0.35) break;
          await PtsUtils.wait(100);
        }
      }
    }

    if (!isStillActive()) return;
    TaskTurnPedToFaceCoord(ped, wx, wy, wz, 800);
    await PtsUtils.wait(800);
  },

  async play() {
    const { dict, clip, flag } = PtsConfig.animation;
    RequestAnimDict(dict);

    const deadline = GetGameTimer() + 3000;
    while (!HasAnimDictLoaded(dict)) {
      if (GetGameTimer() > deadline) {
        console.log(`[paz_tyre_slashing] animation dictionary failed to load: ${dict}`);
        return false;
      }
      await PtsUtils.wait(10);
    }

    TaskPlayAnim(PlayerPedId(), dict, clip, 8.0, -8.0, -1, flag, 0, false, false, false);
    return true;
  },

  stop() {
    const { dict, clip } = PtsConfig.animation;
    const ped = PlayerPedId();
    StopAnimTask(ped, dict, clip, 1.0);
    ClearPedSecondaryTask(ped);
    // Clears a pending walk/turn task if we were stopped during approach.
    if (!IsPedInAnyVehicle(ped, false)) ClearPedTasks(ped);
    RemoveAnimDict(dict);
  },
};
