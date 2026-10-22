// End-to-end check of the admin page's API against a running server:
//   ADMIN_PASSWORD=... node test-admin.js [http://127.0.0.1:8787]
// Makes a test player with a save, edits it through the admin API, restores a copy and deletes the player.
'use strict';
const base = process.argv[2] || 'http://127.0.0.1:8787';
const assert = require('assert');
const { pack, unpack } = require('./admin');

let cookie = '';
async function call(method, path, body, extra = {}) {
  const r = await fetch(base + path, {
    method,
    headers: { 'Content-Type': 'application/json', Cookie: cookie, 'X-Founders-Admin': '1', ...extra },
    body: body ? JSON.stringify(body) : undefined,
  });
  const set = r.headers.get('set-cookie');
  if (set) cookie = set.split(';')[0];
  const text = await r.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: r.status, json };
}

const state = {
  settlementId: 'abcd1234', settlementName: 'Test', mapName: 'm', epochId: 'epoch_first_communities', stageId: 'stage_camp',
  lastUpdateWallTime: 1, simTime: 100, resources: { food: 10, wood: 5, stone: 0, knowledge: 0 }, population: 4,
  growthProgress: 0, assignments: { counts: { gatherer: 2 } }, foodShortage: false,
  buildings: [{ id: 0, definitionId: 'campfire', x: 0, y: 0, status: 'active' }], nextBuildingId: 1,
  researched: [], exploredRegions: [], resolvedEvents: {}, modifiers: [], completedObjectives: [], reachedMilestones: [], legacy: [], nextLegacyId: 0,
};
const snapshot = {
  format: 1, uploadId: 'u1', deviceId: 'd', deviceName: 'iPhone test', savedAt: 1, founderName: 'Founder ABCD', settlementName: 'Test',
  stageId: 'stage_camp', epochId: 'epoch_first_communities', population: 4, buildings: 1, prosperity: 280, simTime: 100,
  save: JSON.stringify({ saveVersion: 1, contentVersion: 1, savedAtWallTime: 1, state }),
  profile: JSON.stringify({ name: 'Founder ABCD', avatar: 'face_m1', frame: 'wood', createdAt: 1, renames: 0, playSeconds: 0, sessions: 1 }),
};

(async () => {
  assert.equal((await call('GET', '/admin')).status, 200, 'page is on (is ADMIN_PASSWORD set?)');
  assert.equal((await call('GET', '/admin/api/players')).status, 401, 'needs sign-in');
  assert.equal((await call('POST', '/admin/api/login', { password: 'wrong' })).status, 401);
  assert.equal((await call('POST', '/admin/api/login', { password: process.env.ADMIN_PASSWORD })).status, 200);
  assert.ok(cookie.startsWith('founders_admin='));
  assert.equal((await call('GET', '/admin/api/players', null, { 'X-Founders-Admin': '' })).status, 401, 'needs the page header');

  // A player with a save, made the way the game makes one.
  const g = await (await fetch(base + '/v1/guest', { method: 'POST', body: '{}' })).json();
  const put = await fetch(base + '/v1/save/founders_snapshot', {
    method: 'PUT', headers: { Authorization: 'Bearer ' + g.token, 'Content-Type': 'application/json' }, body: JSON.stringify({ value: pack(snapshot) }),
  });
  assert.equal(put.status, 200);

  const list = (await call('GET', '/admin/api/players')).json.players;
  const row = list.find((p) => p.id === g.playerId);
  assert.equal(row.save.founderName, 'Founder ABCD');
  assert.equal(row.save.population, 4);

  const one = (await call('GET', '/admin/api/players/' + g.playerId)).json;
  assert.equal(one.snapshot.save.state.resources.food, 10);

  // Rejected: more workers than people, bad name.
  const bad = structuredClone(one.snapshot.save);
  bad.state.population = 1;
  let r = await call('PUT', `/admin/api/players/${g.playerId}/save`, { save: bad, profile: { ...one.snapshot.profile, name: 'X' } });
  assert.equal(r.status, 400);
  assert.equal(r.json.problems.length, 2);

  const good = structuredClone(one.snapshot.save);
  good.state.resources.food = 999;
  good.state.population = 10;
  r = await call('PUT', `/admin/api/players/${g.playerId}/save`, { save: good, profile: { ...one.snapshot.profile, name: 'Novi  Ime' } });
  assert.equal(r.status, 200, JSON.stringify(r.json));

  // The game reads the edited save: new upload id, summary and inner save updated.
  const got = await (await fetch(base + '/v1/save/founders_snapshot', { headers: { Authorization: 'Bearer ' + g.token } })).json();
  const snap = unpack(got.value);
  assert.notEqual(snap.uploadId, 'u1');
  assert.equal(snap.founderName, 'Novi Ime');
  assert.equal(snap.population, 10);
  assert.equal(JSON.parse(snap.save).state.resources.food, 999);
  assert.equal(snap.deviceName, 'iPhone test', 'other fields kept');

  const after = (await call('GET', '/admin/api/players/' + g.playerId)).json;
  assert.equal(after.backups.length, 1);
  r = await call('POST', `/admin/api/players/${g.playerId}/restore`, { backupId: after.backups[0].id });
  assert.equal(r.status, 200);
  assert.equal(JSON.parse(unpack((await call('GET', `/admin/api/players/${g.playerId}/raw`)).json).save).state.resources.food, 10);

  // Accounts.
  const user = 'adm_' + Date.now().toString(36);
  assert.equal((await call('POST', `/admin/api/players/${g.playerId}/account`, { action: 'password', username: user, password: 'weak' })).status, 400);
  assert.equal((await call('POST', `/admin/api/players/${g.playerId}/account`, { action: 'password', username: user, password: 'Strong#123' })).status, 200);
  const login = await fetch(base + '/v1/login', { method: 'POST', body: JSON.stringify({ username: user, password: 'Strong#123' }) });
  assert.equal(login.status, 200, 'player can sign in with the password set by admin');
  assert.equal((await call('POST', `/admin/api/players/${g.playerId}/account`, { action: 'rename', username: user + 'x' })).status, 200);
  assert.equal((await call('POST', `/admin/api/players/${g.playerId}/account`, { action: 'signout' })).status, 200);
  assert.equal((await fetch(base + '/v1/save/founders_snapshot', { headers: { Authorization: 'Bearer ' + g.token } })).status, 401);
  assert.equal((await call('POST', `/admin/api/players/${g.playerId}/account`, { action: 'unbind' })).status, 200);

  assert.equal((await call('DELETE', `/admin/api/players/${g.playerId}`)).status, 200);
  assert.equal((await call('GET', `/admin/api/players/${g.playerId}`)).status, 404);
  assert.equal((await call('POST', '/admin/api/logout')).status, 200);
  assert.equal((await call('GET', '/admin/api/players')).status, 401);
  console.log('admin: all checks passed');
})().catch((e) => { console.error(e); process.exit(1); });
