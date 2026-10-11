# Dnevnik implementacije

## 2026-10-09 — Sesija 1

**Okruženje (provjereno):** Windows 11 Pro, PowerShell 5.1, .NET 9 runtime (bez SDK-a), nema Gita, Unityja, Node.js-a ni Maca/Xcodea. Mapa projekta bila je prazna.

**Instalirano (uz dopuštenje korisnika):** Visual Studio 2022 Build Tools 17.14 (MSVC + Windows SDK 10.0.22621), Swift 6.4.0 toolchain (winget je usput instalirao i Python 3.10 kao ovisnost).

**Napravljeno:**
- `Core/` (FoundersCore): kompletna domenska logika prvog vertikalnog presjeka, `content.json` sa svim sadržajem i balansom.
- `Core/Tests/`: 45 testova (sadržaj/validacija, ekonomija, postavljanje, gradnja/upgrade, nastavak/offline, istraživanje, eventi/milestone/ciljevi/istraživanje karte, save/korupcija/verzije, skriptirani prolaz).
- `App/`: SwiftUI + SpriteKit iOS aplikacija (scena, GameStore, paneli, event, milestone overlay, developer panel, haptika).
- `project.yml` (XcodeGen), `.github/workflows/build-ipa.yml` (testovi + nepotpisana IPA + „latest-build” release).
- Placeholder ikona (`scripts/make-icon.ps1`), `.gitignore`, dokumentacija.

**Stvarno pokrenute provjere:**
- `swift build` za FoundersCore na Windowsu: uspjeh (prvi pokušaj je otkrio grešku sa zasjenjenim imenom metode u `Placement.swift`, koja je popravljena).
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
- `swift test` za FoundersCore na macOS-u: **uspjeh**.
- `xcodegen generate` + nepotpisani `xcodebuild` (Release, iphoneos): **uspjeh iz prvog pokušaja**, bez ijedne izmjene koda u `App/`.
- `Founders.ipa` (≈560 KB) objavljena u pre-releaseu **„Latest build (unsigned)”**:
  https://github.com/mmzlopasa/iosgame/releases/download/latest-build/Founders.ipa

**Provjereno na samoj IPA datoteci:** `Payload/Founders.app` sadrži Mach-O arm64 izvršnu datoteku, `Info.plist` (bundle id `com.founders.prototype`, verzija 0.1.0, MinimumOSVersion 16.0), ikone, `Assets.car` i `FoundersCore_FoundersCore.bundle/content.json`.

**I dalje nije provjereno:** pokretanje na stvarnom iPhoneu (instalacija, prikaz, safe area, dodir, haptika). To je sljedeći korak preko Sideloadlyja ili Feathera.

## 2026-10-09 — Sesija 2b: prelazak na Unity (FOUNDERS, ne kopija druge igre)

**Odluka:** korisnik je odabrao Unity radi Androida, 3D grafike, Asset Storea i rasta (DECISIONS D10). Igra ostaje FOUNDERS. Kratki pokušaj „frozen settlement” scene je uklonjen.

**Napravljeno:**
- `unity/Assets/Scripts/Core/`: C# port cijelog `FoundersCore` (1:1 pravila, isti `content.json`, isti save format i validacija).
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

## 2026-10-11 — Novi UI (UI Toolkit), loading screen, ikona aplikacije

**Dizajn:** odobren dizajn (Design canvas „Founders — UI dizajn”, 10 ekrana, stil scyhost: noćna navy pozadina, stakleni paneli, zlatni akcenti, fontovi Sora / Geist / Geist Mono).

**Napravljeno:**
- `Hud.cs` (IMGUI) zamijenjen s `Scripts/UI/` (UI Toolkit, sve iz koda):
  - HUD: avatar, naselje i era, 5 resursa, kartica cilja, brzi gumbi, mjerači vremena (istraživanje, gradnja, izviđanje), toastovi, donji izbornik.
  - Kartice: postavljanje zgrade, odabrana zgrada (radnici, nadogradnja).
  - Sheetovi: Gradnja (kategorije), Ljudi i rad, Istraživanje, Ciljevi, Svijet i Legacy Timeline, Profil, Postavke (kvaliteta grafike, štednja baterije, haptika, alati za razvoj).
  - Dijalozi: događaj (odluka), milestone, „dok te nije bilo” (offline izvještaj iz stvarne simulacije).
  - Loading screen dok se svijet generira.
- `tools/ui/build_ui_assets.py`:
  - 28 ikona, logo, pozadina za loading, zlatni prsten avatara i ikona aplikacije 1024×1024, iscrtani headless Chromiumom iz SVG-a.
  - Fontovi (SIL OFL) izrezani na statične težine pomoću fontToolsa.
- `Editor/UiSetup.cs`:
  - Tema i PanelSettings.
  - iOS ikona za sve veličine.
  - Launch screen.
  - Unity splash isključen.
- MSAA 4× je zamijenjen SMAA-om, zbog RenderPass greške na Metalu.
- Capture mode sada snima i nove ekrane (00–18).
- `GameStore`: `OfflineReport`, `FocusBuilding`.
- Dijagnostički log terena.

**Provjereno:**
- `scripts/unity-compile-check.sh` prolazi bez grešaka.
- `dotnet test unity/CoreTests` prolazi 45/45.

**Nije provjereno:** izgled u simulatoru. Provjerit će se kad se završe CI preview snimke.
