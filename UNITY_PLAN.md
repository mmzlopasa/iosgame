# EONWARD na Unityju — plan

Ista igra kao Swift prototip (*EONWARD — Rise Through the Ages*): prapovijesna zajednica uz rijeku postaje stalno selo, a isto naselje i iste odluke trebaju trajati kroz sve buduće epohe. Unity donosi **iPhone + Android**, 3D prikaz, Asset Store i standardni engine za zvuk, efekte i kasniji multiplayer (DECISIONS D10).

## Kako je projekt složen

| Dio | Mapa | Napomena |
|---|---|---|
| Pravila igre (EonwardCore) | `unity/Assets/Scripts/Core/` | C# port Swift paketa `Core/`, 1:1. Ne ovisi o Unityju (`noEngineReferences`). |
| Sadržaj i balans | `unity/Assets/Resources/content.json` | Ista datoteka kao u Swift verziji: zgrade, tehnologije, eventi, ciljevi, regije, karta. |
| Testovi pravila | `unity/CoreTests/` | 45 testova prenesenih iz Swifta, rade s `dotnet test` (bez Unityja i licence). |
| Prikaz | `unity/Assets/Scripts/*.cs` | 3D karta 16×16 iz `content.json`, magla, zgrade, ghost postavljanje, seljani, kamera. |
| Sučelje | `unity/Assets/Scripts/Hud.cs` | Svi ekrani iz SwiftUI verzije: Build, People, Research, Goals, Map & Legacy, event, milestone, Dev. |
| Build | `unity/Assets/Editor/BuildScript.cs` + `.github/workflows/unity-build.yml` | Scena i materijali se generiraju iz koda, pa editor nije potreban. |

Novi `.cs` fajlovi dobivaju Unity `.meta` preko `python3 scripts/gen-unity-meta.py`.

## Faze

| Faza | Sadržaj | Stanje |
|---|---|---|
| 1 | Port pravila u C# + 45 testova (scripted igrač dolazi do sela za 17 min 10 s, isto kao Swift) | ✅ |
| 2 | Unity prikaz i UI cijelog vertikalnog presjeka (Epoha 1: kamp → stabilno selo) | 🟡 napisano, čeka prvi Unity build |
| 3 | Prvi build na uređaju (iPhone + Android), popravci sučelja i dodira | ⬜ |
| 4 | Prava grafika i zvuk: modeli (Asset Store ili vlastiti), animacije ljudi, glazba, efekti | ⬜ |
| 5 | Playtest i balans (sve brojke su u `content.json`) | ⬜ |
| 6 | Pregled Epohe 2 (*First Civilizations*): isto naselje se transformira (successor zgrade, povijesni kvartovi) | ⬜ |
| 7 | Multiplayer planiranje (server-autoritativna ekonomija, iste naredbe kao API) | ⬜ |

Swift verzija (`Core/`, `App/`) ostaje kao referenca dok Unity build ne prođe na uređaju.
