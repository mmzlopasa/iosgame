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

## 2026-10-09 — Sesija 2 (Claude Cloud)

**Prvi CI build (GitHub Actions, run [#1](https://github.com/mmzlopasa/iosgame/actions/runs/37992205677), commit `f869e11`, macos-15):**
- `swift test` za EonwardCore na macOS-u: **uspjeh**.
- `xcodegen generate` + nepotpisani `xcodebuild` (Release, iphoneos): **uspjeh iz prvog pokušaja**, bez ijedne izmjene koda u `App/`.
- `Eonward.ipa` (≈560 KB) objavljena u pre-releaseu **„Latest build (unsigned)”**:
  https://github.com/mmzlopasa/iosgame/releases/download/latest-build/Eonward.ipa

**Provjereno na samoj IPA datoteci:** `Payload/Eonward.app` sadrži Mach-O arm64 izvršnu datoteku, `Info.plist` (bundle id `com.eonward.prototype`, verzija 0.1.0, MinimumOSVersion 16.0), ikone, `Assets.car` i `EonwardCore_EonwardCore.bundle/content.json`.

**I dalje nije provjereno:** pokretanje na stvarnom iPhoneu (instalacija, prikaz, safe area, dodir, haptika). To je sljedeći korak preko Sideloadlyja ili Feathera.

## 2026-10-09 — Sesija 2b: prelazak na Unity (EONWARD, ne kopija druge igre)

**Odluka:** korisnik je odabrao Unity radi Androida, 3D grafike, Asset Storea i rasta (DECISIONS D10). Igra ostaje EONWARD. Kratki pokušaj „frozen settlement” scene je uklonjen.

**Napravljeno:**
- `unity/Assets/Scripts/Core/`: C# port cijelog `EonwardCore` (1:1 pravila, isti `content.json`, isti save format i validacija).
- `unity/CoreTests/`: 45 Swift testova preneseno u NUnit.
- Unity prikaz: karta 16×16 iz `content.json` (trava, šuma, stijene, rijeka, pijesak), magla nad neistraženim regijama, 10 proceduralnih 3D modela zgrada, ghost postavljanje (zeleno/crveno), odabir, staze sela, seljani, vatra sa svjetlom, dimom i iskrama, kamera (povlačenje, pinch, tap).
- `Hud.cs`: svi ekrani iz SwiftUI verzije (IMGUI).
- `scripts/unity-compile-check.sh`: kompajlira sve Unity skripte prema pravim Unity 6000.3.26f1 DLL-ovima (iz javnog GameCI imagea), bez instalacije Unityja i bez licence.

**Stvarno pokrenute provjere (Linux cloud sesija, .NET 8):**
- `dotnet test unity/CoreTests`: **45/45 prolazi** iz prvog pokušaja.
- Scripted igrač: selo nakon **17 min 10 s**, 8/8 ciljeva (identično Swift verziji).
- Core se kompajlira kao .NET Standard 2.1 / C# 9 (Unityjev target).
- `scripts/unity-compile-check.sh`: sve Unity skripte (igra, UI, editor build skripta) kompajliraju se **bez grešaka i upozorenja**. Namjerno pogrešan poziv u kontrolnoj datoteci pravilno javlja grešku, dakle provjera je stvarna.

**Nije provjereno:**
- Unity build (APK/IPA) još nije pokrenut: čeka `UNITY_LICENSE`, `UNITY_EMAIL` i `UNITY_PASSWORD` secrete.
- Ponašanje u igri (izgled, dodir, performanse) nije viđeno ni u editoru ni na uređaju.
