// Client CPU cost of the panel: renderer main-thread task time over a fixed period with the
// Playback Info overlay open vs closed, measured through CDP Performance.getMetrics.
const { chromium } = require('playwright');
const BASE = process.env.JELLYFIN_URL || 'http://jellyfin:8096';
const SECS = +process.env.SECS || 30;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const browser = await chromium.launch({ channel: process.env.CHANNEL || 'chrome', args: ['--autoplay-policy=no-user-gesture-required'] });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();
  await page.goto(`${BASE}/web/index.html`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#txtManualName, .btnManualLogin', { timeout: 30000 });
  if (await page.$('.btnManualLogin')) { await page.click('.btnManualLogin'); }
  await page.fill('#txtManualName', 'admin'); await page.fill('#txtManualPassword', 'admin'); await page.click('button[type=submit]');
  await page.waitForFunction(() => window.ApiClient && window.ApiClient.getCurrentUserId(), null, { timeout: 30000 });
  const item = await page.evaluate(async (name) => { const a = window.ApiClient; const r = await a.getJSON(a.getUrl('Items', { IncludeItemTypes: 'Movie', Recursive: true, SearchTerm: name })); return { Id: r.Items[0].Id, serverId: a.serverId() }; }, process.env.ITEM || 'Long');
  await page.goto(`${BASE}/web/index.html#/details?id=${item.Id}&serverId=${item.serverId}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('.btnPlay:not(.hide)', { timeout: 30000 }); await sleep(500); await page.click('.btnPlay:not(.hide)');
  await page.waitForFunction(() => { const v = document.querySelector('video.htmlvideoplayer'); return v && v.currentTime > 1; }, null, { timeout: 60000 });

  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Performance.enable');
  const metric = async (name) => (await cdp.send('Performance.getMetrics')).metrics.find((m) => m.name === name).value;
  const window_ = async (label) => {
    const t0 = await metric('TaskDuration'), s0 = await metric('ScriptDuration'), l0 = await metric('LayoutDuration'), r0 = await metric('RecalcStyleDuration');
    await sleep(SECS * 1000);
    const dt = await metric('TaskDuration') - t0, ds = await metric('ScriptDuration') - s0, dl = await metric('LayoutDuration') - l0, dr = await metric('RecalcStyleDuration') - r0;
    console.log(`${label}: main-thread ${(dt / SECS * 100).toFixed(2)}% (script ${(ds / SECS * 100).toFixed(2)}%, layout ${(dl / SECS * 100).toFixed(2)}%, style ${(dr / SECS * 100).toFixed(2)}%) over ${SECS} s`);
    return dt / SECS * 100;
  };

  // baseline: playing, overlay closed (OSD hidden)
  await page.mouse.move(10, 790); await sleep(4000);
  const closed = await window_('overlay closed');
  // open Playback Info
  await page.mouse.move(600, 400); await sleep(200); await page.mouse.move(660, 420); await page.waitForSelector('.btnVideoOsdSettings', { state: 'visible', timeout: 15000 }); await page.click('.btnVideoOsdSettings');
  await page.waitForSelector('.actionSheet [data-id="stats"]', { timeout: 10000 }); await page.click('.actionSheet [data-id="stats"]');
  await page.waitForSelector('.playerStats .dataFlow', { timeout: 15000 }); await page.mouse.move(10, 790); await sleep(4000);
  const open = await window_('overlay open (Data Flow enabled)');
  // hide our panel to isolate jellyfin-web's own stats cost
  console.log('overlay state:', await page.evaluate(() => { const r = document.querySelector('.playerStats'); const d = window.__jellyfinDataFlow.debug(); return `playerStats=${!!r} hide=${r && r.classList.contains('hide')} panelAttached=${!!document.querySelector('.dataFlow')} visible=${d.view.visible} len=${d.data.len} failures=${d.data.failures} video=${!!document.querySelector('video.htmlvideoplayer')}`; }));
  await page.evaluate(() => { const v = window.__jellyfinDataFlow.debug().view; v.visible = false; clearTimeout(v.pollTimer); clearTimeout(v.sessionTimer); v.pollTimer = null; v.sessionTimer = null; if (v.panel) { v.panel.hidden = true; } });
  await sleep(2000);
  const openNoDf = await window_('overlay open (Data Flow suspended)');
  console.log(`Data Flow cost ≈ ${(open - openNoDf).toFixed(2)}% of one core (overlay open vs open-without-panel); overlay itself ≈ ${(openNoDf - closed).toFixed(2)}%`);
  await browser.close();
})().catch((e) => { console.error('FAILED', e); process.exit(1); });
