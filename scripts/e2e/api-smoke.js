// Fast, browser-free checks of the plugin against a live Jellyfin 12.x server (the dev container by
// default). Each check asserts; the process exits non-zero if any fails.
//
// Jellyfin 12.0 turned off the legacy auth mechanisms (api_key, X-Emby-Token, X-Emby-Authorization)
// by default and dropped the /emby and /mediabrowser prefixes, so this confirms the plugin's
// endpoints work with the standard Authorization header and the new ApiKey query parameter alone.
const BASE = process.env.JELLYFIN_URL || 'http://jellyfin:8096';
const USER = process.env.JELLYFIN_USER || 'admin';
const PASS = process.env.JELLYFIN_PASS || 'admin';
const PLUGIN_ID = 'b7e3d5a2-4c1f-4e8a-9b6d-2f0a7c9e1d3b';
const CLIENT = 'MediaBrowser Client="DataFlow smoke", Device="node", DeviceId="dataflow-smoke", Version="1"';

let failures = 0;
const check = (name, ok, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ` (${detail})` : ''}`);
  if (!ok) failures++;
};
const status = async (path, headers = {}) => (await fetch(BASE + path, { headers })).status;

(async () => {
  const info = await (await fetch(`${BASE}/System/Info/Public`)).json();
  // 12.0 dropped the leading "10." from the version scheme.
  check('server is Jellyfin 12.x', /^12\./.test(info.Version), info.Version);

  const auth = await fetch(`${BASE}/Users/AuthenticateByName`, {
    method: 'POST',
    headers: { Authorization: CLIENT, 'Content-Type': 'application/json' },
    body: JSON.stringify({ Username: USER, Pw: PASS }),
  });
  check('login with Authorization header', auth.ok, `HTTP ${auth.status}`);
  if (!auth.ok) process.exit(1);
  const token = (await auth.json()).AccessToken;
  const hdr = { Authorization: `${CLIENT}, Token="${token}"` };

  const plugins = await (await fetch(`${BASE}/Plugins`, { headers: hdr })).json();
  const me = plugins.find((p) => p.Id.replace(/-/g, '') === PLUGIN_ID.replace(/-/g, ''));
  check('plugin loaded and Active', me && me.Status === 'Active', me ? `${me.Version} ${me.Status}` : 'not installed');

  const index = await (await fetch(`${BASE}/web/index.html`)).text();
  check('index.html carries the client script tag', /<script src="[^"]*\/DataFlow\/client\.js\?v=[^"]+" defer data-dataflow>/.test(index));
  check('client.js is served anonymously', (await status('/DataFlow/client.js')) === 200);
  check('client.css is served anonymously', (await status('/DataFlow/client.css')) === 200);

  const endpoints = ['/DataFlow/Config', '/DataFlow/Samples?deviceId=dataflow-smoke', '/DataFlow/Interfaces'];
  for (const ep of endpoints) {
    check(`${ep} with Authorization header`, (await status(ep, hdr)) === 200);
    const sep = ep.includes('?') ? '&' : '?';
    check(`${ep} with ApiKey query`, (await status(`${ep}${sep}ApiKey=${token}`)) === 200);
    check(`${ep} rejects anonymous`, (await status(ep)) === 401);
  }

  // Informational: whether this server still accepts legacy auth. The plugin must not depend on it.
  const legacy = await status('/DataFlow/Config', { 'X-Emby-Token': token });
  console.log(`info legacy X-Emby-Token -> HTTP ${legacy} (${legacy === 401 ? 'EnableLegacyAuthorization off, the 12.0 default' : 'legacy auth re-enabled on this server'})`);

  console.log(failures ? `\n${failures} check(s) failed` : '\nall checks passed');
  process.exit(failures ? 1 : 0);
})().catch((e) => { console.error('SMOKE FAILED', e); process.exit(1); });
