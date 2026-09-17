// Bridge to the NUI minigame. Resolves exactly once with 'success' | 'failed' | 'cancelled' | 'timeout' | 'aborted'.
// The result is only a request to the server; it never punctures anything by itself.
const PTS_MINIGAME_RESULTS = new Set(['success', 'failed', 'cancelled', 'timeout']);
let ptsMinigameResolve = null;
let ptsMinigameTimer = null;

RegisterNuiCallbackType('minigameResult');
on('__cfx_nui:minigameResult', (data, cb) => {
  cb({ ok: true });
  const result = data && PTS_MINIGAME_RESULTS.has(data.result) ? data.result : 'failed';
  PtsMinigame.finish(result);
});

// Each successful stab: relay the sound to nearby players (the local player already heard it in the NUI).
RegisterNuiCallbackType('minigameHit');
on('__cfx_nui:minigameHit', (_data, cb) => {
  cb({ ok: true });
  if (PtsConfig.sounds.enabled && ptsActive) TriggerServerEvent(PtsEvents.stab, ptsActive.sessionId);
});

const PtsMinigame = {
  isOpen: () => ptsMinigameResolve !== null,

  run() {
    if (ptsMinigameResolve) return Promise.resolve('failed');

    const settings = PtsConfig.minigame;
    const difficulty = settings.difficulties[settings.difficulty] || settings.difficulties.normal;

    return new Promise((resolve) => {
      ptsMinigameResolve = resolve;
      SendNUIMessage({
        action: 'open',
        spots: settings.spots,
        timeLimitMs: settings.timeLimitSeconds * 1000,
        inputKey: settings.inputKey,
        cancelKey: settings.cancelKey,
        cycleMs: difficulty.cycleMs,
        window: difficulty.window,
        maxMisses: difficulty.maxMisses,
        soundVolume: PtsConfig.sounds.enabled ? PtsConfig.sounds.volume : 0,
      });
      SetNuiFocus(true, true);

      // Safety net if the NUI never answers (crashed page, stolen focus).
      ptsMinigameTimer = setTimeout(() => PtsMinigame.finish('timeout'), settings.timeLimitSeconds * 1000 + 1500);
    });
  },

  finish(result) {
    if (!ptsMinigameResolve) return;
    const resolve = ptsMinigameResolve;
    ptsMinigameResolve = null;
    clearTimeout(ptsMinigameTimer);
    SetNuiFocus(false, false);
    SendNUIMessage({ action: 'close' });
    resolve(result);
  },
};
