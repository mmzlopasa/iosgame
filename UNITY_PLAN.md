# EONWARD (Unity) — plan

Cilj: mobilna strategija za **iPhone i Android** u žanru „frozen survival city builder” (poput Whiteout Survivala), na Unityju.

## Pravna granica (važno)
Mehanike žanra (peć u središtu, preživjeli, temperatura, resursne zgrade, heroji, istraživanje, karta svijeta, savezi) **nisu zaštićene** i smijemo ih koristiti.
**Ne smijemo** kopirati: grafiku, modele, ikone, UI dizajn piksel-po-piksel, imena likova i zgrada, priču, glazbu, logo ni naziv „Whiteout Survival”. Sve to mora biti naše ili licencirano (Asset Store, vlastiti modeli).

## Kako radimo bez Unity editora
- Sve (svijet, kamera, efekti, UI, pravila) radi se **iz koda** (`unity/Assets/Scripts`).
- `unity/Assets/Editor/BuildScript.cs` u CI-ju generira scenu i materijale, podešava iOS/Android i gradi.
- `.github/workflows/unity-build.yml`: Android APK (Linux) + iOS Xcode projekt (Linux) → nepotpisana IPA (macOS) → pre-release **`unity-latest`**.
- Novi `.cs` fajlovi dobivaju `.meta` preko `python3 scripts/gen-unity-meta.py`.
- Editor na računalu treba samo za stvari koje se ne mogu kodom (uvoz kupljenih 3D modela iz Asset Storea).

## Faze
| Faza | Sadržaj | Stanje |
|---|---|---|
| 0 | Unity projekt, CI build iOS + Android, 3D zaleđeno naselje (placeholder), kamera, snijeg, dim, preživjeli | **u izradi** |
| 1 | Prijenos pravila iz Swift `Core/` u C# + testovi: resursi (meso, drvo, ugljen, željezo), peć i temperatura, preživjeli (glad, hladnoća, bolest), gradnja/nadogradnja zgrada s timerima, save/offline napredak | |
| 2 | Pravi UI (uGUI): gornja traka resursa, izbornik gradnje, info zgrade, ubrzanja, zadaci/ciljevi, događaji | |
| 3 | Istraživanje (tech stablo), vojska (trening jedinica), bolnica | |
| 4 | Heroji: regrutacija, razine, oprema, vještine | |
| 5 | Karta svijeta (offline): zvijeri, resursne točke, istraživanje (PvE borbe) | |
| 6 | Grafika i zvuk: modeli s Asset Storea, animacije, efekti, glazba | |
| 7 | Online (server: npr. Unity Gaming Services ili Nakama): računi, savezi, chat, PvP, eventi, ljestvice | |

Online faza (7) traži server koji se plaća kad raste broj igrača; faze 0–6 rade potpuno offline.
