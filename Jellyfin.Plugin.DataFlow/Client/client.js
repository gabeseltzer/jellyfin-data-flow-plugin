/* Jellyfin Data Flow: injected client script. Phase 1 placeholder. */
(function () {
  'use strict';
  if (window.__jellyfinDataFlow) { return; }
  window.__jellyfinDataFlow = { version: '0.1.0' };
  try {
    console.info('[DataFlow] client script loaded');
  } catch (e) { /* never break playback */ }
})();
