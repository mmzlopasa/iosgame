// FOUNDERS admin dashboard: a password-protected web page (https://<your domain>/admin) for looking after players.
// Lists every player, opens a player's save and profile in a readable form, edits them (with checks so the game
// accepts the result), keeps a copy of every save before it is changed, and manages accounts.
//
// Turned on by ADMIN_PASSWORD in /etc/founders-cloud.env (at least 12 characters). Without it /admin is off.
//
//   GET    /admin                                  the page
//   POST   /admin/api/login        {password}      sets a sign-in cookie (12 hours)
//   POST   /admin/api/logout
//   GET    /admin/api/players?q=                   players with a short summary of their save
//   GET    /admin/api/players/:id                  player, readable save and profile, copies
//   PUT    /admin/api/players/:id/save   {save, profile}
//   POST   /admin/api/players/:id/restore {backupId}
//   POST   /admin/api/players/:id/account {action: rename|password|unbind|signout, username?, password?}
//   DELETE /admin/api/players/:id
//   GET    /admin/api/players/:id/raw              the stored value as a file (manual backup)
'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const zlib = require('zlib');

const KEY = 'founders_snapshot';
const SESSION_MS = 12 * 60 * 60 * 1000;
const KEEP_BACKUPS = 30;
const PAGE = fs.readFileSync(path.join(__dirname, 'admin.html'));

const sha256 = (s) => crypto.createHash('sha256').update(s).digest();

// MARK: - Save format (same as the game's CloudSave: "gz:" + base64(gzip(snapshot JSON)))

function unpack(value) {
  if (value.startsWith('gz:')) value = zlib.gunzipSync(Buffer.from(value.slice(3), 'base64')).toString('utf8');
  return JSON.parse(value);
}

function pack(snapshot) {
  return 'gz:' + zlib.gzipSync(Buffer.from(JSON.stringify(snapshot), 'utf8')).toString('base64');
}

function summary(value) {
  try {
    const s = unpack(value);
    return {
      founderName: s.founderName ?? null, settlementName: s.settlementName ?? null, epochId: s.epochId ?? null,
      population: s.population ?? null, buildings: s.buildings ?? null, prosperity: s.prosperity ?? null,
      deviceName: s.deviceName ?? null, savedAt: s.savedAt ?? null,
    };
  } catch {
    return { broken: true };
  }
}

const isNum = (v) => typeof v === 'number' && Number.isFinite(v);
const isCount = (v) => Number.isInteger(v) && v >= 0;

// The checks the game makes when it loads a save (SaveCodec.Validate) that can be made without the game's content,
// plus the profile name rules (PlayerProfile.NameProblem).
function problems(save, profile) {
  const p = [];
  if (!save || typeof save !== 'object' || !save.state || typeof save.state !== 'object') return ['Save nema dio "state".'];
  const s = save.state;
  for (const [k, v] of Object.entries(s.resources || {}))
    if (!isNum(v) || v < 0) p.push(`Resurs "${k}" mora biti broj 0 ili veći.`);
  if (!isCount(s.population)) p.push('Broj ljudi mora biti cijeli broj 0 ili veći.');
  const counts = Object.values(s.assignments?.counts || {});
  if (counts.some((v) => !isCount(v))) p.push('Broj zaposlenih na poslu mora biti cijeli broj 0 ili veći.');
  const workers = counts.reduce((a, v) => a + (isCount(v) ? v : 0), 0);
  if (isCount(s.population) && workers > s.population)
    p.push(`Zaposlenih (${workers}) je više nego ljudi (${s.population}). Smanji zaposlene ili povećaj broj ljudi.`);
  if (!isNum(s.simTime) || s.simTime < 0) p.push('simTime mora biti broj 0 ili veći.');
  if (typeof s.epochId !== 'string' || !s.epochId) p.push('Fali epoha (epochId).');
  const ids = (s.buildings || []).map((b) => b?.id);
  if (ids.some((id) => !Number.isInteger(id))) p.push('Svaka zgrada mora imati cijeli broj kao id.');
  if (new Set(ids).size !== ids.length) p.push('Dvije zgrade imaju isti id.');
  if (ids.length && Math.max(...ids) >= s.nextBuildingId) p.push('nextBuildingId mora biti veći od najvećeg id-a zgrade.');
  if (profile != null) {
    const name = String(profile.name ?? '').trim().replace(/\s+/g, ' ');
    if (name.length < 3 || name.length > 16) p.push('Ime osnivača: 3 do 16 znakova.');
    else if (!/^[\p{L}\p{N} _.'-]+$/u.test(name) || !/\p{L}/u.test(name)) p.push("Ime osnivača: slova, brojevi, razmak i _ - . ' (barem jedno slovo).");
  }
  return p;
}

module.exports = function createAdmin({ pool, HttpError, readBody, hashPassword, usernameProblem, passwordProblem, tooManyFailures, noteFailure }) {
  const password = process.env.ADMIN_PASSWORD || '';
  const enabled = password.length >= 12;
  if (password && !enabled) console.error('ADMIN_PASSWORD is shorter than 12 characters: /admin stays off.');
  const sessions = new Map(); // sha256(token) hex -> expiry

  function signedIn(req) {
    const m = /(?:^|;\s*)founders_admin=([A-Za-z0-9_-]+)/.exec(req.headers.cookie || '');
    if (!m) return false;
    const key = sha256(m[1]).toString('hex');
    const until = sessions.get(key);
    if (!until || until < Date.now()) { sessions.delete(key); return false; }
    return true;
  }

  function send(res, status, body, headers = {}) {
    const text = JSON.stringify(body);
    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Content-Length': Buffer.byteLength(text), 'Cache-Control': 'no-store', ...headers });
    res.end(text);
  }

  async function player(id) {
    const [rows] = await pool.execute('SELECT id, username, created_at, last_seen FROM players WHERE id = ?', [id]);
    if (rows.length === 0) throw new HttpError(404, 'Nema tog igrača.');
    return rows[0];
  }

  async function backup(playerId, note) {
    const [rows] = await pool.execute('SELECT value FROM saves WHERE player_id = ? AND save_key = ?', [playerId, KEY]);
    if (rows.length === 0) return;
    await pool.execute('INSERT INTO save_backups (player_id, save_key, value, note) VALUES (?, ?, ?, ?)', [playerId, KEY, rows[0].value, note]);
    await pool.execute(
      `DELETE FROM save_backups WHERE player_id = ? AND id NOT IN
         (SELECT id FROM (SELECT id FROM save_backups WHERE player_id = ? ORDER BY id DESC LIMIT ${KEEP_BACKUPS}) keep)`,
      [playerId, playerId]);
  }

  async function api(req, res, p, url) {
    if (req.method === 'POST' && p === '/login') {
      const ip = req.headers['x-forwarded-for']?.split(',')[0].trim() || req.socket.remoteAddress;
      if (tooManyFailures('admin:' + ip)) throw new HttpError(429, 'Previše pokušaja. Pokušaj za 15 minuta.');
      const { password: given } = await readBody(req);
      if (!crypto.timingSafeEqual(sha256(String(given || '')), sha256(password))) {
        noteFailure('admin:' + ip);
        throw new HttpError(401, 'Kriva lozinka.');
      }
      const token = crypto.randomBytes(32).toString('base64url');
      sessions.set(sha256(token).toString('hex'), Date.now() + SESSION_MS);
      return send(res, 200, { ok: true }, {
        'Set-Cookie': `founders_admin=${token}; Path=/admin; HttpOnly; Secure; SameSite=Strict; Max-Age=${SESSION_MS / 1000}`,
      });
    }

    // Everything else needs the cookie, and the page's own header (a form on another site cannot send it).
    if (!signedIn(req) || req.headers['x-founders-admin'] !== '1') throw new HttpError(401, 'Nisi prijavljen.');

    if (req.method === 'POST' && p === '/logout') {
      const m = /(?:^|;\s*)founders_admin=([A-Za-z0-9_-]+)/.exec(req.headers.cookie || '');
      if (m) sessions.delete(sha256(m[1]).toString('hex'));
      return send(res, 200, { ok: true }, { 'Set-Cookie': 'founders_admin=; Path=/admin; HttpOnly; Secure; SameSite=Strict; Max-Age=0' });
    }

    if (req.method === 'GET' && p === '/players') {
      const q = (url.searchParams.get('q') || '').trim();
      const [rows] = await pool.execute(
        `SELECT p.id, p.username, p.created_at, p.last_seen, s.value, s.updated_at,
                (SELECT COUNT(*) FROM sessions x WHERE x.player_id = p.id) AS devices
           FROM players p LEFT JOIN saves s ON s.player_id = p.id AND s.save_key = ?
          ORDER BY p.last_seen DESC LIMIT 2000`, [KEY]);
      let list = rows.map((r) => ({
        id: r.id, username: r.username, createdAt: r.created_at, lastSeen: r.last_seen, devices: Number(r.devices),
        savedAt: r.updated_at, save: r.value ? summary(r.value) : null,
      }));
      if (q) {
        const needle = q.toLowerCase();
        list = list.filter((x) => [x.id, x.username, x.save?.founderName, x.save?.settlementName]
          .some((v) => v && String(v).toLowerCase().includes(needle)));
      }
      return send(res, 200, { players: list });
    }

    const m = /^\/players\/([0-9a-f]{32})(\/[a-z]+)?$/.exec(p);
    if (!m) throw new HttpError(404, 'Not found.');
    const [, id, action = ''] = m;
    const who = await player(id);

    if (req.method === 'GET' && action === '') {
      const [saves] = await pool.execute('SELECT value, updated_at FROM saves WHERE player_id = ? AND save_key = ?', [id, KEY]);
      const [copies] = await pool.execute('SELECT id, note, created_at FROM save_backups WHERE player_id = ? ORDER BY id DESC', [id]);
      const [devices] = await pool.execute('SELECT created_at, last_used FROM sessions WHERE player_id = ? ORDER BY last_used DESC', [id]);
      let snapshot = null, error = null;
      if (saves.length) {
        try {
          const s = unpack(saves[0].value);
          snapshot = { ...s, save: JSON.parse(s.save), profile: s.profile ? JSON.parse(s.profile) : null };
        } catch (e) { error = 'Save se ne može pročitati: ' + e.message; }
      }
      return send(res, 200, { player: who, devices, savedAt: saves[0]?.updated_at ?? null, snapshot, error, backups: copies });
    }

    if (req.method === 'GET' && action === '/raw') {
      const [saves] = await pool.execute('SELECT value FROM saves WHERE player_id = ? AND save_key = ?', [id, KEY]);
      if (!saves.length) throw new HttpError(404, 'Igrač nema save.');
      res.writeHead(200, {
        'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store',
        'Content-Disposition': `attachment; filename="save_${who.username || id}_${new Date().toISOString().slice(0, 10)}.txt"`,
      });
      return res.end(saves[0].value);
    }

    if (req.method === 'PUT' && action === '/save') {
      const { save, profile } = await readBody(req);
      const bad = problems(save, profile);
      if (bad.length) return send(res, 400, { error: 'Nisam spremio, igra bi odbila ovaj save.', problems: bad });
      const [saves] = await pool.execute('SELECT value FROM saves WHERE player_id = ? AND save_key = ?', [id, KEY]);
      if (!saves.length) throw new HttpError(404, 'Igrač nema save.');
      const old = unpack(saves[0].value);
      const s = save.state;
      const snapshot = {
        ...old,
        uploadId: crypto.randomBytes(16).toString('hex'), // a new id: the player's device asks which progress to keep
        savedAt: Date.now() / 1000,
        settlementName: s.settlementName ?? old.settlementName,
        epochId: s.epochId, stageId: s.stageId ?? old.stageId,
        population: s.population, simTime: s.simTime,
        save: JSON.stringify(save),
      };
      if (profile != null) {
        profile.name = String(profile.name).trim().replace(/\s+/g, ' ');
        snapshot.founderName = profile.name;
        snapshot.profile = JSON.stringify(profile);
      }
      await backup(id, 'Prije uređivanja');
      await pool.execute('UPDATE saves SET value = ? WHERE player_id = ? AND save_key = ?', [pack(snapshot), id, KEY]);
      return send(res, 200, { ok: true });
    }

    if (req.method === 'POST' && action === '/restore') {
      const { backupId } = await readBody(req);
      const [rows] = await pool.execute('SELECT value FROM save_backups WHERE id = ? AND player_id = ?', [Number(backupId), id]);
      if (!rows.length) throw new HttpError(404, 'Nema te kopije.');
      const snapshot = unpack(rows[0].value);
      snapshot.uploadId = crypto.randomBytes(16).toString('hex');
      await backup(id, 'Prije vraćanja kopije');
      await pool.execute(
        'INSERT INTO saves (player_id, save_key, value) VALUES (?, ?, ?) ON DUPLICATE KEY UPDATE value = VALUES(value)',
        [id, KEY, pack(snapshot)]);
      return send(res, 200, { ok: true });
    }

    if (req.method === 'POST' && action === '/account') {
      const body = await readBody(req);
      if (body.action === 'rename') {
        const problem = usernameProblem(body.username);
        if (problem) throw new HttpError(400, problem);
        if (!who.username) throw new HttpError(400, 'Igrač je gost: korisničko ime veže sam u igri (ili postavi i lozinku ispod).');
        try { await pool.execute('UPDATE players SET username = ? WHERE id = ?', [body.username, id]); }
        catch (e) { if (e.code === 'ER_DUP_ENTRY') throw new HttpError(409, 'To korisničko ime već postoji.'); throw e; }
      } else if (body.action === 'password') {
        const problem = passwordProblem(body.password) || (!who.username && usernameProblem(body.username));
        if (problem) throw new HttpError(400, problem);
        try {
          await pool.execute('UPDATE players SET username = COALESCE(username, ?), pass_hash = ? WHERE id = ?',
            [who.username ? null : body.username, hashPassword(body.password), id]);
        } catch (e) { if (e.code === 'ER_DUP_ENTRY') throw new HttpError(409, 'To korisničko ime već postoji.'); throw e; }
      } else if (body.action === 'unbind') {
        await pool.execute('UPDATE players SET username = NULL, pass_hash = NULL WHERE id = ?', [id]);
      } else if (body.action === 'signout') {
        await pool.execute('DELETE FROM sessions WHERE player_id = ?', [id]);
      } else {
        throw new HttpError(400, 'Nepoznata radnja.');
      }
      return send(res, 200, { ok: true });
    }

    if (req.method === 'DELETE' && action === '') {
      await backup(id, `Prije brisanja igrača ${who.username || ''}`.trim());
      await pool.execute('DELETE FROM players WHERE id = ?', [id]);
      return send(res, 200, { ok: true });
    }

    throw new HttpError(404, 'Not found.');
  }

  // Returns true when the request was for /admin.
  return async function handle(req, res, url) {
    const p = url.pathname;
    if (p !== '/admin' && p !== '/admin/' && !p.startsWith('/admin/api/')) return false;
    try {
      if (p === '/admin' || p === '/admin/') {
        if (!enabled) throw new HttpError(404, 'Admin is off. Set ADMIN_PASSWORD (12+ characters) in /etc/founders-cloud.env.');
        res.writeHead(200, {
          'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store', 'X-Frame-Options': 'DENY',
          'Content-Security-Policy': "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; frame-ancestors 'none'",
          'Referrer-Policy': 'no-referrer',
        });
        res.end(PAGE);
        return true;
      }
      if (!enabled) throw new HttpError(404, 'Admin is off.');
      await api(req, res, p.slice('/admin/api'.length), url);
    } catch (e) {
      const status = e instanceof HttpError ? e.status : 500;
      if (!(e instanceof HttpError)) console.error(new Date().toISOString(), 'admin', req.method, req.url, e);
      if (!res.headersSent) send(res, status, { error: e instanceof HttpError ? e.message : 'Greška na serveru.' });
    }
    return true;
  };
};

module.exports.problems = problems;
module.exports.pack = pack;
module.exports.unpack = unpack;
