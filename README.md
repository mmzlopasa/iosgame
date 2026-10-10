# EONWARD — Rise Through the Ages

*Build a world that outlives you.* (radni naziv, nije pravno provjeren)

Mobilna strategija za iPhone: mala prapovijesna zajednica uz rijeku postaje stalno selo. Isto naselje, ista geografija i iste odluke trebaju trajati kroz sve buduće epohe.

**Stanje:** prvi igrivi vertikalni presjek (Epoha 1: kamp → stabilno selo). Radi offline, bez računa i bez mreže.

## Unity verzija (iPhone + Android) — novo

Ista igra (EONWARD, Epoha 1) prelazi na **Unity 6.3 LTS (6000.3.26f1)** radi Androida, 3D grafike i Asset Storea. Pravila su prenesena u C# i testirana (`unity/CoreTests`, 45/45), a prikaz i sučelje su u `unity/Assets/Scripts`. Plan i faze: [`UNITY_PLAN.md`](UNITY_PLAN.md).

**Preuzimanje buildova:** Releases → **„Unity build (unsigned IPA + APK)”** (tag `unity-latest`):
- iPhone: https://github.com/mmzlopasa/iosgame/releases/download/unity-latest/Eonward.ipa (Feather / Sideloadly)
- Android: https://github.com/mmzlopasa/iosgame/releases/download/unity-latest/Eonward.apk

**Jednokratno postavljanje licence (na računalu):**
1. Unity račun (id.unity.com) → Unity Hub → *Preferences → Licenses → Add → Get a free personal license*.
2. U Hubu instaliraj editor **6000.3.26f1** (Installs → Install Editor → Archive) s modulom *iOS Build Support* (i *Android Build Support* po želji).
3. Sadržaj `C:\ProgramData\Unity\Unity_lic.ulf` spremi kao GitHub secret `UNITY_LICENSE`, uz `UNITY_EMAIL` i `UNITY_PASSWORD` (Settings → Secrets and variables → Actions).

Bez tih secreta Unity workflow samo javlja upozorenje i ništa ne gradi.

Swift verzija (`Core/`, `App/`) ostaje kao referenca dok se pravila ne prenesu u C#.

## Struktura

| Mapa | Sadržaj |
|---|---|
| `Core/` | Swift paket **EonwardCore**: sva pravila igre (ekonomija, gradnja, poslovi, istraživanje, eventi, ciljevi, milestone, istraživanje karte, save). Bez UIKit/SpriteKit, testira se na Windowsu, macOS-u i Linuxu. |
| `Core/Sources/EonwardCore/Resources/content.json` | **Sav sadržaj i balans**: zgrade, tehnologije, eventi, ciljevi, regije, karta, brojke. |
| `Core/Tests/` | 45 unit testova, uključujući skriptiranog igrača koji odigra cijeli tutorial. |
| `App/` | iOS aplikacija: SwiftUI sučelje + SpriteKit izometrijska scena. |
| `project.yml` | XcodeGen specifikacija. Xcode projekt se generira na CI-ju, nije u gitu. |
| `.github/workflows/build-ipa.yml` | GitHub Actions: testovi + nepotpisana `Eonward.ipa`. |
| `scripts/` | `test-core-windows.ps1` (testovi na Windowsu), `make-icon.ps1` (placeholder ikona). |

## Kako dobiti .ipa (bez Maca)

Na Windowsu nije moguće izgraditi iOS aplikaciju. To radi GitHub Actions na besplatnom macOS runneru.

1. Napravi **novi repozitorij** na GitHubu (preporuka: javni, jer javni repozitoriji imaju neograničene macOS minute).
2. Pushaj ovu mapu u njega na granu `main` (vidi „Git” niže).
3. Na GitHubu otvori **Actions → Build iOS IPA** i pričekaj zeleni rezultat (~5–10 min).
4. Preuzmi `Eonward.ipa`:
   - **Releases → „Latest build (unsigned)”** → `Eonward.ipa`. Ovaj URL možeš zalijepiti izravno u **Feather**.
   - ili **Actions → zadnji run → Artifacts → Eonward-ipa** (zip u kojem je .ipa).
5. Instalacija:
   - **Sideloadly** (Windows): spoji iPhone, povuci `Eonward.ipa` u Sideloadly, upiši svoj Apple ID i klikni Start. Na iPhoneu zatim: *Postavke → Općenito → VPN i upravljanje uređajima → vjeruj developeru*. Ako se traži, uključi i *Developer Mode*.
   - **Feather** (na iPhoneu): uvezi .ipa (URL ili datoteka), potpiši svojim certifikatom i instaliraj.

.ipa je namjerno **nepotpisana**. Sideloadly i Feather je potpisuju tvojim Apple ID-jem ili certifikatom. S besplatnim Apple ID-jem aplikacija istječe nakon 7 dana i treba je ponovno potpisati. Save ostaje sačuvan ako se ne briše aplikacija.

Bundle ID je `com.eonward.prototype` (mijenja se u `project.yml`).

## Git (prvi put)

Git nije instaliran na ovom računalu. Najjednostavnije je instalirati **GitHub Desktop** ili Git:

```bash
winget install --id Git.Git -e
```

Zatim u ovoj mapi:

```bash
git init -b main
```
```bash
git add .
```
```bash
git commit -m "EONWARD vertical slice"
```
```bash
git remote add origin https://github.com/<tvoj-račun>/eonward.git
```
```bash
git push -u origin main
```

## Testovi domenske logike na Windowsu

Potrebni su Swift toolchain i Visual Studio Build Tools (instalirani u prvoj sesiji):

```bash
powershell -ExecutionPolicy Bypass -File scripts\test-core-windows.ps1
```

Skripta kopira `Core/` u kratku putanju (`%TEMP%\eonw`), jer SwiftPM na Windowsu ne podnosi dugačke putanje, i pokreće `swift test` unutar MSVC okruženja.

## Kako se igra (prva sesija, oko 15–20 min)

1. **People**: rasporedi besposlene ljude. Hrana mora rasti (cilj 1).
2. **Build** → odaberi zgradu → tapni tlo → *Build here*. Zelena ploča znači da je mjesto valjano, crvena da nije (uz razlog).
3. Kamen se vadi samo uz stijene (Stone Gathering Site), drvo uz šumu.
4. **Research**: znanje proizvode istraživači (vatra ima 1 mjesto, Community Circle 3).
5. Nakon prve tehnologije stiže event *A Poor Foraging Season* s tri izbora. Treći izbor traži Storage Pit i nagrađuje pripremu.
6. **Map**: izviđači otkrivaju novu zemlju. Istočni greben otvara drugi event, a tamo je i **Legacy Timeline**.
7. **Objectives** → *Found the village* kad su svi uvjeti zeleni. Vatra postaje *Village Hearth* (isti ID i pozicija, povijest se čuva), a pojave se staze.
8. Zupčanik gore desno otvara Developer panel: dijagnostiku, fast-forward, dodavanje resursa i reset.

Kontrole: tap za odabir i postavljanje, povlačenje za pomicanje kamere, pinch za zoom.

## Placeholderi (nije finalna umjetnost)

- Sve grafike zgrada, terena i drveća crtaju se **proceduralno u kodu** (`App/Scene/SpriteFactory.swift`). To su jednostavni placeholderi.
- Ikona aplikacije je placeholder generiran skriptom `scripts/make-icon.ps1`.
- Nema zvuka. Nema tuđih ni nelicenciranih asseta.

## Licence / pravno

Kod i sadržaj su **vlasnički, sva prava pridržana** (vidi `LICENSE`). Repozitorij je javan samo radi pregleda i besplatnih GitHub buildova. Zamijenjena je prijašnja GPLv3 licenca, koja se ne slaže s Unityjem ni s objavom u trgovinama.

Naziv „EONWARD” je radni naziv i nije provjeren kao žig. Prije javne objave treba provjeriti App Store, EUIPO, WIPO, USPTO i domene.
