# Plan implementacije

Legenda: ✅ gotovo i provjereno · 🟡 napisano, čeka provjeru (CI ili uređaj) · ⬜ nije započeto

## Milestone 0 — Okruženje
- ✅ Inspekcija: Windows 11, prazna mapa, nema Unityja, Gita ni Maca.
- ✅ Odluka o tehnologiji (DECISIONS D1) i build putu (.ipa preko GitHub Actions).
- ✅ Swift 6.4 + VS Build Tools instalirani; `EonwardCore` se kompajlira i testira na Windowsu.
- 🟡 Prvi CI run na GitHubu (korisnik mora pushati repo).

## Milestone 1 — Funkcionalno naselje
- ✅ Domena: resursi, skladište, poslovi i kapaciteti, proizvodnja i potrošnja, kvarenje, crafting bonus, rast populacije, nestašica bez smrti.
- ✅ Grid postavljanje: preklapanje, teren, neistraženo, susjedstvo (šuma/stijena), limit, red gradnje, cijena; trošak točno jednom.
- ✅ Timeri gradnje (brzina ovisi o graditeljima), upgrade na istom mjestu.
- ✅ Save/load: verzioniran, validiran, oporavak od korupcije, offline napredak bez dupliciranja.
- 🟡 SpriteKit izometrijska scena (teren, drveće, stijene, magla, zgrade, ghost, odabir, pan/zoom).
- 🟡 SwiftUI: top bar, bottom nav, build, people, detalji zgrade, toastovi, safe area.

## Milestone 2 — Prva sesija
- ✅ 8 tutorial ciljeva s nagradama (svaka točno jednom).
- ✅ 5 tehnologija s preduvjetima i validacijom ciklusa.
- ✅ Event „A Poor Foraging Season” (3 izbora, jedan traži pripremu) + event „A Useful Stone Deposit” (iz istraživanja).
- ✅ Milestone „A Stable Village”: gating, transformacija Campfire → Village Hearth.
- ✅ Skriptirani igrač dolazi do sela za ~17 min igre (test).
- 🟡 UI: research, objectives, event panel, milestone animacija, staze sela, haptika.

## Milestone 3 — Povijesni kontinuitet (dokaz)
- ✅ Legacy Timeline se puni (osnivanje, prve zgrade, tehnologije, odluke, istraživanje, milestone) i prikazuje u Map panelu.
- ✅ Rana odluka s trajnim efektom (kamenolom ili netaknuti greben).
- ✅ Zgrada koja prelazi u nasljednika uz isti ID i povijest (Campfire → Village Hearth, Shelter → Woven Shelter).
- ⬜ Pregled kako bi kasnija era transformirala isto naselje (npr. „preview” Epohe 2).

## Milestone 4 — Playtest
- ⬜ Instalirati na iPhone, odigrati prvih 20 min i zapisati gdje igrač zapne.
- ⬜ Lokalni log sesije (bez osobnih podataka) za tuning.
- ⬜ Balans prema opažanjima (sve brojke su u `content.json`).

## Milestone 5 — Multiplayer planiranje
- ⬜ Tek kad je offline petlja zabavna: usporedba backenda, autoritativna ekonomija, PoC regije za dva igrača.

## Sljedeći konkretni korak
Pushati repo na GitHub, pustiti workflow i popraviti eventualne greške pri kompajliranju iOS sloja. Zatim instalirati .ipa i odigrati prvu sesiju na uređaju.
