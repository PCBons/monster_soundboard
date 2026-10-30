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
const pads = new Map();      // id -> knopelement
const playing = new Set();   // { src, id }
let sounds = [];
let overlap = false;
let wakeLock = null;

try { overlap = localStorage.getItem('overlap') === '1'; } catch (e) { /* opslag niet beschikbaar */ }

function setStatus(text) { statusEl.textContent = text; }

function decode(arrayBuffer) {
  // Oudere Safari ondersteunt alleen de callback-vorm.
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

  // pointerdown gaat direct af bij aanraken (geen klikvertraging).
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
  void pad.offsetWidth; // voortgangsanimatie opnieuw starten
  pad.classList.add('playing');
}

function refreshPlayingState() {
  const active = new Set([...playing].map((p) => p.id));
  pads.forEach((pad, id) => { if (!active.has(id)) pad.classList.remove('playing'); });
  document.body.classList.toggle('speaking', active.size > 0);
  if (active.size === 0) setStatus('Klaar — tik op een geluid');
}

function stopAll() {
  playing.forEach((p) => { try { p.src.stop(); } catch (e) { /* al gestopt */ } });
  playing.clear();
  refreshPlayingState();
}

function play(id) {
  if (ctx.state !== 'running') ctx.resume();
  const buffer = buffers.get(id);
  if (!buffer) { setStatus('Dit geluid laadt nog…'); return; }
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
      console.error('Kon niet laden:', s.file, e);
      pads.get(s.id).classList.add('missing');
    }
    done++;
    wakeProgress.textContent = `${done} / ${sounds.length} geluiden geladen`;
  }));

  const missing = sounds.length - buffers.size;
  wakeProgress.textContent = missing ? `${missing} geluid(en) niet geladen` : 'alle geluiden klaar';
  setStatus(missing ? `${missing} geluid(en) niet geladen` : 'Klaar — tik op een geluid');
}

// ---- Scherm wakker houden: de tablet mag tijdens de act niet in slaap vallen ----
async function requestWakeLock() {
  if (!('wakeLock' in navigator)) return;
  try {
    wakeLock = await navigator.wakeLock.request('screen');
    wakeLock.addEventListener('release', () => { wakeLock = null; });
  } catch (e) { /* nu niet toegestaan */ }
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

// De eerste tik ontgrendelt geluid (verplicht op iOS/Android), houdt het scherm aan en gaat naar volledig scherm.
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

// ---- Bediening ----
function renderOverlap() {
  overlapBtn.setAttribute('aria-pressed', String(overlap));
  overlapBtn.textContent = 'Stapelen: ' + (overlap ? 'aan' : 'uit');
}
overlapBtn.addEventListener('click', () => {
  overlap = !overlap;
  try { localStorage.setItem('overlap', overlap ? '1' : '0'); } catch (e) { /* negeren */ }
  renderOverlap();
});
renderOverlap();

document.getElementById('stop').addEventListener('pointerdown', (e) => { e.preventDefault(); stopAll(); });
document.getElementById('fullscreen').addEventListener('click', toggleFullscreen);

// Sneltoetsen op een laptop: 1-0 en Q-P, spatie/Escape = stop.
document.addEventListener('keydown', (e) => {
  if (e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
  if (!wake.classList.contains('hidden')) return;
  if (e.key === ' ' || e.key === 'Escape') { e.preventDefault(); stopAll(); return; }
  const i = KEYS.indexOf(e.key.toLowerCase());
  if (i >= 0 && sounds[i]) play(sounds[i].id);
});

// Voorkom dat iOS de pagina laat scrollen / verend meebewegen bij vegen over het bord.
document.addEventListener('touchmove', (e) => e.preventDefault(), { passive: false });

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('sw.js').catch(() => {});
}

loadAll().catch((e) => {
  console.error(e);
  setStatus('Kon sounds.json niet laden');
  wakeProgress.textContent = 'kon geluiden niet laden — controleer de verbinding';
});
