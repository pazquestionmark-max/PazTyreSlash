// Timing minigame: a ring shrinks onto the highlighted spot; stab (key or click) while it overlaps.
(() => {
  const RING_START_SCALE = 2.2;
  const SPOT_RADIUS_PCT = 38; // distance of spots from the tyre centre, in % of tyre size

  const root = document.getElementById('minigame');
  const spotsEl = document.getElementById('spots');
  const progressEl = document.getElementById('progress');
  const timerFill = document.getElementById('timer-fill');
  const hintEl = document.getElementById('hint');
  const feedbackEl = document.getElementById('feedback');

  let game = null;

  const resourceName = typeof GetParentResourceName === 'function' ? GetParentResourceName() : 'paz_tyre_slashing';

  function post(endpoint, body) {
    fetch(`https://${resourceName}/${endpoint}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json; charset=UTF-8' },
      body: JSON.stringify(body),
    }).catch(() => {});
  }

  const send = (result) => post('minigameResult', { result });

  // Procedural sounds via Web Audio: no audio files to ship or license.
  const Sound = (() => {
    let ctx = null;

    function context() {
      ctx = ctx || new (window.AudioContext || window.webkitAudioContext)();
      if (ctx.state === 'suspended') ctx.resume();
      return ctx;
    }

    function noise(c, seconds) {
      const buffer = c.createBuffer(1, Math.ceil(c.sampleRate * seconds), c.sampleRate);
      const data = buffer.getChannelData(0);
      for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
      const source = c.createBufferSource();
      source.buffer = buffer;
      return source;
    }

    // Gain node that ramps from `peak` down to silence over `decay` seconds, starting at `at`.
    function envelope(c, peak, at, attack, decay) {
      const gain = c.createGain();
      gain.gain.setValueAtTime(0.0001, at);
      gain.gain.exponentialRampToValueAtTime(Math.max(peak, 0.0002), at + attack);
      gain.gain.exponentialRampToValueAtTime(0.0001, at + attack + decay);
      gain.connect(c.destination);
      return gain;
    }

    function filtered(c, type, frequency, q) {
      const filter = c.createBiquadFilter();
      filter.type = type;
      filter.frequency.value = frequency;
      filter.Q.value = q;
      return filter;
    }

    function thump(c, volume, at, from, to, length) {
      const osc = c.createOscillator();
      osc.frequency.setValueAtTime(from, at);
      osc.frequency.exponentialRampToValueAtTime(to, at + length);
      osc.connect(envelope(c, volume, at, 0.004, length));
      osc.start(at);
      osc.stop(at + length + 0.05);
    }

    function burst(c, volume, at, type, frequency, q, attack, length) {
      const src = noise(c, attack + length + 0.05);
      src.connect(filtered(c, type, frequency, q)).connect(envelope(c, volume, at, attack, length));
      src.start(at);
    }

    const sounds = {
      // Blade punching into rubber: a dull thud plus a short tearing scrape.
      stab(c, v) {
        const t = c.currentTime;
        thump(c, v * 0.9, t, 180, 60, 0.09);
        burst(c, v * 0.6, t, 'bandpass', 1800, 1.2, 0.003, 0.12);
      },
      // Tyre pop followed by escaping air.
      puncture(c, v) {
        const t = c.currentTime;
        thump(c, v, t, 120, 35, 0.18);
        burst(c, v, t, 'lowpass', 2200, 0.7, 0.002, 0.08);
        burst(c, v * 0.45, t + 0.05, 'bandpass', 5200, 0.6, 0.04, 1.8);
      },
      // Metal blade cracking: sharp click plus a short ring.
      snap(c, v) {
        const t = c.currentTime;
        burst(c, v, t, 'highpass', 3500, 0.8, 0.001, 0.04);
        [2900, 4300].forEach((frequency) => {
          const osc = c.createOscillator();
          osc.type = 'triangle';
          osc.frequency.value = frequency;
          osc.connect(envelope(c, v * 0.25, t, 0.002, 0.3));
          osc.start(t);
          osc.stop(t + 0.35);
        });
      },
    };

    return {
      play(name, volume) {
        if (!sounds[name] || !(volume > 0)) return;
        try {
          sounds[name](context(), Math.min(1, volume));
        } catch (err) {
          console.log(`[paz_tyre_slashing] sound failed: ${err}`);
        }
      },
    };
  })();

  function ringScale(now) {
    const phase = ((now - game.cycleStart) % game.cycleMs) / game.cycleMs;
    return RING_START_SCALE - phase * RING_START_SCALE;
  }

  function inWindow(now) {
    return Math.abs(ringScale(now) - 1) <= game.window;
  }

  function render() {
    spotsEl.innerHTML = '';
    progressEl.innerHTML = '';

    game.positions.forEach((angle, i) => {
      const spot = document.createElement('button');
      spot.className = 'spot' + (i < game.current ? ' done' : i === game.current ? ' current' : '');
      spot.style.left = `${50 + SPOT_RADIUS_PCT * Math.cos(angle)}%`;
      spot.style.top = `${50 + SPOT_RADIUS_PCT * Math.sin(angle)}%`;
      spot.setAttribute('aria-label', `Puncture spot ${i + 1}`);
      spot.tabIndex = -1;
      if (i === game.current) {
        const ring = document.createElement('div');
        ring.className = 'ring';
        spot.appendChild(ring);
        game.ringEl = ring;
      }
      spot.addEventListener('mousedown', (e) => {
        e.preventDefault();
        if (i === game.current) stab();
      });
      spotsEl.appendChild(spot);

      const pip = document.createElement('div');
      pip.className = 'pip' + (i < game.current ? ' done' : '');
      progressEl.appendChild(pip);
    });
  }

  function flash(text, good) {
    feedbackEl.textContent = text;
    feedbackEl.className = good ? 'good' : 'bad';
  }

  function end(result) {
    if (!game || game.finished) return; // repeated input can never send twice
    game.finished = true;
    cancelAnimationFrame(game.frame);
    send(result);
  }

  function stab() {
    if (!game || game.finished) return;
    const now = performance.now();

    if (inWindow(now)) {
      game.current += 1;
      Sound.play('stab', game.soundVolume);
      post('minigameHit', {});
      flash('Stabbed!', true);
      if (game.current >= game.positions.length) {
        render();
        return end('success');
      }
      game.cycleStart = now;
      render();
    } else {
      game.misses += 1;
      flash(game.misses > game.maxMisses ? 'The blade slipped.' : 'Missed!', false);
      if (game.misses > game.maxMisses) end('failed');
    }
  }

  function loop(now) {
    if (!game || game.finished) return;

    const left = game.deadline - now;
    timerFill.style.width = `${Math.max(0, (left / game.timeLimitMs) * 100)}%`;
    if (left <= 0) {
      flash('Too slow.', false);
      return end('timeout');
    }

    if (game.ringEl) {
      game.ringEl.style.transform = `scale(${ringScale(now)})`;
      game.ringEl.classList.toggle('in-window', inWindow(now));
    }
    game.frame = requestAnimationFrame(loop);
  }

  function open(data) {
    const count = Math.min(5, Math.max(3, data.spots | 0));
    const offset = Math.random() * Math.PI * 2;
    const now = performance.now();

    game = {
      positions: Array.from({ length: count }, (_, i) => offset + (i * Math.PI * 2) / count),
      current: 0,
      misses: 0,
      maxMisses: data.maxMisses | 0,
      cycleMs: Math.max(300, data.cycleMs | 0),
      window: Number(data.window) || 0.25,
      timeLimitMs: data.timeLimitMs,
      deadline: now + data.timeLimitMs,
      cycleStart: now,
      inputKey: String(data.inputKey || 'E').toLowerCase(),
      cancelKey: String(data.cancelKey || 'Escape').toLowerCase(),
      soundVolume: Number(data.soundVolume) || 0,
      finished: false,
    };

    hintEl.innerHTML = '';
    const stabKey = document.createElement('kbd');
    stabKey.textContent = data.inputKey || 'E';
    const cancelKey = document.createElement('kbd');
    cancelKey.textContent = data.cancelKey || 'Escape';
    hintEl.append('Press ', stabKey, ' or click when the ring turns green · ', cancelKey, ' to cancel');

    feedbackEl.textContent = '';
    root.classList.remove('hidden');
    render();
    game.frame = requestAnimationFrame(loop);
  }

  function close() {
    if (game) {
      game.finished = true;
      cancelAnimationFrame(game.frame);
    }
    game = null;
    root.classList.add('hidden');
  }

  window.addEventListener('message', (event) => {
    const data = event.data || {};
    if (data.action === 'open') open(data);
    else if (data.action === 'close') close();
    else if (data.action === 'sound') Sound.play(data.sound, Number(data.volume));
  });

  window.addEventListener('keydown', (event) => {
    if (!game || game.finished || event.repeat) return;
    const key = event.key.toLowerCase();
    if (key === game.cancelKey) {
      event.preventDefault();
      end('cancelled');
    } else if (key === game.inputKey) {
      event.preventDefault();
      stab();
    }
  });
})();
