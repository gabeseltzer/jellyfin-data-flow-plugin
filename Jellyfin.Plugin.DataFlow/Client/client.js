/* Jellyfin Data Flow: live network throughput graph in the Playback Info overlay.
   Injected into jellyfin-web by the server plugin. ES2018, no dependencies.
   Everything is driven by the overlay's visibility: hidden => no timers, no fetches, no drawing. */
(function () {
  'use strict';
  if (window.__jellyfinDataFlow) { return; }
  var DF = window.__jellyfinDataFlow = { version: '0.1.0' };

  var STR = {
    title: 'Data Flow', clientDown: 'Client ↓', clientUp: 'Client ↑', serverUp: 'Server ↑',
    serverDown: 'Server ↓', average: 'Avg', required: 'Required', buffer: 'Buffer', ok: 'OK',
    marginal: 'marginal', starved: 'starved', buffered: 'buffered', paused: 'paused', none: '–',
    waiting: 'Waiting for data…', unavailable: 'Data Flow: server data not available',
    now: 'now', srcBrowser: 'measured in browser', mean5: '5 s mean', min: 'min', sec: 's'
  };

  var SERIES = [
    { key: 'clientDown', label: STR.clientDown, color: '#4fc3f7', width: 1.6, dash: [], def: true },
    { key: 'clientUp', label: STR.clientUp, color: '#ce93d8', width: 1, dash: [], def: false },
    { key: 'serverUp', label: STR.serverUp, color: '#ffb74d', width: 1.2, dash: [5, 3], def: true },
    { key: 'serverDown', label: STR.serverDown, color: '#80cbc4', width: 1, dash: [3, 3], def: true }
  ];
  var FILL = { ok: 'rgba(67,160,71,.35)', buffered: 'rgba(67,160,71,.25)', marginal: 'rgba(255,179,0,.35)', starved: 'rgba(229,57,53,.4)', paused: 'rgba(120,120,120,.2)', none: 'rgba(160,160,160,.22)' };
  var WINDOWS = [60, 120, 300];
  var MEAN_SECONDS = 5;
  var HEALTHY_BUFFER_S = 8; // below this much buffered playback while under the required rate is a real warning; browsers idle at 10-30 s

  var api = function () { return window.ApiClient; };
  var log = function (m, e) { try { console.warn('[DataFlow] ' + m, e || ''); } catch (x) { /* ignore */ } };
  function safe(fn) { return function () { try { return fn.apply(this, arguments); } catch (e) { log('error', e); } }; }
  function store(k, v) { try { if (v === undefined) { return localStorage.getItem('dataFlow.' + k); } localStorage.setItem('dataFlow.' + k, v); } catch (e) { return null; } }
  function fmtBps(b) {
    if (b === null || b === undefined || isNaN(b)) { return STR.none; }
    if (b < 1e3) { return Math.round(b) + ' bps'; }
    if (b < 1e6) { return (b < 1e5 ? (b / 1e3).toFixed(1) : Math.round(b / 1e3)) + ' kbps'; }
    if (b < 1e9) { return (b < 1e8 ? (b / 1e6).toFixed(1) : Math.round(b / 1e6)) + ' Mbps'; }
    return (b / 1e9).toFixed(2) + ' Gbps';
  }
  function fmtAxis(b) { return fmtBps(b).replace(/\.0+ /, ' '); }
  function fmtAgo(s) { var m = Math.floor(s / 60), r = s % 60; return '-' + m + ':' + (r < 10 ? '0' : '') + r; }

  /* ---------- config ---------- */
  var cfg = { pollIntervalMs: 2000, historySeconds: 300, marginPercent: 20, browserMeasurement: true, windows: WINDOWS };
  var cfgLoaded = false;
  function loadConfig() {
    if (cfgLoaded || !api()) { return Promise.resolve(); }
    return api().getJSON(api().getUrl('DataFlow/Config')).then(function (c) {
      if (c && c.pollIntervalMs) { cfg = c; if (!cfg.windows || !cfg.windows.length) { cfg.windows = WINDOWS; } }
      cfgLoaded = true;
    }).catch(function (e) { log('config', e); });
  }

  /* ---------- data model: aligned 1 s buckets, oldest first ---------- */
  var data = { t0: 0, interval: 1000, len: 0, clientDown: [], clientUp: [], serverUp: [], serverDown: [], browserDown: [], bufferAt: [], serverNow: 0, offset: 0, known: false, failures: 0 };
  function trimTo(n) {
    var extra = data.len - n;
    if (extra <= 0) { return; }
    ['clientDown', 'clientUp', 'serverUp', 'serverDown', 'browserDown', 'bufferAt'].forEach(function (k) { data[k].splice(0, extra); });
    data.t0 += extra * data.interval; data.len -= extra;
  }
  function cursor() { return data.len ? data.t0 + (data.len - 1) * data.interval : 0; }
  function merge(r) {
    var n = r.clientDown ? r.clientDown.length : 0;
    data.serverNow = r.now; data.offset = r.now - Date.now(); data.known = !!r.deviceKnown; data.interval = r.interval || 1000;
    if (!n) { return; }
    if (!data.len || r.t0 < data.t0) {
      data.t0 = r.t0; data.len = 0;
      data.clientDown = []; data.clientUp = []; data.serverUp = []; data.serverDown = []; data.browserDown = []; data.bufferAt = [];
    }
    var expected = data.t0 + data.len * data.interval;
    var skip = 0, buf = bufferAhead(); // buffer health is only observable now; remember it with the buckets that arrive now
    if (r.t0 < expected) { skip = Math.round((expected - r.t0) / data.interval); }
    else if (r.t0 > expected) {
      var gap = Math.round((r.t0 - expected) / data.interval);
      if (gap > cfg.historySeconds) { data.t0 = r.t0; data.len = 0; data.clientDown = []; data.clientUp = []; data.serverUp = []; data.serverDown = []; data.browserDown = []; data.bufferAt = []; gap = 0; }
      for (var g = 0; g < gap; g++) { data.clientDown.push(0); data.clientUp.push(0); data.serverUp.push(0); data.serverDown.push(0); data.browserDown.push(0); data.bufferAt.push(buf); data.len++; }
    }
    for (var i = skip; i < n; i++) {
      data.clientDown.push(r.clientDown[i]); data.clientUp.push(r.clientUp[i]); data.serverUp.push(r.serverUp[i]); data.serverDown.push(r.serverDown[i]);
      data.browserDown.push(browser.take(data.t0 + data.len * data.interval)); data.bufferAt.push(buf); data.len++;
    }
    trimTo(cfg.historySeconds);
  }
  function bps(bytes) { return bytes * 8000 / data.interval; }

  /* ---------- browser-side measurement (fallback when the server sees no media bytes) ---------- */
  var browser = {
    buckets: {}, total: 0, obs: null,
    start: function () {
      if (this.obs || !cfg.browserMeasurement || typeof PerformanceObserver === 'undefined') { return; }
      var self = this;
      try {
        this.obs = new PerformanceObserver(function (list) {
          list.getEntries().forEach(function (e) {
            if (!/\/(Videos|Audio)\/[^/]+\/(stream|hls1?\/|master\.m3u8|main\.m3u8|live\.m3u8)/i.test(e.name)) { return; }
            var bytes = e.transferSize || e.encodedBodySize || 0;
            if (!bytes) { return; }
            // key by the transfer's end time in server clock ms; merge() assigns it to the server bucket containing it
            var t = Math.round(performance.timeOrigin + e.responseEnd + data.offset);
            self.buckets[t] = (self.buckets[t] || 0) + bytes; self.total += bytes;
          });
        });
        this.obs.observe({ type: 'resource', buffered: true });
      } catch (e) { this.obs = null; }
    },
    stop: function () { if (this.obs) { try { this.obs.disconnect(); } catch (e) { /* ignore */ } this.obs = null; } this.buckets = {}; },
    take: function (t) { // sum browser buckets overlapping [t, t + interval); server buckets are not aligned to whole seconds
      var v = 0, cutoff = t - 30000, end = t + data.interval;
      for (var k in this.buckets) { var kt = +k; if (kt < cutoff) { delete this.buckets[k]; } else if (kt >= t && kt < end) { v += this.buckets[k]; delete this.buckets[k]; } }
      return v;
    }
  };

  /* ---------- session / required bitrate ---------- */
  var session = { requiredBps: null, method: null, itemId: null, mediaSourceId: null, lastFetch: 0, item: null, transcoding: false };
  function refreshSession() {
    var a = api();
    if (!a) { return Promise.resolve(); }
    return a.getSessions({ deviceId: a.deviceId() }).then(function (list) {
      var mine = null;
      (list || []).forEach(function (s) { if (s.DeviceId === a.deviceId() && (s.NowPlayingItem || !mine)) { mine = s; } });
      if (!mine || !mine.NowPlayingItem) { session.requiredBps = null; session.method = null; session.transcoding = false; return; }
      var ps = mine.PlayState || {};
      session.method = ps.PlayMethod || null;
      session.transcoding = !!(mine.TranscodingInfo && ps.PlayMethod === 'Transcode');
      if (mine.TranscodingInfo && mine.TranscodingInfo.Bitrate && ps.PlayMethod === 'Transcode') {
        session.requiredBps = mine.TranscodingInfo.Bitrate; return;
      }
      var itemId = mine.NowPlayingItem.Id, msId = ps.MediaSourceId || null;
      if (session.item && session.itemId === itemId) { session.requiredBps = requiredFromItem(session.item, msId, ps); return; }
      session.itemId = itemId; session.mediaSourceId = msId;
      var sources = mine.NowPlayingItem.MediaSources;
      var p = sources && sources.length ? Promise.resolve(mine.NowPlayingItem) : a.getItem(a.getCurrentUserId(), itemId);
      return p.then(function (item) { session.item = item; session.requiredBps = requiredFromItem(item, msId, ps); });
    }).catch(function (e) { log('session', e); });
  }
  function requiredFromItem(item, msId, ps) {
    var sources = (item && item.MediaSources) || [];
    var ms = null;
    sources.forEach(function (s) { if (!ms && (!msId || s.Id === msId)) { ms = s; } });
    if (!ms) { return null; }
    if (ms.Bitrate) { return ms.Bitrate; }
    var sum = 0, streams = ms.MediaStreams || [];
    streams.forEach(function (st) {
      if (st.Type === 'Video' && st.BitRate) { sum += st.BitRate; }
      if (st.Type === 'Audio' && st.BitRate && (ps.AudioStreamIndex === undefined || ps.AudioStreamIndex === null || ps.AudioStreamIndex === st.Index)) { sum += st.BitRate; }
    });
    return sum || null;
  }
  function videoEl() { return document.querySelector('video.htmlvideoplayer') || document.querySelector('.videoPlayerContainer video') || document.querySelector('audio.mediaPlayerAudio'); }
  function bufferAhead() {
    var v = videoEl();
    if (!v || !v.buffered) { return null; }
    try {
      var t = v.currentTime;
      for (var i = 0; i < v.buffered.length; i++) {
        if (v.buffered.start(i) <= t + 0.5 && v.buffered.end(i) >= t) { return Math.max(0, v.buffered.end(i) - t); }
      }
      return 0;
    } catch (e) { return null; }
  }
  function isPaused() { var v = videoEl(); return !!(v && v.paused); }

  /* ---------- derived values ---------- */
  function activeDownSeries() {
    var wnd = view.windowSeconds, start = Math.max(0, data.len - wnd), any = false;
    for (var i = start; i < data.len; i++) { if (data.clientDown[i]) { any = true; break; } }
    if (any || !cfg.browserMeasurement) { return { arr: data.clientDown, browser: false }; }
    for (var j = start; j < data.len; j++) { if (data.browserDown[j]) { return { arr: data.browserDown, browser: true }; } }
    return { arr: data.clientDown, browser: false };
  }
  function trailingMean(arr, endIdx, n) {
    var s = 0, c = 0;
    for (var i = Math.max(0, endIdx - n + 1); i <= endIdx; i++) { s += arr[i]; c++; }
    return c ? bps(s / c) : 0;
  }
  function statusFor(meanBps, buffer, paused) {
    if (paused) { return 'paused'; }
    var req = session.requiredBps;
    if (!req) { return 'none'; }
    if (meanBps >= req) { return 'ok'; }
    if (buffer !== null && buffer >= HEALTHY_BUFFER_S) { return 'buffered'; }
    if (meanBps >= req * (1 - cfg.marginPercent / 100)) { return 'marginal'; }
    return 'starved';
  }

  /* ---------- panel ---------- */
  var view = { root: null, panel: null, canvas: null, ctx: null, tip: null, legend: {}, badge: null, winBtn: null, src: null, note: null,
    windowSeconds: +store('window') || 120, visible: false, enabled: {}, rafPending: false, pollTimer: null, sessionTimer: null,
    hover: null, ro: null, classObs: null, pointerBound: false, cssLoaded: false, inflight: false };
  SERIES.forEach(function (s) { var v = store('series.' + s.key); view.enabled[s.key] = v === null ? s.def : v === '1'; });
  if (cfg.windows.indexOf(view.windowSeconds) < 0) { view.windowSeconds = 120; }

  function ensureCss() {
    if (view.cssLoaded || document.getElementById('dataFlowCss')) { view.cssLoaded = true; return; }
    var link = document.createElement('link'); link.id = 'dataFlowCss'; link.rel = 'stylesheet';
    var a = api(); link.href = a ? a.getUrl('DataFlow/client.css', { v: DF.version }) : '/DataFlow/client.css';
    document.head.appendChild(link); view.cssLoaded = true;
  }
  function el(tag, cls, html) { var e = document.createElement(tag); if (cls) { e.className = cls; } if (html !== undefined) { e.innerHTML = html; } return e; }

  function buildPanel() {
    var p = el('div', 'dataFlow');
    var header = el('div', 'playerStats-stat playerStats-stat-header dataFlow-header');
    header.appendChild(el('div', 'playerStats-stat-label', STR.title));
    var hv = el('div', 'playerStats-stat-value');
    view.winBtn = el('button', 'dataFlow-btn'); view.winBtn.type = 'button'; view.winBtn.title = 'Click to change the time window';
    view.badge = el('span', 'dataFlow-badge dataFlow-badge-none', STR.none);
    view.src = el('span', 'dataFlow-src');
    hv.appendChild(view.winBtn); hv.appendChild(view.badge); hv.appendChild(view.src);
    header.appendChild(hv); p.appendChild(header);

    var body = el('div', 'dataFlow-body');
    var graph = el('div', 'dataFlow-graph');
    view.canvas = document.createElement('canvas'); graph.appendChild(view.canvas);
    view.tip = el('div', 'dataFlow-tooltip'); view.tip.hidden = true; graph.appendChild(view.tip);
    body.appendChild(graph);

    var legend = el('div', 'dataFlow-legend');
    SERIES.forEach(function (s) {
      var b = el('button', 'dataFlow-legend-item' + (view.enabled[s.key] ? '' : ' off')); b.type = 'button'; b.dataset.series = s.key;
      b.title = (s.key === 'clientDown' ? STR.mean5 + '. ' : '') + 'Click to show or hide';
      b.innerHTML = '<i style="background:' + s.color + ';color:' + s.color + '"></i><span class="lbl">' + s.label + '</span><span class="val">' + STR.none + '</span>';
      b.addEventListener('click', safe(function () { view.enabled[s.key] = !view.enabled[s.key]; store('series.' + s.key, view.enabled[s.key] ? '1' : '0'); b.classList.toggle('off', !view.enabled[s.key]); requestRender(); }));
      legend.appendChild(b); view.legend[s.key] = b.querySelector('.val');
    });
    [['average', STR.average, '#9e9e9e'], ['required', STR.required, '#fff'], ['buffer', STR.buffer, null]].forEach(function (x) {
      var sp = el('span', 'dataFlow-legend-item');
      sp.innerHTML = (x[2] ? '<i style="background:' + x[2] + '"></i>' : '') + '<span class="lbl">' + x[1] + '</span><span class="val">' + STR.none + '</span>';
      legend.appendChild(sp); view.legend[x[0]] = sp.querySelector('.val');
    });
    body.appendChild(legend);
    view.note = el('div', 'dataFlow-note'); view.note.hidden = true; body.appendChild(view.note);
    p.appendChild(body);

    view.winBtn.addEventListener('click', safe(function () {
      var ws = cfg.windows, i = ws.indexOf(view.windowSeconds); view.windowSeconds = ws[(i + 1) % ws.length]; store('window', view.windowSeconds); updateWindowLabel(); requestRender();
    }));
    updateWindowLabel();
    view.ctx = view.canvas.getContext('2d');
    return p;
  }
  function updateWindowLabel() { var w = view.windowSeconds; view.winBtn.textContent = (w % 60 ? w + ' ' + STR.sec : (w / 60) + ' ' + STR.min); }

  function isTv() { return !!(view.root && view.root.classList.contains('playerStats-tv')); }

  function bindPointer() {
    if (view.pointerBound || isTv()) { return; }
    view.pointerBound = true;
    var g = view.canvas.parentNode;
    var move = safe(function (ev) { var r = g.getBoundingClientRect(); view.hover = { x: ev.clientX - r.left }; requestRender(); });
    var leave = safe(function () { view.hover = null; requestRender(); });
    g.addEventListener('pointermove', move); g.addEventListener('pointerdown', move); g.addEventListener('pointerleave', leave); g.addEventListener('pointercancel', leave);
  }

  /* ---------- attach / detach to the overlay ---------- */
  function attach(root) {
    if (view.root === root) { return; }
    detach();
    view.root = root;
    var content = root.querySelector('.playerStats-content');
    if (!content) { view.root = null; return; }
    ensureCss();
    view.panel = view.panel || buildPanel();
    content.appendChild(view.panel);
    view.classObs = new MutationObserver(safe(onVisibilityMaybeChanged));
    view.classObs.observe(root, { attributes: true, attributeFilter: ['class'] });
    if (window.ResizeObserver) { view.ro = new ResizeObserver(safe(function () { requestRender(); })); view.ro.observe(view.canvas.parentNode); }
    onVisibilityMaybeChanged();
  }
  function detach() {
    setVisible(false);
    if (view.classObs) { view.classObs.disconnect(); view.classObs = null; }
    if (view.ro) { view.ro.disconnect(); view.ro = null; }
    if (view.panel && view.panel.parentNode) { view.panel.parentNode.removeChild(view.panel); }
    view.root = null;
  }
  function onVisibilityMaybeChanged() {
    var vis = !!(view.root && !view.root.classList.contains('hide') && document.body.contains(view.root));
    setVisible(vis);
  }
  function setVisible(vis) {
    if (vis === view.visible) { return; }
    view.visible = vis;
    if (vis) {
      bindPointer(); browser.start();
      data.failures = 0;
      loadConfig().then(function () { if (!view.visible) { return; } refreshSession().then(requestRender); poll(); scheduleSession(); });
    } else {
      if (view.pollTimer) { clearTimeout(view.pollTimer); view.pollTimer = null; }
      if (view.sessionTimer) { clearTimeout(view.sessionTimer); view.sessionTimer = null; }
      browser.stop(); view.hover = null;
    }
  }

  /* ---------- polling ---------- */
  function poll() {
    if (!view.visible || view.inflight) { return; }
    var a = api();
    if (!a) { schedulePoll(); return; }
    view.inflight = true;
    var since = cursor();
    a.getJSON(a.getUrl('DataFlow/Samples', { deviceId: a.deviceId(), since: since })).then(function (r) {
      view.inflight = false; data.failures = 0; view.note.hidden = true;
      merge(r); requestRender(); schedulePoll();
    }).catch(function (e) {
      view.inflight = false; data.failures++;
      view.note.textContent = STR.unavailable; view.note.hidden = false;
      log('samples', e); requestRender(); schedulePoll();
    });
  }
  function schedulePoll() {
    if (!view.visible) { return; }
    var delay = Math.min(30000, cfg.pollIntervalMs * Math.pow(2, Math.min(4, data.failures)));
    view.pollTimer = setTimeout(safe(poll), delay);
  }
  function scheduleSession() {
    if (!view.visible) { return; }
    view.sessionTimer = setTimeout(safe(function () { refreshSession().then(function () { requestRender(); scheduleSession(); }); }), 10000);
  }

  /* ---------- rendering ---------- */
  function requestRender() {
    if (view.rafPending || !view.visible) { return; }
    view.rafPending = true;
    requestAnimationFrame(safe(function () { view.rafPending = false; render(); }));
  }
  function niceStep(maxV, target) {
    var raw = maxV / target, p = Math.pow(10, Math.floor(Math.log(raw) / Math.LN10)), m = raw / p;
    return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
  }
  function render() {
    var c = view.canvas, ctx = view.ctx, box = c.parentNode.getBoundingClientRect();
    var W = Math.max(50, Math.round(box.width)), H = Math.max(30, Math.round(box.height)), dpr = window.devicePixelRatio || 1;
    if (c.width !== Math.round(W * dpr) || c.height !== Math.round(H * dpr)) { c.width = Math.round(W * dpr); c.height = Math.round(H * dpr); }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, H);
    var fs = parseFloat(getComputedStyle(c.parentNode).fontSize) || 12;
    ctx.font = Math.round(fs * 0.8) + 'px sans-serif';
    var padR = fs * 0.4, padT = fs * 0.4, padB = fs * 1.1;

    var wnd = view.windowSeconds, len = data.len, start = Math.max(0, len - wnd);
    var down = activeDownSeries();
    var series = { clientDown: down.arr, clientUp: data.clientUp, serverUp: data.serverUp, serverDown: data.serverDown };
    var paused = isPaused(), buffer = bufferAhead();

    // y scale: a high percentile of the visible values so one burst (buffer fill at LAN speed) does not flatten the graph
    var vals = [];
    SERIES.forEach(function (s) { if (!view.enabled[s.key]) { return; } var arr = series[s.key]; for (var i = start; i < len; i++) { if (arr[i]) { vals.push(bps(arr[i])); } } });
    vals.sort(function (a, b) { return a - b; });
    var maxV = vals.length ? vals[Math.min(vals.length - 1, Math.floor(vals.length * 0.92))] * 1.25 : 0;
    if (session.requiredBps) { maxV = Math.max(maxV, session.requiredBps * 1.3); }
    if (maxV <= 0) { maxV = 1e6; }
    var step = niceStep(maxV, 2.5), top = Math.ceil(maxV / step) * step;
    var padL = fs * 0.6;
    for (var lv = step; lv <= top + 1e-9; lv += step) { padL = Math.max(padL, ctx.measureText(fmtAxis(lv)).width + fs * 0.6); }
    var gw = W - padL - padR, gh = H - padT - padB;
    var y = function (v) { return padT + gh - (Math.min(v, top) / top) * gh; };
    var x = function (i) { return padL + ((i - (len - wnd)) + 0.5) / wnd * gw; }, hs = gw / wnd / 2;

    // grid
    ctx.strokeStyle = 'rgba(255,255,255,.14)'; ctx.fillStyle = '#bbb'; ctx.lineWidth = 1; ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
    for (var gv = step; gv <= top + 1e-9; gv += step) {
      var gy = Math.round(y(gv)) + 0.5;
      ctx.beginPath(); ctx.moveTo(padL, gy); ctx.lineTo(W - padR, gy); ctx.stroke();
      ctx.fillText(fmtAxis(gv), padL - fs * 0.3, gy);
    }
    ctx.beginPath(); ctx.moveTo(padL, padT + gh + 0.5); ctx.lineTo(W - padR, padT + gh + 0.5); ctx.stroke();
    // time labels
    ctx.textAlign = 'center'; ctx.textBaseline = 'top';
    var tstep = wnd <= 60 ? 15 : wnd <= 120 ? 30 : 60;
    for (var ts = 0; ts <= wnd; ts += tstep) {
      var tx = padL + gw - (ts / wnd) * gw;
      ctx.fillText(ts ? fmtAgo(ts) : STR.now, Math.min(W - padR - fs, Math.max(padL + fs, tx)), padT + gh + fs * 0.15);
    }

    if (!len) {
      ctx.fillStyle = '#aaa'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.fillText(data.failures ? STR.unavailable : STR.waiting, padL + gw / 2, padT + gh / 2);
      updateLegend(null, null, null, buffer, paused, down.browser); return;
    }

    // colour-coded fill under the 5 s trailing mean of client download (HLS 1 s buckets are too spiky to read as an area)
    var statuses = [];
    if (view.enabled.clientDown) {
      for (var i = start; i < len; i++) {
        var m = trailingMean(down.arr, i, MEAN_SECONDS), st = statusFor(m, data.bufferAt[i] === undefined ? buffer : data.bufferAt[i], false);
        statuses[i] = st;
        var x0 = Math.max(padL, x(i) - hs), x1 = Math.min(padL + gw, x(i) + hs);
        var yv = y(m);
        ctx.fillStyle = FILL[st];
        ctx.fillRect(x0, yv, x1 - x0 + 0.5, padT + gh - yv);
      }
    }
    // series lines
    ctx.lineJoin = 'round'; ctx.lineCap = 'round';
    SERIES.forEach(function (s) {
      if (!view.enabled[s.key]) { return; }
      var arr = series[s.key];
      ctx.strokeStyle = s.color; ctx.lineWidth = s.width; ctx.setLineDash(s.dash);
      ctx.beginPath();
      for (var i = start; i < len; i++) { var px = x(i), py = y(bps(arr[i])); if (i === start) { ctx.moveTo(px, py); } else { ctx.lineTo(px, py); } }
      ctx.stroke();
    });
    ctx.setLineDash([]);
    // average and required
    var sum = 0; for (var k = start; k < len; k++) { sum += down.arr[k]; }
    var avg = len - start ? bps(sum / (len - start)) : 0;
    ctx.setLineDash([6, 4]); ctx.strokeStyle = '#9e9e9e'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(padL, y(avg)); ctx.lineTo(W - padR, y(avg)); ctx.stroke();
    if (session.requiredBps) {
      ctx.setLineDash([2, 3]); ctx.strokeStyle = '#fff';
      ctx.beginPath(); ctx.moveTo(padL, y(session.requiredBps)); ctx.lineTo(W - padR, y(session.requiredBps)); ctx.stroke();
    }
    ctx.setLineDash([]);

    // hover cursor + tooltip
    var lastIdx = len - 1, mean5 = trailingMean(down.arr, lastIdx, MEAN_SECONDS);
    var status = statusFor(mean5, buffer, paused);
    if (view.hover && view.hover.x >= padL && view.hover.x <= padL + gw) {
      var hi = Math.round((view.hover.x - padL) / gw * wnd - 0.5) + (len - wnd);
      hi = Math.max(start, Math.min(lastIdx, hi));
      var hx = x(hi);
      ctx.strokeStyle = 'rgba(255,255,255,.7)'; ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(Math.round(hx) + 0.5, padT); ctx.lineTo(Math.round(hx) + 0.5, padT + gh); ctx.stroke();
      showTip(hi, hx, W, series, down, statuses[hi] || statusFor(trailingMean(down.arr, hi, MEAN_SECONDS), data.bufferAt[hi] === undefined ? buffer : data.bufferAt[hi], false));
    } else { view.tip.hidden = true; }

    updateLegend(series, avg, status, buffer, paused, down.browser);
  }
  function showTip(i, hx, W, series, down, st) {
    var t = new Date(data.t0 + i * data.interval), tt = view.tip;
    var html = '<b>' + t.toLocaleTimeString() + '</b>';
    SERIES.forEach(function (s) { if (!view.enabled[s.key]) { return; } html += '<br><i style="background:' + s.color + '"></i>' + s.label + ' ' + fmtBps(bps(series[s.key][i])); });
    html += '<br>' + STR.mean5 + ' ' + fmtBps(trailingMean(down.arr, i, MEAN_SECONDS));
    if (data.bufferAt[i] !== undefined && data.bufferAt[i] !== null) { html += ' · ' + STR.buffer + ' ' + data.bufferAt[i].toFixed(1) + ' ' + STR.sec; }
    if (session.requiredBps) { html += ' · ' + STR.required + ' ' + fmtBps(session.requiredBps) + ' · <b>' + STR[st] + '</b>'; }
    tt.innerHTML = html; tt.hidden = false;
    var tw = tt.offsetWidth;
    tt.style.left = (hx + 10 + tw > W ? hx - 10 - tw : hx + 10) + 'px';
  }
  function updateLegend(series, avg, status, buffer, paused, fromBrowser) {
    var last = data.len - 1;
    // Client download shows the 5 s trailing mean (what the colour is judged on); HLS is too bursty for a 1 s value to be readable.
    SERIES.forEach(function (s) { view.legend[s.key].textContent = series && last >= 0 ? fmtBps(s.key === 'clientDown' ? trailingMean(series.clientDown, last, MEAN_SECONDS) : bps(series[s.key][last])) : STR.none; });
    view.legend.average.textContent = avg === null ? STR.none : fmtBps(avg);
    view.legend.required.textContent = session.requiredBps ? fmtBps(session.requiredBps) + (session.transcoding ? ' (transcode)' : '') : STR.none;
    view.legend.buffer.textContent = buffer === null ? STR.none : buffer.toFixed(1) + ' ' + STR.sec;
    var st = status || (paused ? 'paused' : 'none');
    view.badge.className = 'dataFlow-badge dataFlow-badge-' + st; view.badge.textContent = STR[st];
    view.src.textContent = fromBrowser ? STR.srcBrowser : '';
  }

  /* ---------- discovery ---------- */
  function scan() {
    var root = document.querySelector('.playerStats');
    if (root) { attach(root); } else if (view.root) { detach(); }
  }
  function start() {
    var bodyObs = new MutationObserver(safe(function (muts) {
      for (var i = 0; i < muts.length; i++) {
        var m = muts[i];
        for (var j = 0; j < m.addedNodes.length; j++) { var n = m.addedNodes[j]; if (n.nodeType === 1 && n.classList.contains('playerStats')) { attach(n); return; } }
        for (var k = 0; k < m.removedNodes.length; k++) { if (m.removedNodes[k] === view.root) { detach(); return; } }
      }
    }));
    bodyObs.observe(document.body, { childList: true });
    scan();
  }
  DF.debug = function () { return { data: data, session: session, view: view, cfg: cfg }; };
  if (document.body) { safe(start)(); } else { document.addEventListener('DOMContentLoaded', safe(start)); }
})();
