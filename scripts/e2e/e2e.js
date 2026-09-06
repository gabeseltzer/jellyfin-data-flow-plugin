// End-to-end check of the Data Flow panel in a headless browser against the dev Jellyfin.
const { chromium } = require('playwright');
const fs = require('fs');

const BASE = process.env.JELLYFIN_URL || 'http://jellyfin:8096';
const OUT = process.env.OUT || __dirname + '/out';
const CHANNEL = process.env.CHANNEL || undefined; // e.g. 'chrome'
fs.mkdirSync(OUT, { recursive: true });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch({ channel: CHANNEL, args: ['--autoplay-policy=no-user-gesture-required', '--use-gl=swiftshader'] });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 2 });
  const page = await ctx.newPage();
  const logs = [];
  const dataFlowRequests = [];
  page.on('console', (m) => { const t = m.text(); if (/\[DataFlow\]|pageerror|Uncaught/.test(t)) logs.push(`[${m.type()}] ${t}`); });
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));
  page.on('request', (r) => { if (r.url().includes('/DataFlow/')) dataFlowRequests.push({ t: Date.now(), url: r.url().replace(BASE, '') }); });

  // 1. login
  await page.goto(`${BASE}/web/index.html`, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => window.__jellyfinDataFlow, null, { timeout: 20000 }).catch(() => {});
  const injected = await page.evaluate(() => !!window.__jellyfinDataFlow);
  console.log('script injected:', injected);
  await page.waitForSelector('#txtManualName, .btnManualLogin, .cardImageContainer', { timeout: 30000 });
  if (await page.$('.btnManualLogin')) { await page.click('.btnManualLogin'); }
  await page.waitForSelector('#txtManualName', { timeout: 15000 });
  await page.fill('#txtManualName', 'admin');
  await page.fill('#txtManualPassword', 'admin');
  await page.click('button[type=submit]');
  await page.waitForFunction(() => window.ApiClient && window.ApiClient.getCurrentUserId(), null, { timeout: 30000 });
  console.log('logged in as', await page.evaluate(() => window.ApiClient.getCurrentUserId()));

  // 2. find the item and play it
  const item = await page.evaluate(async (name) => {
    const a = window.ApiClient;
    const r = await a.getJSON(a.getUrl('Items', { IncludeItemTypes: 'Movie', Recursive: true, SearchTerm: name }));
    return { Id: r.Items[0].Id, Name: r.Items[0].Name, serverId: a.serverId() };
  }, process.env.ITEM || 'Mid');
  console.log('item:', item.Name, item.Id);
  await page.goto(`${BASE}/web/index.html#/details?id=${item.Id}&serverId=${item.serverId}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.btnPlay:not(.hide)', { timeout: 30000 });
  await sleep(500);
  await page.click('.btnPlay:not(.hide)');
  await page.waitForSelector('video.htmlvideoplayer', { timeout: 30000 });
  await page.waitForFunction(() => { const v = document.querySelector('video.htmlvideoplayer'); return v && v.currentTime > 1; }, null, { timeout: 60000 })
    .catch(async () => { console.log('video did not start; error=', await page.evaluate(() => { const v = document.querySelector('video.htmlvideoplayer'); return v && v.error && v.error.code; })); });
  console.log('video playing; currentTime', await page.evaluate(() => document.querySelector('video.htmlvideoplayer').currentTime));

  // 2b. optionally force a transcode by capping the quality (hls.js XHR requests honour CDP throttling; <video> loads do not)
  if (process.env.FORCE_TRANSCODE) {
    await page.mouse.move(640, 400); await sleep(300);
    await page.waitForSelector('.btnVideoOsdSettings', { timeout: 15000 });
    await page.click('.btnVideoOsdSettings');
    await page.waitForSelector('.actionSheet', { timeout: 10000 });
    await page.click('.actionSheet [data-id="quality"]');
    await page.waitForSelector('.actionSheet [data-id="quality"]', { state: 'detached', timeout: 10000 });
    await page.waitForSelector('.actionSheet .actionSheetMenuItem', { timeout: 10000 });
    await sleep(300);
    const opts = await page.$$eval('.actionSheet .actionSheetMenuItem', (els) => els.map((e) => ({ id: e.getAttribute('data-id'), text: e.textContent.trim() })));
    const pick = opts.filter((o) => /^\d+$/.test(o.id) && +o.id <= 3000000 && +o.id >= 2000000).sort((a, b) => b.id - a.id)[0] || opts.find((o) => /^\d+$/.test(o.id) && +o.id < 6000000);
    console.log('quality options:', opts.map((o) => o.id).join(','), '-> picking', pick && pick.id);
    await page.click(`.actionSheet [data-id="${pick.id}"]`);
    await page.waitForFunction(() => { const v = document.querySelector('video.htmlvideoplayer'); return v && /^blob:|m3u8/.test(v.currentSrc || v.src || '') && v.currentTime > 0.5; }, null, { timeout: 60000 })
      .catch(async () => console.log('did not switch to HLS; src =', await page.evaluate(() => { const v = document.querySelector('video.htmlvideoplayer'); return v && (v.currentSrc || v.src); })));
    console.log('now playing', await page.evaluate(() => { const v = document.querySelector('video.htmlvideoplayer'); return (v.currentSrc || v.src || '').slice(0, 80); }));
    await sleep(3000);
  }

  // 3. open Playback Info from the OSD settings menu
  await page.mouse.move(640, 400); await sleep(300);
  await page.waitForSelector('.btnVideoOsdSettings', { timeout: 15000 });
  await page.click('.btnVideoOsdSettings');
  await page.waitForSelector('.actionSheet', { timeout: 10000 });
  const statsBtn = await page.$('.actionSheet button[data-id="stats"], .actionSheet .actionSheetMenuItem:has-text("Playback Info"), .actionSheet .actionSheetMenuItem:has-text("Stats")');
  if (!statsBtn) { console.log('menu items:', await page.$$eval('.actionSheet .actionSheetMenuItem', (els) => els.map((e) => e.getAttribute('data-id') + ':' + e.textContent.trim()))); }
  await statsBtn.click();
  await page.waitForSelector('.playerStats:not(.hide)', { timeout: 15000 });
  const t0 = Date.now();
  await page.waitForSelector('.playerStats .dataFlow', { timeout: 10000 });
  await page.waitForFunction(() => { const d = window.__jellyfinDataFlow.debug(); return d.data.len > 0; }, null, { timeout: 15000 });
  console.log('panel + first data after ms:', Date.now() - t0);

  // let some data flow in
  await sleep(12000);
  let dbg = await page.evaluate(() => { const d = window.__jellyfinDataFlow.debug(); return { len: d.data.len, known: d.data.known, lastDown: d.data.clientDown.slice(-8), lastBrowser: d.data.browserDown.slice(-8), serverUp: d.data.serverUp.slice(-8), required: d.session.requiredBps, method: d.session.method, transcoding: d.session.transcoding, windowSeconds: d.view.windowSeconds }; });
  console.log('state:', JSON.stringify(dbg));
  const legend = await page.$$eval('.dataFlow-legend .dataFlow-legend-item', (els) => els.map((e) => e.textContent.trim().replace(/\s+/g, ' ')));
  console.log('legend:', legend.join(' | '));
  console.log('badge:', await page.$eval('.dataFlow-badge', (e) => e.textContent + ' ' + e.className));
  await page.screenshot({ path: `${OUT}/01-overlay.png` });
  const panel = await page.$('.playerStats');
  await panel.screenshot({ path: `${OUT}/02-panel.png` });

  // hover tooltip
  const g = await page.$('.dataFlow-graph');
  const gb = await g.boundingBox();
  await page.mouse.move(gb.x + gb.width * 0.8, gb.y + gb.height / 2);
  await sleep(300);
  console.log('tooltip:', await page.$eval('.dataFlow-tooltip', (e) => (e.hidden ? 'hidden' : e.innerText.replace(/\n/g, ' / '))));
  await panel.screenshot({ path: `${OUT}/03-tooltip.png` });
  await page.mouse.move(10, 10);

  // 4. throttle below the media bitrate (6 Mbps video): 1.5 Mbps
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Network.enable');
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 50, downloadThroughput: (+process.env.THROTTLE_BPS || 1_500_000) / 8, uploadThroughput: 1_000_000 / 8 });
  console.log('throttled to', (+process.env.THROTTLE_BPS || 1_500_000) / 1e6, 'Mbps; waiting...');
  const badges = [];
  const badge = async () => page.$eval('.dataFlow-badge', (e) => e.textContent).catch(() => 'GONE');
  for (let i = 0; i < Math.round((+process.env.THROTTLE_SECONDS || 60) / 2.5); i++) { await sleep(2500); badges.push(await badge()); }
  console.log('badge sequence under throttle:', badges.join(','));
  await panel.screenshot({ path: `${OUT}/04-throttled.png` });
  await cdp.send('Network.emulateNetworkConditions', { offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1 });
  const after = [];
  for (let i = 0; i < 12; i++) { await sleep(2500); after.push(await badge()); }
  console.log('badge sequence after unthrottle:', after.join(','));
  dbg = await page.evaluate(() => { const d = window.__jellyfinDataFlow.debug(); return { lastDown: d.data.clientDown.slice(-90), t: document.querySelector('video.htmlvideoplayer') && document.querySelector('video.htmlvideoplayer').currentTime }; });
  console.log('down buckets (last 90 s):', dbg.lastDown.join(','), '| currentTime', dbg.t);

  // 5. resize
  await page.setViewportSize({ width: 700, height: 800 }); await sleep(600);
  console.log('canvas after resize:', await page.$eval('.dataFlow-graph canvas', (c) => `${c.width}x${c.height} css ${c.clientWidth}x${c.clientHeight}`));
  await panel.screenshot({ path: `${OUT}/05-narrow.png` });

  // 6. close overlay: no more polling
  await page.click('.playerStats-closeButton');
  await sleep(500);
  const nBefore = dataFlowRequests.length;
  await sleep(7000);
  console.log('requests in 7 s after close:', dataFlowRequests.length - nBefore, '| timers:', await page.evaluate(() => { const v = window.__jellyfinDataFlow.debug().view; return `poll=${v.pollTimer} session=${v.sessionTimer} visible=${v.visible}`; }));

  // 7. admin configuration page
  await page.goto(`${BASE}/web/index.html#/configurationpage?name=Data%20Flow`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#DataFlowConfigPage #PollIntervalMs', { timeout: 30000 });
  await page.waitForFunction(() => document.querySelector('#DataFlowConfigPage #PollIntervalMs').value !== '', null, { timeout: 15000 });
  await sleep(1000);
  console.log('config page:', await page.evaluate(() => { const q = (s) => document.querySelector('#DataFlowConfigPage ' + s); return `inject=${q('#EnableInjection').checked} poll=${q('#PollIntervalMs').value} hist=${q('#HistorySeconds').value} margin=${q('#MarginPercent').value} nics=${Array.from(document.querySelectorAll('#NetworkInterfaces .nicCheckbox')).map((o) => o.value + (o.checked ? '*' : '')).join('/')}`; }));
  await page.screenshot({ path: `${OUT}/06-config.png` });

  console.log('DataFlow requests total:', dataFlowRequests.length, 'first few:', dataFlowRequests.slice(0, 4).map((r) => r.url).join(' ; '));
  console.log('console logs:', logs.length ? logs.join('\n') : '(none)');
  await browser.close();
})().catch((e) => { console.error('E2E FAILED', e); process.exit(1); });
