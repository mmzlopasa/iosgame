# FOUNDERS cloud save: tvoj server

Igra **nikad ne spaja direktno na bazu** (lozinka baze bi bila u igri, a baza otvorena cijelom internetu). Umjesto toga na serveru radi mali program (API) koji jedini razgovara s bazom:

```
iPhone/Android  ──HTTPS──▶  Caddy (443)  ──▶  founders-cloud API (Node.js, 127.0.0.1:8787)  ──▶  MariaDB/MySQL (127.0.0.1:3306)
```

- Svaki igrač dobije svoj tajni ključ (token). Vidi i mijenja samo **svoj** save.
- Igrač može vezati korisničko ime i lozinku i prijaviti se na novom mobitelu. Lozinke se spremaju samo kao hash (scrypt), a tokeni kao SHA-256.
- Baza ostaje zatvorena prema van. Otvoreni su samo portovi 80 i 443.

Upute su za **Ubuntu ili Debian**. Naredbe se upisuju u terminal na serveru (preko SSH-a).

---

## 1. Domena (besplatno) – iPhone traži HTTPS

iPhone aplikacije smiju slati podatke samo preko **HTTPS-a**, a za HTTPS certifikat treba ime (domena), ne samo IP adresa.

- Ako već imaš domenu: dodaj A zapis, npr. `founders.tvojadomena.hr` → javna IP adresa servera.
- Ako nemaš: na **https://www.duckdns.org** se prijavi (Google/GitHub), upiši ime (npr. `founders-scyde`) i IP servera → **add domain**. Dobiješ `founders-scyde.duckdns.org`.

Provjera (nakon par minuta): `ping founders-scyde.duckdns.org` mora pokazati IP tvog servera.

## 2. Vatrozid (firewall)

```bash
sudo apt update
sudo apt install -y ufw
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status
```

Port **3306 (baza) NE otvaraj**. Ako ga je netko prije otvorio: `sudo ufw delete allow 3306`.

Ako server ima i vatrozid kod hostinga (Hetzner, OVH, DigitalOcean…), i tamo otvori 80 i 443.

## 3. Baza

Prebaci mapu `server/cloud` iz repozitorija na server (najlakše `git clone`):

```bash
sudo apt install -y git
sudo git clone https://github.com/mmzlopasa/iosgame.git /opt/iosgame
sudo mkdir -p /opt/founders-cloud
sudo cp -r /opt/iosgame/server/cloud/* /opt/founders-cloud/
```

Napravi bazu i tablice:

```bash
sudo mysql < /opt/founders-cloud/schema.sql
```

Napravi korisnika baze samo za igru. **Smisli svoju jaku lozinku** umjesto `OVDJE-JAKA-LOZINKA`:

```bash
sudo mysql -e "CREATE USER 'founders'@'localhost' IDENTIFIED BY 'OVDJE-JAKA-LOZINKA';
GRANT SELECT, INSERT, UPDATE, DELETE ON founders.* TO 'founders'@'localhost';
FLUSH PRIVILEGES;"
```

Provjeri da baza sluša samo lokalno: `sudo ss -ltnp | grep 3306` treba pokazati `127.0.0.1:3306`. Ako piše `0.0.0.0:3306`, u `/etc/mysql/mariadb.conf.d/50-server.cnf` (MySQL: `/etc/mysql/mysql.conf.d/mysqld.cnf`) postavi `bind-address = 127.0.0.1` i pokreni `sudo systemctl restart mariadb`.

## 4. Node.js i API

```bash
sudo apt install -y nodejs npm
node --version            # treba v18 ili noviji
```

Ako je verzija manja od 18 (npr. Ubuntu 22.04), instaliraj noviju:

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs
```

Zatim:

```bash
cd /opt/founders-cloud
sudo npm ci --omit=dev
sudo useradd --system --no-create-home --shell /usr/sbin/nologin founders
```

Postavke (ovdje upiši istu lozinku baze kao u koraku 3):

```bash
sudo cp /opt/founders-cloud/founders-cloud.env.example /etc/founders-cloud.env
sudo nano /etc/founders-cloud.env          # promijeni DB_PASSWORD, spremi: Ctrl+O, Enter, Ctrl+X
sudo chmod 600 /etc/founders-cloud.env
```

Pokreni kao servis (sam se pali nakon restarta servera):

```bash
sudo cp /opt/founders-cloud/founders-cloud.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now founders-cloud
sudo systemctl status founders-cloud       # treba pisati "active (running)"
curl http://127.0.0.1:8787/health          # treba vratiti {"ok":true}
```

Ako ne radi: `sudo journalctl -u founders-cloud -n 50` pokaže zašto (najčešće kriva lozinka baze u `/etc/founders-cloud.env`).

## 5. HTTPS (Caddy)

Caddy sam dobije i obnavlja besplatni HTTPS certifikat (Let's Encrypt).

```bash
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install -y caddy
```

Postavi svoju domenu (zamijeni `founders-scyde.duckdns.org` svojom):

```bash
sudo cp /opt/founders-cloud/Caddyfile /etc/caddy/Caddyfile
sudo sed -i 's/founders.example.duckdns.org/founders-scyde.duckdns.org/' /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

Ako na serveru već radi nginx ili Apache na portu 443, nemoj instalirati Caddy. Javi mi i dat ću ti postavke za nginx.

## 6. Provjera s bilo kojeg računala

```bash
curl https://founders-scyde.duckdns.org/health
```

Mora vratiti `{"ok":true}`. Cijeli test (napravi testnog igrača, spremi, veže račun, prijavi se):

```bash
cd /opt/founders-cloud && node test.js https://founders-scyde.duckdns.org
```

Treba ispisati `all checks passed`.

## 7. Javi adresu

Pošalji mi adresu (npr. `https://founders-scyde.duckdns.org`). Upisat ću je u igru (`CloudConfig.ServerUrl` u `unity/Assets/Scripts/Cloud/CloudBackend.cs`), a sljedeći build sprema u tvoju bazu.

## 8. Sigurnosna kopija (preporuka)

Svaku noć kopija baze, čuva se 14 dana:

```bash
sudo mkdir -p /var/backups/founders
echo '0 3 * * * root mysqldump --single-transaction founders | gzip > /var/backups/founders/founders-$(date +\%F).sql.gz && find /var/backups/founders -mtime +14 -delete' | sudo tee /etc/cron.d/founders-backup
```

## 9. Admin stranica (svi igrači)

Na adresi **https://tvoja-domena/admin** (npr. https://founders.scyde.dev/admin) je stranica za upravljanje igračima:

- popis svih igrača s traženjem (ime osnivača, naselje, korisničko ime, ID),
- uređivanje: ime osnivača, portret, okvir, ime naselja, broj ljudi, resursi; u kartici **Napredno** cijeli save,
- račun: promjena korisničkog imena, nova lozinka, odjava sa svih uređaja, odvezivanje računa, brisanje igrača,
- **Kopije**: prije svake promjene sama spremi kopiju savea (zadnjih 30 po igraču) i možeš je vratiti jednim klikom.

Prije spremanja provjeri je li save ispravan, pa igra neće odbiti uređeni save. Igrač zatim u igri otvori
Settings → Cloud Save → Load from cloud.

**Uključivanje** (jednom). Smisli dugu lozinku (barem 12 znakova) i upiši je umjesto `OVDJE-ADMIN-LOZINKA`:

```bash
cd /opt/iosgame && sudo git pull
sudo cp server/cloud/server.js server/cloud/admin.js server/cloud/admin.html server/cloud/test-admin.js server/cloud/schema.sql /opt/founders-cloud/
sudo mysql < /opt/founders-cloud/schema.sql
echo 'ADMIN_PASSWORD=OVDJE-ADMIN-LOZINKA' | sudo tee -a /etc/founders-cloud.env
sudo systemctl restart founders-cloud
```

Otvori https://tvoja-domena/admin i prijavi se tom lozinkom. Prijava traje 12 sati. Nakon 10 krivih lozinki u 15
minuta prijava se privremeno blokira. Lozinku mijenjaš u `/etc/founders-cloud.env` (`sudo nano /etc/founders-cloud.env`),
pa `sudo systemctl restart founders-cloud`. Bez `ADMIN_PASSWORD` stranica je isključena.

Provjera na serveru: `cd /opt/founders-cloud && sudo ADMIN_PASSWORD='OVDJE-ADMIN-LOZINKA' node test-admin.js`. Treba ispisati `admin: all checks passed`.

## Nadogradnja servera kasnije

```bash
cd /opt/iosgame && sudo git pull
sudo cp server/cloud/server.js server/cloud/admin.js server/cloud/admin.html server/cloud/schema.sql server/cloud/package*.json /opt/founders-cloud/
sudo mysql < /opt/founders-cloud/schema.sql
cd /opt/founders-cloud && sudo npm ci --omit=dev && sudo systemctl restart founders-cloud
```

---

## API (za programere)

| Metoda | Put | Tijelo | Odgovor |
|---|---|---|---|
| POST | `/v1/guest` | `{deviceId}` | `{playerId, token}`: novi gost |
| POST | `/v1/login` | `{username, password}` | `{playerId, token}` |
| POST | `/v1/link` | `{username, password}` + token | veže račun na igrača |
| POST | `/v1/logout` | token | briše token |
| GET | `/v1/save/:key` | token | `{value}` ili 404 |
| PUT | `/v1/save/:key` | `{value}` + token | sprema (do 6 MB) |
| GET | `/health` | | `{ok:true}` |

Token se šalje kao `Authorization: Bearer <token>`. Nakon 10 krivih lozinki u 15 minuta prijava se privremeno blokira.
