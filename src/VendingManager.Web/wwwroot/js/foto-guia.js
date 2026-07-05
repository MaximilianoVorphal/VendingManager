/**
 * foto-guia.js — Pan/zoom ES module for the Foto Guía panel.
 *
 * Exports flat module-level functions (no controller object pattern)
 * so bUnit can test the Blazor-to-JS dispatch via SetupModule + SetupVoid/Setup.
 *
 * Module-level state is intentional: only one FotoGuiaPanel exists per
 * page. bUnit 1.39.5 cannot mock IJSObjectReference return values from
 * SetupModule, so the controller-pattern from the design (#580) is replaced
 * with flat module-level exports (initPanZoom, zoomIn, zoomOut, reset,
 * label). If multi-panel support is needed, refactor to per-container
 * closure pattern.
 *
 * Usage:
 *   import { initPanZoom } from './js/foto-guia.js';
 *   const el = document.getElementById('...');
 *   initPanZoom(el);
 *   // then: zoomIn(), zoomOut(), reset(), label()
 */

/* ── Module-level state (single panel, so no instance management needed) ── */
let _el = null;
let _zoom = 1;
let _panX = 0;
let _panY = 0;
let _isDragging = false;
let _lastX = 0;
let _lastY = 0;

/* ── Public API ── */

/**
 * Initialize pan/zoom on the given element.
 * @param {HTMLElement} el - The .rec-guia-body element to attach events to.
 */
export function initPanZoom(el) {
  if (!el) return;
  _el = el;
  _zoom = 1;
  _panX = 0;
  _panY = 0;
  _isDragging = false;

  // Mouse wheel for zoom
  el.addEventListener('wheel', onWheel, { passive: false });

  // Mouse drag for pan
  el.addEventListener('mousedown', onPanStart);
  document.addEventListener('mousemove', onPanMove);
  document.addEventListener('mouseup', onPanEnd);

  // Touch support
  el.addEventListener('touchstart', onTouchStart, { passive: false });
  document.addEventListener('touchmove', onTouchMove, { passive: false });
  document.addEventListener('touchend', onTouchEnd);

  applyTransform();
}

export function zoomIn() {
  _zoom = Math.min(5, Math.round((_zoom * 1.25) * 100) / 100);
  applyTransform();
}

export function zoomOut() {
  _zoom = Math.max(1, Math.round((_zoom / 1.25) * 100) / 100);
  applyTransform();
}

export function reset() {
  _zoom = 1;
  _panX = 0;
  _panY = 0;
  applyTransform();
}

/**
 * Returns the current zoom level as a display string, e.g. "100%".
 * @returns {string}
 */
export function label() {
  return Math.round(_zoom * 100) + '%';
}

/* ── Internal helpers ── */

function applyTransform() {
  if (!_el) return;
  const img = _el.querySelector('img');
  if (!img) return;
  // Clamp pan to the image's actual overflow beyond the container so every
  // part stays reachable. The image is scaled from its center, so half of the
  // overflow spills to each side; that half is the max pan distance per axis.
  const maxPanX = Math.max(0, (img.offsetWidth * _zoom - _el.clientWidth) / 2);
  const maxPanY = Math.max(0, (img.offsetHeight * _zoom - _el.clientHeight) / 2);
  const clampedX = Math.max(-maxPanX, Math.min(maxPanX, _panX));
  const clampedY = Math.max(-maxPanY, Math.min(maxPanY, _panY));
  _panX = clampedX;
  _panY = clampedY;
  img.style.transform = `translate(${clampedX}px, ${clampedY}px) scale(${_zoom})`;
  img.style.transformOrigin = 'center center';
}

/* ── Mouse wheel zoom ── */

/**
 * @param {WheelEvent} e
 */
function onWheel(e) {
  e.preventDefault();
  const delta = e.deltaY > 0 ? -0.1 : 0.1;
  _zoom = Math.max(1, Math.min(5, Math.round((_zoom + delta) * 100) / 100));
  applyTransform();
}

/* ── Mouse drag pan ── */

/**
 * @param {MouseEvent} e
 */
function onPanStart(e) {
  if (e.button !== 0) return; // left button only
  _isDragging = true;
  _lastX = e.clientX;
  _lastY = e.clientY;
  if (_el) _el.style.cursor = 'grabbing';
}

/**
 * @param {MouseEvent} e
 */
function onPanMove(e) {
  if (!_isDragging) return;
  const dx = e.clientX - _lastX;
  const dy = e.clientY - _lastY;
  _lastX = e.clientX;
  _lastY = e.clientY;
  _panX += dx;
  _panY += dy;
  applyTransform();
}

function onPanEnd() {
  _isDragging = false;
  if (_el) _el.style.cursor = 'grab';
}

/* ── Touch support ── */

/**
 * @param {TouchEvent} e
 */
function onTouchStart(e) {
  if (e.touches.length !== 1) return;
  e.preventDefault();
  _isDragging = true;
  _lastX = e.touches[0].clientX;
  _lastY = e.touches[0].clientY;
}

/**
 * @param {TouchEvent} e
 */
function onTouchMove(e) {
  if (!_isDragging || e.touches.length !== 1) return;
  e.preventDefault();
  const dx = e.touches[0].clientX - _lastX;
  const dy = e.touches[0].clientY - _lastY;
  _lastX = e.touches[0].clientX;
  _lastY = e.touches[0].clientY;
  _panX += dx;
  _panY += dy;
  applyTransform();
}

function onTouchEnd() {
  _isDragging = false;
}
