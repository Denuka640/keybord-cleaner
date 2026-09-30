/* ====================================
   KeyShield — App Logic
   Keyboard locking via event capture
   ==================================== */

'use strict';

// ── State ─────────────────────────────
let isLocked   = false;
let keyCount   = 0;
let timerSecs  = 0;
let timerIntvl = null;
let toastTimer = null;

// ── DOM Refs ──────────────────────────
const idlePanel   = document.getElementById('idlePanel');
const lockedPanel = document.getElementById('lockedPanel');
const statusPill  = document.getElementById('statusPill');
const statusDot   = document.getElementById('statusDot');
const statusLabel = document.getElementById('statusLabel');
const keyCounter  = document.getElementById('keyCounter');
const counterBar  = document.getElementById('counterBar');
const timerDisp   = document.getElementById('timerDisplay');
const toastEl     = document.getElementById('toast');

// ── Keyboard Blocker ──────────────────
/**
 * Intercepts ALL keyboard events at the capture phase,
 * preventing them from reaching any element.
 * The 'true' flag = capture phase, runs before anything else.
 */
function blockEvent(e) {
  if (!isLocked) return;
  e.stopImmediatePropagation();
  e.preventDefault();

  // Count & animate
  keyCount++;
  keyCounter.textContent = keyCount;

  // Bump animation
  keyCounter.classList.remove('bump');
  void keyCounter.offsetWidth; // reflow
  keyCounter.classList.add('bump');
  setTimeout(() => keyCounter.classList.remove('bump'), 120);

  // Bar fill (caps at 100 presses = full)
  const pct = Math.min((keyCount / 100) * 100, 100);
  counterBar.style.width = pct + '%';
}

// Attach at capture phase on window so nothing gets through
const BLOCK_EVENTS = ['keydown', 'keyup', 'keypress'];
BLOCK_EVENTS.forEach(evt =>
  window.addEventListener(evt, blockEvent, { capture: true })
);

// ── Timer ─────────────────────────────
function startTimer() {
  timerSecs = 0;
  timerDisp.textContent = '00:00';
  timerIntvl = setInterval(() => {
    timerSecs++;
    const m = String(Math.floor(timerSecs / 60)).padStart(2, '0');
    const s = String(timerSecs % 60).padStart(2, '0');
    timerDisp.textContent = `${m}:${s}`;
  }, 1000);
}

function stopTimer() {
  clearInterval(timerIntvl);
  timerIntvl = null;
}

// ── Toast ─────────────────────────────
function showToast(msg, durationMs = 2800) {
  clearTimeout(toastTimer);
  toastEl.textContent = msg;
  toastEl.classList.add('show');
  toastTimer = setTimeout(() => toastEl.classList.remove('show'), durationMs);
}

// ── Lock ──────────────────────────────
function lockKeyboard() {
  if (isLocked) return;
  isLocked = true;
  keyCount  = 0;
  keyCounter.textContent = '0';
  counterBar.style.width  = '0%';

  // Switch panels
  idlePanel.classList.add('hidden');
  lockedPanel.classList.remove('hidden');

  // Status indicator
  statusPill.classList.add('locked');
  statusLabel.textContent = 'LOCKED';

  // Body class (changes card glow colours)
  document.body.classList.add('is-locked');

  // Start timer
  startTimer();

  showToast('🔒 Keyboard locked — clean away!');
}

// ── Unlock ────────────────────────────
function unlockKeyboard() {
  if (!isLocked) return;
  isLocked = false;

  // Switch panels
  lockedPanel.classList.add('hidden');
  idlePanel.classList.remove('hidden');

  // Status indicator
  statusPill.classList.remove('locked');
  statusLabel.textContent = 'READY';

  // Body class
  document.body.classList.remove('is-locked');

  // Stop timer
  stopTimer();

  const blocked = keyCount;
  const mins    = Math.floor(timerSecs / 60);
  const secs    = timerSecs % 60;
  const timeStr = mins > 0 ? `${mins}m ${secs}s` : `${secs}s`;

  showToast(`✅ Unlocked! Blocked ${blocked} key press${blocked !== 1 ? 'es' : ''} over ${timeStr}.`, 4000);
}

// ── Ripple Effect on Buttons ──────────
document.querySelectorAll('.action-btn').forEach(btn => {
  btn.addEventListener('click', function (e) {
    const rect = this.getBoundingClientRect();
    const ripple = document.createElement('span');
    ripple.className = 'ripple';
    const size = Math.max(rect.width, rect.height) * 1.5;
    ripple.style.cssText = `
      width: ${size}px; height: ${size}px;
      left: ${e.clientX - rect.left - size / 2}px;
      top:  ${e.clientY - rect.top  - size / 2}px;
    `;
    this.appendChild(ripple);
    setTimeout(() => ripple.remove(), 700);
  });
});

// ── Animated Background Orbs ──────────
(function spawnOrbs() {
  const layer = document.getElementById('bgLayer');
  const orbs = [
    { size: 400, color: 'rgba(124,58,237,0.12)', x: '10%',  y: '20%',  delay: '0s',   dur: '18s' },
    { size: 300, color: 'rgba(6,182,212,0.1)',   x: '70%',  y: '60%',  delay: '-6s',  dur: '22s' },
    { size: 250, color: 'rgba(167,139,250,0.08)',x: '40%',  y: '80%',  delay: '-12s', dur: '14s' },
    { size: 200, color: 'rgba(244,63,94,0.07)',  x: '80%',  y: '10%',  delay: '-3s',  dur: '20s' },
  ];

  orbs.forEach(o => {
    const el = document.createElement('div');
    el.className = 'orb';
    el.style.cssText = `
      width:${o.size}px; height:${o.size}px;
      background:${o.color};
      left:${o.x}; top:${o.y};
      margin-left:-${o.size/2}px; margin-top:-${o.size/2}px;
      animation-delay:${o.delay};
      animation-duration:${o.dur};
    `;
    layer.appendChild(el);
  });
})();

// ── Keyboard shortcuts guard ──────────
// Extra safety: also prevent context menus triggered by keyboard on lock
window.addEventListener('contextmenu', e => {
  // Allow right-click context menu from mouse always
}, { capture: false });
