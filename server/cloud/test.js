// End-to-end check against a running server: node test.js [http://127.0.0.1:8787]
'use strict';
const base = process.argv[2] || 'http://127.0.0.1:8787';
const assert = require('assert');

async function call(method, path, body, token) {
  const r = await fetch(base + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  return { status: r.status, json: await r.json() };
}

(async () => {
  assert.equal((await call('GET', '/health')).status, 200);
  const a = (await call('POST', '/v1/guest', { deviceId: 'phone-a' })).json;
  assert.ok(a.token && a.playerId);
  assert.equal((await call('GET', '/v1/save/founders_snapshot', null, a.token)).status, 404);
  assert.equal((await call('PUT', '/v1/save/founders_snapshot', { value: 'gz:AAAA' }, a.token)).status, 200);
  assert.equal((await call('GET', '/v1/save/founders_snapshot', null, a.token)).json.value, 'gz:AAAA');
  assert.equal((await call('GET', '/v1/save/founders_snapshot', null, 'nope')).status, 401);
  assert.equal((await call('PUT', '/v1/save/bad key!', { value: 'x' }, a.token)).status, 400);

  const user = 'test_' + Date.now().toString(36);
  assert.equal((await call('POST', '/v1/link', { username: user, password: 'weak' }, a.token)).status, 400);
  assert.equal((await call('POST', '/v1/link', { username: user, password: 'Strong#123' }, a.token)).status, 200);
  assert.equal((await call('POST', '/v1/link', { username: user + 'x', password: 'Strong#123' }, a.token)).status, 409);

  const b = (await call('POST', '/v1/guest', {})).json;
  assert.equal((await call('POST', '/v1/link', { username: user, password: 'Strong#123' }, b.token)).status, 409, 'username taken');
  assert.equal((await call('POST', '/v1/login', { username: user, password: 'Wrong#123' })).status, 401);
  const login = (await call('POST', '/v1/login', { username: user, password: 'Strong#123' })).json;
  assert.equal(login.playerId, a.playerId);
  assert.equal((await call('GET', '/v1/save/founders_snapshot', null, login.token)).json.value, 'gz:AAAA', 'new phone sees the save');
  assert.equal((await call('POST', '/v1/logout', {}, login.token)).status, 200);
  assert.equal((await call('GET', '/v1/save/founders_snapshot', null, login.token)).status, 401);
  console.log('all checks passed');
})().catch((e) => { console.error(e); process.exit(1); });
