// FOUNDERS cloud save API: a small HTTP service in front of MariaDB/MySQL. The game never talks to the database;
// it talks to this service (behind HTTPS, see README.md), which signs players in and keeps their saves.
//
//   POST   /v1/guest            {deviceId}            -> {playerId, token}   new guest player
//   POST   /v1/login            {username, password}  -> {playerId, token}   sign in to a bound account
//   POST   /v1/link             {username, password}  (token)               bind credentials to this player
//   POST   /v1/logout                                 (token)               forget this token
//   GET    /v1/save/:key                              (token) -> {value}    404 when nothing is saved
//   PUT    /v1/save/:key        {value}               (token)
//   GET    /health                                    -> {ok: true}
//   /admin                                                 password-protected admin page (admin.js)
//
// Configuration (environment, e.g. /etc/founders-cloud.env): DB_HOST, DB_PORT, DB_USER, DB_PASSWORD, DB_NAME,
// PORT (default 8787), HOST (default 127.0.0.1: only the HTTPS proxy on the same server reaches it),
// ADMIN_PASSWORD (turns on /admin; at least 12 characters).
'use strict';

const http = require('http');
const crypto = require('crypto');
const mysql = require('mysql2/promise');

const MAX_BODY = 8 * 1024 * 1024; // a settlement is a few hundred KB compressed; plenty of room
const MAX_VALUE = 6 * 1024 * 1024;

const pool = mysql.createPool({
  host: process.env.DB_HOST || '127.0.0.1',
  port: Number(process.env.DB_PORT || 3306),
  user: process.env.DB_USER || 'founders',
  password: process.env.DB_PASSWORD || '',
  database: process.env.DB_NAME || 'founders',
  connectionLimit: 10,
  charset: 'utf8mb4',
});

// MARK: - Helpers

class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

const sha256 = (s) => crypto.createHash('sha256').update(s).digest('hex');
const newId = () => crypto.randomBytes(16).toString('hex');

function hashPassword(password) {
  const salt = crypto.randomBytes(16);
  const key = crypto.scryptSync(password, salt, 64, { N: 16384, r: 8, p: 1 });
  return `scrypt$${salt.toString('hex')}$${key.toString('hex')}`;
}

function checkPassword(password, stored) {
  const [kind, saltHex, keyHex] = String(stored || '').split('$');
  if (kind !== 'scrypt' || !saltHex || !keyHex) return false;
  const key = crypto.scryptSync(password, Buffer.from(saltHex, 'hex'), 64, { N: 16384, r: 8, p: 1 });
  const want = Buffer.from(keyHex, 'hex');
  return want.length === key.length && crypto.timingSafeEqual(want, key);
}

// The game's rules (same as in Settings → Account).
function usernameProblem(u) {
  if (typeof u !== 'string' || u.length < 3 || u.length > 20) return 'Username: 3 to 20 characters.';
  if (!/^[A-Za-z0-9.\-@_]+$/.test(u)) return 'Username: letters, numbers and . - @ _ only (no spaces).';
  return null;
}

function passwordProblem(p) {
  if (typeof p !== 'string' || p.length < 8 || p.length > 30) return 'Password: 8 to 30 characters.';
  if (!(/[A-Z]/.test(p) && /[a-z]/.test(p) && /[0-9]/.test(p) && /[^A-Za-z0-9]/.test(p)))
    return 'Password needs a capital letter, a small letter, a number and a symbol.';
  return null;
}

// Slows down password guessing: at most 10 failed logins per address and per username in 15 minutes.
const failures = new Map();
function tooManyFailures(key) {
  const now = Date.now();
  const list = (failures.get(key) || []).filter((t) => now - t < 15 * 60 * 1000);
  failures.set(key, list);
  return list.length >= 10;
}
function noteFailure(key) { (failures.get(key) || failures.set(key, []).get(key)).push(Date.now()); }
setInterval(() => { const now = Date.now(); for (const [k, v] of failures) if (!v.some((t) => now - t < 15 * 60 * 1000)) failures.delete(k); }, 60 * 1000).unref();

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', (c) => {
      size += c.length;
      if (size > MAX_BODY) { reject(new HttpError(413, 'Too large.')); req.destroy(); return; }
      chunks.push(c);
    });
    req.on('end', () => {
      if (size === 0) return resolve({});
      try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
      catch { reject(new HttpError(400, 'Body is not JSON.')); }
    });
    req.on('error', reject);
  });
}

async function issueToken(playerId) {
  const token = crypto.randomBytes(32).toString('base64url');
  await pool.execute('INSERT INTO sessions (token_hash, player_id) VALUES (?, ?)', [sha256(token), playerId]);
  return token;
}

async function authenticate(req) {
  const m = /^Bearer (.+)$/.exec(req.headers.authorization || '');
  if (!m) throw new HttpError(401, 'Not signed in.');
  const hash = sha256(m[1]);
  const [rows] = await pool.execute('SELECT player_id FROM sessions WHERE token_hash = ?', [hash]);
  if (rows.length === 0) throw new HttpError(401, 'Not signed in.');
  pool.execute('UPDATE sessions SET last_used = NOW() WHERE token_hash = ?', [hash]).catch(() => {});
  pool.execute('UPDATE players SET last_seen = NOW() WHERE id = ?', [rows[0].player_id]).catch(() => {});
  return { playerId: rows[0].player_id, tokenHash: hash };
}

function validKey(key) {
  if (!/^[A-Za-z0-9_.-]{1,64}$/.test(key)) throw new HttpError(400, 'Bad key.');
  return key;
}

// MARK: - Routes

async function route(req, url) {
  const p = url.pathname;
  if (req.method === 'GET' && p === '/health') {
    await pool.query('SELECT 1');
    return { ok: true };
  }

  if (req.method === 'POST' && p === '/v1/guest') {
    const id = newId();
    await pool.execute('INSERT INTO players (id) VALUES (?)', [id]);
    return { playerId: id, token: await issueToken(id) };
  }

  if (req.method === 'POST' && p === '/v1/login') {
    const { username, password } = await readBody(req);
    const ip = req.headers['x-forwarded-for']?.split(',')[0].trim() || req.socket.remoteAddress;
    const keys = ['ip:' + ip, 'user:' + String(username).toLowerCase()];
    if (keys.some(tooManyFailures)) throw new HttpError(429, 'Too many attempts. Try again in a few minutes.');
    const [rows] = await pool.execute('SELECT id, pass_hash FROM players WHERE username = ?', [String(username || '')]);
    if (rows.length === 0 || !checkPassword(String(password || ''), rows[0].pass_hash)) {
      keys.forEach(noteFailure);
      throw new HttpError(401, 'Invalid username or password.');
    }
    return { playerId: rows[0].id, token: await issueToken(rows[0].id) };
  }

  if (req.method === 'POST' && p === '/v1/link') {
    const me = await authenticate(req);
    const { username, password } = await readBody(req);
    const problem = usernameProblem(username) || passwordProblem(password);
    if (problem) throw new HttpError(400, problem);
    const [mine] = await pool.execute('SELECT username FROM players WHERE id = ?', [me.playerId]);
    if (mine[0]?.username) throw new HttpError(409, 'This player is already bound to an account.');
    try {
      await pool.execute('UPDATE players SET username = ?, pass_hash = ? WHERE id = ?', [username, hashPassword(password), me.playerId]);
    } catch (e) {
      if (e.code === 'ER_DUP_ENTRY') throw new HttpError(409, 'Username already exists.');
      throw e;
    }
    return { ok: true };
  }

  if (req.method === 'POST' && p === '/v1/logout') {
    const me = await authenticate(req);
    await pool.execute('DELETE FROM sessions WHERE token_hash = ?', [me.tokenHash]);
    return { ok: true };
  }

  const save = /^\/v1\/save\/([^/]+)$/.exec(p);
  if (save && req.method === 'GET') {
    const me = await authenticate(req);
    const [rows] = await pool.execute('SELECT value, updated_at FROM saves WHERE player_id = ? AND save_key = ?', [me.playerId, validKey(save[1])]);
    if (rows.length === 0) throw new HttpError(404, 'Nothing saved.');
    return { value: rows[0].value, updatedAt: rows[0].updated_at };
  }
  if (save && req.method === 'PUT') {
    const me = await authenticate(req);
    const { value } = await readBody(req);
    if (typeof value !== 'string' || value.length > MAX_VALUE) throw new HttpError(400, 'Value must be a string up to 6 MB.');
    await pool.execute(
      'INSERT INTO saves (player_id, save_key, value) VALUES (?, ?, ?) ON DUPLICATE KEY UPDATE value = VALUES(value)',
      [me.playerId, validKey(save[1]), value]);
    return { ok: true };
  }

  throw new HttpError(404, 'Not found.');
}

const admin = require('./admin')({ pool, HttpError, readBody, hashPassword, usernameProblem, passwordProblem, tooManyFailures, noteFailure });

const server = http.createServer(async (req, res) => {
  let status = 200, body;
  try {
    const url = new URL(req.url, 'http://localhost');
    if (await admin(req, res, url)) return;
    body = await route(req, url);
  } catch (e) {
    status = e instanceof HttpError ? e.status : 500;
    body = { error: e instanceof HttpError ? e.message : 'Server error.' };
    if (!(e instanceof HttpError)) console.error(new Date().toISOString(), req.method, req.url, e);
  }
  const text = JSON.stringify(body);
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Content-Length': Buffer.byteLength(text), 'Cache-Control': 'no-store' });
  res.end(text);
});

const port = Number(process.env.PORT || 8787);
const host = process.env.HOST || '127.0.0.1';
server.listen(port, host, () => console.log(`FOUNDERS cloud API on http://${host}:${port}`));

module.exports = { server, pool };
