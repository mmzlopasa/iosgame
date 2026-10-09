# Dnevnik implementacije

## 2026-10-09 — Sesija 1

**Okruženje (provjereno):** Windows 11 Pro, PowerShell 5.1, .NET 9 runtime (bez SDK-a), nema Gita, Unityja, Node.js-a ni Maca/Xcodea. Mapa projekta bila je prazna.

**Instalirano (uz dopuštenje korisnika):** Visual Studio 2022 Build Tools 17.14 (MSVC + Windows SDK 10.0.22621), Swift 6.4.0 toolchain (winget je usput instalirao i Python 3.10 kao ovisnost).

**Napravljeno:**
- `Core/` (EonwardCore): kompletna domenska logika prvog vertikalnog presjeka, `content.json` sa svim sadržajem i balansom.
- `Core/Tests/`: 45 testova (sadržaj/validacija, ekonomija, postavljanje, gradnja/upgrade, nastavak/offline, istraživanje, eventi/milestone/ciljevi/istraživanje karte, save/korupcija/verzije, skriptirani prolaz).
- `App/`: SwiftUI + SpriteKit iOS aplikacija (scena, GameStore, paneli, event, milestone overlay, developer panel, haptika).
- `project.yml` (XcodeGen), `.github/workflows/build-ipa.yml` (testovi + nepotpisana IPA + „latest-build” release).
- Placeholder ikona (`scripts/make-icon.ps1`), `.gitignore`, dokumentacija.

**Stvarno pokrenute provjere:**
- `swift build` za EonwardCore na Windowsu: uspjeh (prvi pokušaj je otkrio grešku sa zasjenjenim imenom metode u `Placement.swift`, koja je popravljena).
- `swift test` na Windowsu: **45/45 prolazi**. Prvi run je imao 2 neuspjela testa zbog grešaka u samim testovima (nagrada cilja +4 znanja, ćelija (3,3) je šuma); testovi su ispravljeni.
- Skriptirani igrač: „A Stable Village” postignut nakon **17 min 10 s** igre, 8/8 ciljeva.

**Nije provjereno:**
- iOS sloj (`App/`) **nije kompajliran**: SwiftUI, SpriteKit i UIKit ne postoje na Windowsu. Prvi kompajl bit će na GitHub Actionsu.
- .ipa još nije izgrađena, a igra nije pokrenuta ni na simulatoru ni na uređaju.
- Safe area, čitljivost i dodir nisu provjereni na stvarnom iPhoneu.

**Poznata ograničenja:**
- Grafika je proceduralni placeholder.
- Nema zvuka.
- iPad koristi isti layout (radi, ali nije posebno prilagođen).
