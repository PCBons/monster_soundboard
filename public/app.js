'use strict';

const KEYS = '1234567890qwertyuiop';
const board = document.getElementById('board');
const statusEl = document.getElementById('status');
const wake = document.getElementById('wake');
const wakeProgress = document.getElementById('wake-progress');
const overlapBtn = document.getElementById('overlap');

const AudioCtx = window.AudioContext || window.webkitAudioContext;
const ctx = new AudioCtx({ latencyHint: 'interactive' });
const master = ctx.createGain();
master.connect(ctx.destination);

const buffers = new Map();   // id -> AudioBuffer
const pads = new Map();      // id -> button element
const playing = new Set();   // { src, id }
let sounds = [];
let overlap = false;
let wakeLock = null;

try { overlap = localStorage.getItem('overlap') === '1'; } catch (e) { /* storage unavailable */ }

function setStatus(text) { statusEl.textContent = text; }

function decode(arrayBuffer) {
  // Older Safari only supports the callback form.
  return new Promise((resolve, reject) => {
    const p = ctx.decodeAudioData(arrayBuffer, resolve, reject);
    if (p && p.then) p.then(resolve, reject);
  });
}

function buildPad(sound, index) {
  const btn = document.createElement('button');
  btn.className = 'pad';
  btn.dataset.group = sound.group || 'growl';
  btn.innerHTML =
    '<span class="key"></span><span class="emoji"></span><span class="label"></span><span class="bar"></span>';
  btn.querySelector('.key').textContent = (KEYS[index] || '').toUpperCase();
  btn.querySelector('.emoji').textContent = sound.emoji || '👹';
  btn.querySelector('.label').textContent = sound.label;

  // pointerdown fires immediately on touch (no click delay).
  btn.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    btn.classList.add('down');
    play(sound.id);
  });
  const up = () => btn.classList.remove('down');
  btn.addEventListener('pointerup', up);
  btn.addEventListener('pointerleave', up);
  btn.addEventListener('pointercancel', up);
  btn.addEventListener('contextmenu', (e) => e.preventDefault());

  pads.set(sound.id, btn);
  board.appendChild(btn);
}

function markPlaying(id, duration) {
  const pad = pads.get(id);
  if (!pad) return;
  pad.style.setProperty('--dur', duration + 's');
  pad.classList.remove('playing');
  void pad.offsetWidth; // restart the progress animation
  pad.classList.add('playing');
}

function refreshPlayingState() {
  const active = new Set([...playing].map((p) => p.id));
  pads.forEach((pad, id) => { if (!active.has(id)) pad.classList.remove('playing'); });
  document.body.classList.toggle('speaking', active.size > 0);
  if (active.size === 0) setStatus('Ready — tap a sound');
}

function stopAll() {
  playing.forEach((p) => { try { p.src.stop(); } catch (e) { /* already stopped */ } });
  playing.clear();
  refreshPlayingState();
}

function play(id) {
  if (ctx.state !== 'running') ctx.resume();
  const buffer = buffers.get(id);
  if (!buffer) { setStatus('Still loading that one…'); return; }
  if (!overlap) stopAll();

  const src = ctx.createBufferSource();
  src.buffer = buffer;
  src.connect(master);
  const entry = { src, id };
  playing.add(entry);
  src.onended = () => { playing.delete(entry); refreshPlayingState(); };
  src.start(0);

  const sound = sounds.find((s) => s.id === id);
  setStatus('🔊 ' + sound.label);
  markPlaying(id, buffer.duration);
  document.body.classList.add('speaking');
}

async function loadAll() {
  const res = await fetch('sounds.json', { cache: 'no-cache' });
  sounds = await res.json();
  sounds.forEach(buildPad);

  let done = 0;
  await Promise.all(sounds.map(async (s) => {
    try {
      const r = await fetch(s.file);
      if (!r.ok) throw new Error(r.status);
      buffers.set(s.id, await decode(await r.arrayBuffer()));
    } catch (e) {
      console.error('Could not load', s.file, e);
      pads.get(s.id).classList.add('missing');
    }
    done++;
    wakeProgress.textContent = `loaded ${done} / ${sounds.length} sounds`;
  }));

  const missing = sounds.length - buffers.size;
  wakeProgress.textContent = missing ? `${missing} sound(s) failed to load` : 'all sounds ready';
  setStatus(missing ? `${missing} sound(s) failed to load` : 'Ready — tap a sound');
}

// ---- Screen wake lock: keep the tablet from sleeping during the act ----
async function requestWakeLock() {
  if (!('wakeLock' in navigator)) return;
  try {
    wakeLock = await navigator.wakeLock.request('screen');
    wakeLock.addEventListener('release', () => { wakeLock = null; });
  } catch (e) { /* not allowed right now */ }
}
document.addEventListener('visibilitychange', () => {
  if (document.visibilityState === 'visible') {
    if (!wakeLock) requestWakeLock();
    if (ctx.state !== 'running') ctx.resume();
  }
});

function toggleFullscreen() {
  const el = document.documentElement;
  const fsEl = document.fullscreenElement || document.webkitFullscreenElement;
  if (fsEl) (document.exitFullscreen || document.webkitExitFullscreen).call(document);
  else if (el.requestFullscreen) el.requestFullscreen().catch(() => {});
  else if (el.webkitRequestFullscreen) el.webkitRequestFullscreen();
}

// First tap unlocks audio (required by iOS/Android), keeps the screen on, goes fullscreen.
function unlock() {
  ctx.resume();
  const silent = ctx.createBufferSource();
  silent.buffer = ctx.createBuffer(1, 1, 22050);
  silent.connect(ctx.destination);
  silent.start(0);
  requestWakeLock();
  if (!(document.fullscreenElement || document.webkitFullscreenElement)) toggleFullscreen();
  wake.classList.add('hidden');
}
wake.addEventListener('pointerdown', (e) => { e.preventDefault(); unlock(); });
wake.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') unlock(); });

// ---- Controls ----
function renderOverlap() {
  overlapBtn.setAttribute('aria-pressed', String(overlap));
  overlapBtn.textContent = 'Layer: ' + (overlap ? 'on' : 'off');
}
overlapBtn.addEventListener('click', () => {
  overlap = !overlap;
  try { localStorage.setItem('overlap', overlap ? '1' : '0'); } catch (e) { /* ignore */ }
  renderOverlap();
});
renderOverlap();

document.getElementById('stop').addEventListener('pointerdown', (e) => { e.preventDefault(); stopAll(); });
document.getElementById('fullscreen').addEventListener('click', toggleFullscreen);

// Keyboard shortcuts for laptop use: 1-0 and Q-P, space/Escape = stop.
document.addEventListener('keydown', (e) => {
  if (e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
  if (!wake.classList.contains('hidden')) return;
  if (e.key === ' ' || e.key === 'Escape') { e.preventDefault(); stopAll(); return; }
  const i = KEYS.indexOf(e.key.toLowerCase());
  if (i >= 0 && sounds[i]) play(sounds[i].id);
});

// Stop iOS from scrolling / bouncing the page when swiping across the board.
document.addEventListener('touchmove', (e) => e.preventDefault(), { passive: false });

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('sw.js').catch(() => {});
}

loadAll().catch((e) => {
  console.error(e);
  setStatus('Could not load sounds.json');
  wakeProgress.textContent = 'could not load sounds — check the connection';
});
