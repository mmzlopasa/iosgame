# Arhitektonske odluke

## D1 — Nativni Swift + SpriteKit umjesto Unityja (2026-10-09)
**Kontekst:** brief predlaže Unity 6.3, ali razvojno računalo je Windows bez Maca, a isporuka mora biti `.ipa` za Sideloadly/Feather. iOS build uvijek zahtijeva macOS + Xcode. Unity bi tražio ~15 GB instalacije, licencu u CI-ju i Unityjev Xcode export.
**Odluka (odabrao korisnik):** nativni Swift, SpriteKit za izometrijsku scenu i SwiftUI za sučelje. Gradi se na GitHub Actions macOS runneru, bez licenci.
**Posljedice:** iOS sloj se kompajlira samo na CI-ju. Zato su sva pravila igre u zasebnom paketu `FoundersCore`, koji se kompajlira i testira i na Windowsu (Swift 6.4 toolchain).

## D2 — Domena kao zaseban Swift paket (`Core/`)
Pravila ne ovise o UIKitu ni SpriteKitu. UI samo čita `game.state` i poziva naredbe (`placeBuilding`, `setWorkers`, …), pa nije autoritet za pravila. To olakšava kasniji prelazak na server-autoritativni multiplayer: iste naredbe postaju API pozivi.

## D3 — Sadržaj u validiranom JSON-u
Sav sadržaj i sve brojke balansa su u `content.json`, uz stabilne string ID-jeve. `ContentValidator` pri učitavanju provjerava duplikate, reference, cikluse preduvjeta tehnologija, kartu i footprint upgradea. Nova epoha ili zgrada je promjena podataka, ne logike.
Iznimka: 4 resursa i 6 poslova su Swift enumi radi sigurnosti tipova. Novi resurs zato traži malu promjenu koda, što je namjerno, jer brief traži da se resursi ne dodaju olako.

## D4 — Vrijeme: fiksni korak simulacije + sidro zidnog sata
`Game.update()` simulira točno interval `[lastUpdateWallTime, now]` u fiksnim koracima od 1 s, a zatim pomakne sidro. Zato nastavak iz pozadine, restart ili višestruki pozivi nikad ne dupliraju proizvodnju ni završetke gradnje. Ako sat ide unatrag, sidro se ponovno postavlja bez nagrade. Offline napredak je ograničen (`maxOfflineSeconds` = 4 h). Istek modifikatora koristi simulacijsko vrijeme, pa je simulacija deterministička. Sat je injektabilan (`GameClock`, `ManualClock` u testovima).

## D5 — Save: verzionirani JSON iza `SaveStore` sučelja
`SaveEnvelope { saveVersion, contentVersion, savedAtWallTime, state }` piše se atomski u Application Support. Učitavanje provjerava verziju (novija = ne dira se), dekodiranje (neispravno = datoteka se premješta u `.corrupt` i kreće nova igra) i strukturu stanja (negativni resursi, nepoznati ID-jevi, više radnika nego ljudi). Cloud ili server implementacija kasnije zamjenjuje `FileSaveStore`.

## D6 — Nestašica hrane ne ubija
Kad hrane nema: upozorenje, −40 % produktivnosti i stop rasta. Nitko ne umire, pa izbivanje iz igre ne kažnjava katastrofalno.

## D7 — Identitet zgrada kroz vrijeme
`PlacedBuilding.id` je stabilan. Upgrade (`upgradesTo`) i milestone transformacije (`buildingTransforms`) mijenjaju samo `definitionId` i dodaju ga u `history`, a pozicija i ID ostaju. `successorId` u definiciji označava nasljednika iz kasnije ere. To je temelj za buduće „historic districts” i Legacy Timeline, koja se već sprema u `state.legacy`.

## D8 — Nepotpisana IPA
CI gradi s `CODE_SIGNING_ALLOWED=NO` i pakira `Payload/Founders.app` u zip (.ipa). Potpisivanje rade Sideloadly ili Feather. U repozitoriju nema certifikata ni tajni.

## D9 — UI: SwiftUI preko SpriteKita
SwiftUI za touch-heavy panele (safe area, Dynamic Type, sheetovi s detentima), a `SpriteView` za scenu. Scena dobiva tapove i javlja ih `GameStore`-u. Ne mijenja stanje sama.

## D10 — Prelazak na Unity 6.3 LTS (2026-10-09)
**Kontekst:** korisnik želi Android uz iPhone, 3D i bogatiju grafiku, Asset Store te standardni engine za zvuk, efekte i multiplayer. Swift verzija radi samo na iOS-u.
**Odluka (odabrao korisnik):** Unity 6.3 LTS (6000.3.26f1). Igra i dizajn ostaju isti (FOUNDERS, Epoha 1).
**Kako:** pravila su prenesena 1:1 u C# (`unity/Assets/Scripts/Core`) bez ovisnosti o Unityju, pa se testiraju običnim `dotnet test` (45 testova, isti scenariji i brojke kao u Swiftu). Scena, materijali i UI nastaju iz koda (`BuildScript.cs`, IMGUI), pa se igra može razvijati iz cloud sesije bez otvaranja editora. GitHub Actions (GameCI) gradi Android APK i iOS Xcode projekt, a macOS runner pravi nepotpisanu IPA.
**Posljedice:** Unity build traži besplatnu Personal licencu kao GitHub secret. IMGUI je privremen i kasnije prelazi na uGUI ili UI Toolkit s pravim dizajnom. GPLv3 licenca je zamijenjena vlasničkom licencom „sva prava pridržana” (D11).

## D11 — Licenca: sva prava pridržana (2026-10-10)
**Odluka (odabrao korisnik):** GPLv3 je zamijenjena vlasničkom licencom. Repozitorij ostaje javan, jer javni repozitoriji imaju besplatne macOS buildove, ali korištenje i kopiranje nije dopušteno bez pisanog dopuštenja. Time nema sukoba s Unityjem ni s App Storeom i Google Playem.

## D12 — Novo ime: Founders — The Long Rise (2026-10-10)
**Odluka (odabrao korisnik):** projekt i igra se zovu **Founders — The Long Rise** (ranije radni naziv „EONWARD — Rise Through the Ages”). Ime na početnom zaslonu je *Founders*.
**Promijenjeno:** `content.json` branding, Swift paket `FoundersCore`, Swift app `Founders`, C# namespace `Founders` / `Founders.Core` (asmdef), editor klasa `FoundersEditor.BuildScript`, bundle ID-jevi `com.founders.longrise` (Unity) i `com.founders.prototype` (Swift), buildovi `Founders.ipa` / `Founders.apk`, ime save datoteke `founders_save.json` i dokumentacija.
**Posljedice:** novi bundle ID znači da se igra na mobitelu instalira kao nova aplikacija. Stari save (`eonward_save.json`) se ne prenosi. Naziv „Founders” nije provjeren kao žig: prije objave provjeriti App Store, Google Play, EUIPO i USPTO. Repozitorij se i dalje zove `iosgame`; GitHub ime repozitorija mijenja samo vlasnik (Settings → General → Repository name).
