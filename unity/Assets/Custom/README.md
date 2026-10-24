# Vlastiti modeli i slike (Custom assets)

Ovdje ubacuješ **svoje** 3D modele, likove, animacije i slike. Igra ih sama pronađe po **imenu datoteke** – ne treba mijenjati nijednu liniju koda.

- Datoteku staviš u pravu mapu, s točnim imenom → igra koristi tvoju datoteku umjesto svoje.
- Ako u mapi nema ničega (samo ovaj README i `.gitkeep`), igra izgleda **točno kao prije**.
- Ako nešto ne valja (krivo ime, krivi format), igra to preskoči i koristi svoje – neće se srušiti.

*(English summary is further down.)*

---

## 1. Gdje idu datoteke

Sve ide u mapu `unity/Assets/Custom/Resources/Custom/` (da, „Custom” dvaput – tako Unity može učitati datoteke dok igra radi):

```
unity/Assets/Custom/
├── README.md                ← ovaj tekst
└── Resources/Custom/
    ├── Buildings/           ← modeli zgrada          (npr. longhouse.fbx, longhouse@L10.fbx)
    ├── Decorations/         ← modeli ukrasa          (npr. well.fbx, totem.fbx)
    ├── People/              ← likovi (seljani)       (npr. Woodcutter_Ivan_Male.fbx)
    │   └── Animations/      ← animacije za sve likove (npr. Idle.fbx, Walk.fbx, Chop.fbx)
    ├── Icons/               ← slike zgrada u izborniku gradnje (npr. longhouse.png, deco_well.png)
    ├── UI/
    │   ├── Icons/           ← ikone sučelja          (npr. wood.png, stone.png, food.png)
    │   ├── Skin/            ← okviri, gumbi, trake   (npr. btn_green.png, panel.png)
    │   └── Images/          ← velike slike           (npr. logo_mark.png, loading_bg.png)
    ├── Textures/
    │   └── Terrain/         ← tlo                    (npr. Grass.png, Grass_normal.png)
    └── Map/                 ← TVOJA MAPA doline      (Map.png, po želji Name.txt)
```

## 2. Pravila za imena (najvažnije!)

1. **Ime datoteke = ključ iz tablica na dnu**, točno tako napisano: mala slova, podvlake `_`, bez razmaka. Primjer: `medieval_house.fbx`, a ne `Medieval House.fbx`.
2. Nastavak (ekstenzija): modeli **`.fbx`** (najbolje), `.obj` ili Unity `.prefab`; slike **`.png`** (može i `.jpg`, `.tga`, `.psd`).
3. `.blend`, `.gltf`, `.glb`, `.zip` **ne rade** – iz Blendera napravi *File → Export → FBX*.
4. Krivo ime se jednostavno ne koristi. Provjera (poglavlje 9) ti javi „UNKNOWN … did you mean …?” za tipfelere.

### Različit izgled po razini zgrade

Dodaj `@L` i broj razine na kraj imena:

| Datoteka | Kada se vidi |
|---|---|
| `longhouse.fbx` | razine 1–9 |
| `longhouse@L10.fbx` | razine 10–19 |
| `longhouse@L20.fbx` | od razine 20 nadalje |

Igra uzme varijantu s **najvećim brojem koji nije veći od trenutne razine**. Ako postoji samo `longhouse@L10.fbx`, razine 1–9 koriste ugrađeni model igre.

Napomena: neke zgrade same mijenjaju ključ s razinom (npr. *Shelter* od razine 15 izgleda kao `medieval_cottage`). Tablica zgrada pokazuje koji ključ se koristi od koje razine.

## 3. Zgrade (`Buildings/`)

- **Veličina se namješta sama**: igra tvoj model poveća/smanji tako da tlocrt stane na parcelu (94 % širine), postavi ga na tlo (najniža točka = tlo) i u sredinu parcele. Visina se mijenja u istom omjeru.
- Zato je svejedno je li model u metrima ili centimetrima – ali **proporcije** moraju biti dobre.
- **Prednja strana (vrata)**: ona koju vidiš u Blenderu u pogledu sprijeda (*Numpad 1*), tj. Unity os **+Z**. Igra je okrene prema ulici i kameri.
- Teksture: u FBX-u uključi teksture (*Blender: Export FBX → Path Mode: Copy* i uključi gumb pored – *Embed Textures*) ili stavi slike teksture u istu mapu s istim imenima kao u modelu.
- Materijali se automatski pretvore u URP/Lit (igra koristi URP). Stari „Standard” materijali iz Asset Storea se pretvaraju sami, da ništa ne bude ružičasto.
- Igra ne dodaje sanduke, baklje i zastave oko tvojih zgrada – vidi se samo ono što je u tvojoj datoteci.
- Preporuka za mobitel: do ~20 000 trokuta po zgradi, teksture do 2048 px.

## 4. Ukrasi (`Decorations/`)

- Model ostaje u **stvarnoj veličini** (1 jedinica = 1 metar); samo ako je širi od polja (2 m) igra ga smanji.
- Postavi se na sredinu polja, na tlo; okreće se kad ga igrač okrene.
- **Ograde i zidovi** (`wattle_fence`, `palisade`, `stone_wall`): s tvojim modelom se dijelovi više ne spajaju sami – svaki komad stoji na svom polju. Bez tvog modela ostaju ugrađeni koji se spajaju.
- **Most** (`wooden_bridge`): ishodište modela (0,0,0) je površina vode, most ide duž osi Z i rastegne se na jedno polje (2 m).
- **Ceste** (`dirt_path`, `stone_road`, `cobble_road`) se crtaju po tlu i ne mogu se zamijeniti modelom (sliku u izborniku možeš).

## 5. Ljudi (`People/`) – likovi i animacije

### Što treba

- Lik mora imati **kostur (rig) tipa Humanoid**. Najlakše: **[Mixamo](https://www.mixamo.com)** (besplatno, Adobe račun). Svaki Mixamo lik radi.
- Igra lik automatski uveze kao *Humanoid*, izmjeri ga i postavi na visinu odrasle osobe (1,6–1,8 m; djeca 1,0–1,15 m).
- Lik gleda prema naprijed = Unity +Z (Mixamo likovi su već tako okrenuti; u Blenderu: lice prema pogledu sprijeda, *Numpad 1*).

### Korak po korak (Mixamo)

1. Na Mixamu odaberi ili uploadaj lik → **Download** → Format **FBX for Unity (.fbx)**, Pose **T-pose** → spremi kao npr. `People/Farmer_Ana_Female.fbx`.
2. Animacije (nije obavezno, vidi dolje): odaberi animaciju (npr. *Walking*), uključi **In Place** ako postoji → **Download** → **FBX for Unity**, **Without Skin** → spremi u `People/Animations/` i **preimenuj datoteku u ime iz tablice** (npr. `Walk.fbx`).

### Animacije

**Ako ne dodaš nijednu animaciju**, igra pokuša svoje ugrađene animacije prenijeti na tvoj Humanoid lik (Idle, Walk, Chop…). Provjera (poglavlje 9) javlja je li to uspjelo. Ako nije, dodaj barem `Idle.fbx` i `Walk.fbx` u `People/Animations/`.

Redoslijed: animacije **unutar datoteke lika** (ako se tako zovu) → `People/Animations/<Ime>.fbx` (vrijede za sve likove) → ugrađene animacije igre.

| Ime animacije (datoteke) | Kada se koristi | Ako ne postoji, igra koristi |
|---|---|---|
| `Idle` | stoji i čeka – **obavezno** | — |
| `Walk` | hoda – **obavezno** | — |
| `Jog` | žuri | Walk |
| `Carry` | nosi robu kući | Walk |
| `Talk` | razgovara | Idle |
| `Watch` | promatra (prekrižene ruke) | Idle |
| `Gather` | skuplja s tla / bere | Work |
| `Work` | radi (čekić, kleči) | Idle |
| `Chop` | siječe drvo, kopa kamen | Work |
| `Plant`, `Harvest`, `Water` | sadi, žanje, zalijeva | Gather |
| `Throw` | baca koplje (lov, ribolov) | Work |
| `Sit` | sjedi na panju | Idle |
| `Lie` | bolestan leži | Sit |
| `Torch`, `Lantern` | drži baklju / lampu | Idle |
| `Eat` | jede | Idle |
| `Push` | gura | Work |

Igra prepoznaje i uobičajena Mixamo imena: `Walking` = Walk, `Running` = Jog, `Sitting Idle` = Sit, `Talking` = Talk, `Picking Up` = Gather… Sve animacije se vrte u krug, a lik ostaje na mjestu (sam hoda po svijetu).

### Tko dobiva koji lik

Čim postoji **barem jedan ispravan lik** (s Idle i Walk), **svi** seljani koriste tvoje likove. Bira se po imenu datoteke:

- **Posao** – prva riječ imena prije `_`: `Forager_`, `Woodcutter_`, `Stonecutter_`, `Crafter_`, `Builder_`, `Scholar_`, `Farmer_`, `Fisher_`, `Hunter_`, `Miner_`, `Herder_`, `Healer_`. Npr. `Woodcutter_Marko_Male.fbx` dobivaju drvosječe.
- `Child_…` – za djecu; `Elder_…` – za starce.
- **Spol** – riječ `Female` ili `Male` u imenu, odvojena s `_` (npr. `…_Female.fbx`). Bez toga lik je za oba spola.
- Bez prefiksa posla (npr. `Ana_Female.fbx`) lik može dobiti bilo tko. Ako za neki posao nema lika, uzme se bilo koji.

Ograničenja: tvoji likovi ne drže alat u ruci (sjekiru, motiku); trupci i košare koje nose se i dalje vide.

## 6. Slike zgrada (`Icons/`)

- Zgrada: `Icons/<ključ zgrade>.png` (npr. `Icons/longhouse.png`). Ukras: `Icons/deco_<ključ>.png` (npr. `Icons/deco_well.png`).
- Kvadratna slika, prozirna pozadina, **256×256 px** (ili 512×512).
- Ako zamijeniš model, a ne dodaš sliku, igra sama „fotografira” tvoj model.

## 7. Sučelje (`UI/`)

- **Ikone** `UI/Icons/<ime>.png`: kvadratne, prozirne, 128×128 px. Imena su u tablici na dnu (npr. `wood`, `stone`, `food`, `settings`).
- **Okviri i gumbi** `UI/Skin/<ime>.png`: najlakše je preuzeti originalnu sliku iz `unity/Assets/Art/UI/Resources/UI/Skin/` i prebojati je. Kutovi (rubovi iz tablice) se ne rastežu. Može i veća slika (npr. 2×) ako su **proporcije iste**.
- **Velike slike** `UI/Images/<ime>.png`: `logo_mark` (logo), `loading_bg` (pozadina učitavanja), `glow`.
- Font se (za sada) ne može zamijeniti.

## 7a. Adventure UI Kit (Unity Asset Store)

Igra sama stavlja **Adventure UI Kit** (Smashy Tech) na sučelje: okvire prozora, gumbe, kartice, trake, utore i male ikone (zatvori, natrag, postavke, plus…). Ikone resursa, zgrada i ljudi ostaju naše, oslikane.

1. U Unityju: *Window → Package Manager → My Assets → Adventure UI Kit → Download → Import* (sve).
2. Izbornik **FOUNDERS → Adventure UI Kit → Apply**. Igra pronađe mapu kita (ime sadrži „Adventure”), odabere slike po imenima datoteka i javi što je uzela. Izbor je zapisan u `Assets/Custom/AdventureUiKit.txt` – tu možeš promijeniti bilo koju sliku (ili napisati `none` da ostane naša) i ponovno kliknuti *Apply*.
3. Ono što kit nema ostaje u izgledu igre.
4. Builds na GitHubu ovo rade same prije svakog builda – dovoljno je da je kit u repozitoriju.

> ⚠️ Kit je pod licencom Asset Storea: **ne smije biti u ovom javnom repozitoriju** (mapa `unity/Assets/Adventure UI Kit/` je u `.gitignore`). Za buildove na GitHubu kit je u zasebnom **privatnom** repozitoriju `mmzlopasa/iosgame-uikit` (mapa `Kit/Adventure UI Kit/`), a workflowi ga preuzimaju sa secretom `KIT_TOKEN`. Bez tog secreta igra se gradi u svom izgledu.

*English:* import the kit anywhere under `Assets` (folder name containing "Adventure"); `FoundersEditor.AdventureUiKit` (menu **FOUNDERS → Adventure UI Kit → Apply**, and automatically in `BuildScript.Generate`) maps its sprites to the game's skins and interface glyphs by file/sprite name, copies them to `Assets/Generated/Resources/Kit/UI/{Skin,Icons}` with the kit's own 9-slice borders (`slices.txt`), and writes the choices to `Assets/Custom/AdventureUiKit.txt` for correction. Lookup order at runtime: Custom → Kit → the game's own.

## 8. Tlo (`Textures/Terrain/`)

`Grass.png`, `Mud.png`, `Sand.png`, `RockGround.png` – bešavna (seamless) tekstura koja se ponavlja svakih 5 m, npr. 1024×1024 px. (Tlo se crta potpuno mat, bez sjaja – normal mape poput `Grass_normal.png` se više ne koriste.)

Normal mape se prepoznaju po kraju imena: `_normal`, `_n`, `_nrm`, `_norm`. Isto vrijedi za teksture modela; `_metallic`, `_roughness`, `_ao`, `_mask` se uvoze kao podatkovne (ne-boja) teksture.

## 8a. Tvoja mapa (`Map/`)

Generatora mapa više nema: nova igra počinje na **tvojoj** dolini. Mapu nacrtaš kao sliku u bilo kojem programu za crtanje (Paint, Photoshop, Krita…):

- Ime: **`Map/Map.png`**. Slika je pogled odozgo; **gornji rub slike = sjever**.
- Najbolje **128×128 piksela**: svaki piksel je jedno polje igre (2 × 2 m), cijela dolina je 256 × 256 m. Veća ili manja slika se sama preračuna na 128×128.
- Boje (ne moraju biti savršeno točne, igra uzme najbližu):

| Što | Boja | RGB | Hex |
|---|---|---|---|
| Livada (gradi se tu) | svijetlozelena | 110, 170, 70 | `#6EAA46` |
| Šuma (drvo) | tamnozelena | 30, 90, 40 | `#1E5A28` |
| Voda (rijeka, jezero) | plava | 40, 110, 220 | `#286EDC` |
| Pijesak (obala) | žuta | 235, 215, 130 | `#EBD782` |
| Kameno brdo (kamen) | siva | 150, 150, 150 | `#969696` |
| Planina (ne gradi se) | tamnosiva-smeđa | 80, 70, 65 | `#504641` |
| Ruda (u podnožju planine) | hrđasto narančasta | 190, 90, 40 | `#BE5A28` |
| Mjesto za logorsku vatru (prijedlog) | ljubičasta | 255, 0, 255 | `#FF00FF` |

- Ljubičasti piksel je samo **prijedlog** gdje kamera počinje; igrač i dalje sam bira gdje pali vatru. Ako ga nema (ili je na vodi), igra sama nađe dobru čistinu.
- Ime doline po želji: `Map/Name.txt` s jednim redom teksta (npr. `Green Hollow`).
- Umjesto slike može i `Map/Map.txt`: 128 redova slova (`.` livada, `T` šuma, `~` voda, `,` pijesak, `R` kameno brdo, `M` planina, `O` ruda, `*` vatra).
- Zemlje za istraživanje (64 komada 16×16 polja) igra sama izreže i imenuje iz tvoje mape.
- **Stare igre ostaju na svojoj dolini** (stara dolina i generirane doline se i dalje učitavaju). Tvoja mapa vrijedi za **novu igru**. Ako kasnije promijeniš mapu, igra koja je već na njoj se učita na novoj verziji – ako je neka zgrada sad u vodi, igra pokrene novi kamp i sačuva kopiju.
- Bez `Map.png` igra koristi svoju dolinu (Long Valley).
- Sada je ovdje već dolina **Green Hollow** (nacrtana skriptom `tools/map/foundation_valley.py`, u stilu Foundationa). Svoju mapu samo stavi preko nje (isto ime `Map.png`).

## 9. Provjera

- **U Unity editoru:** izbornik **FOUNDERS → Check custom assets**. U Consoleu piše što se koristi (`OK`), što ne (`UNKNOWN … did you mean …?`) i upozorenja (`WARNING`: krive jedinice, previše trokuta, nedostaju animacije, ružičasti materijali).
- **Bez Unityja:** isti izvještaj je u logu svakog builda na GitHubu: *Actions → Unity build (iOS) → posao builda → korak buildanja*, potraži `Custom assets check`.
- Iz komandne linije: `Unity -batchmode -projectPath unity -executeMethod FoundersEditor.CustomAssetsEditor.CheckBatch -quit -logFile -`.

## 10. Kako to dobiti na mobitel

1. Na GitHubu otvori repozitorij → uđi u mapu, npr. `unity/Assets/Custom/Resources/Custom/Buildings`.
2. **Add file → Upload files** → povuci datoteke → **Commit changes** (na granu `main`).
3. Svaki push koji mijenja nešto u `unity/` pokreće build (*Actions → Unity build (iOS)*). Pričekaj zelenu kvačicu.
4. Novi `Founders.ipa` je u *Releases → unity-latest*; instaliraj ga kao i prije (Feather / Sideloadly, vidi glavni README).

**Ograničenja veličine:** upload preko web stranice GitHuba – najviše **25 MB po datoteci** (Git općenito odbija datoteke veće od 100 MB). Smanji teksture (2048 px je dovoljno) ili koristi git na računalu.

> ⚠️ **LICENCA – PAŽNJA!** Ovaj repozitorij je **JAVAN** – svatko na internetu može preuzeti sve datoteke iz njega.
> Stavljaj ovdje samo ono što **smiješ objaviti**: svoje radove, CC0 ili slobodne licence koje dopuštaju dijeljenje.
> **Kupljeni modeli (Unity Asset Store, Sketchfab, CGTrader, Fab…) gotovo nikad ne smiju biti javno objavljeni u izvornom obliku** – to krši licencu.
> Prije uploada kupljenih datoteka **prebaci repozitorij u privatni**: *Settings → General → Danger Zone → Change repository visibility → Make private*. (Provjeri da GitHub Actions i dalje imaju dovoljno minuta za privatni repozitorij.)
> Isto vrijedi i za Mixamo: likovi i animacije smiju se koristiti u igri, ali ne i dijeliti kao zasebne datoteke.

---

## English summary

Drop your own art into `unity/Assets/Custom/Resources/Custom/` and the game uses it instead of its own, by **file name**, with no code changes. With the folder empty the game is unchanged; a wrong name is simply ignored.

| Folder | File name | What it does |
|---|---|---|
| `Buildings/` | `<key>.fbx` / `.obj` / `.prefab`, optional `<key>@L10.fbx` | Replaces a building model. The variant with the highest `@L` not above the building's level wins. Auto-fitted to the plot (94 %), centred, standing on the ground, front (+Z) turned to the street. No crates/banners are added. |
| `Decorations/` | `<key>.fbx` | Replaces a decoration. Real size (1 unit = 1 m), shrunk only if wider than the 2 m cell. Fences no longer join with a custom model; roads can't be replaced; bridge origin = water surface, runs along Z. |
| `People/` | `[Role_]Name[_Female/_Male].fbx` | Rigged **Humanoid** characters (Mixamo works). Once one valid character exists, all villagers use custom characters, picked by job prefix (`Forager Woodcutter Stonecutter Crafter Builder Scholar Farmer Fisher Hunter Miner Herder Healer`, `Child`, `Elder`), then gender, else anyone. Scaled to 1.6–1.8 m (children 1.0–1.15 m). No hand tools. |
| `People/Animations/` | `<Clip>.fbx` (one clip per file, the file name is the clip name) | Clips for every custom person: `Idle` and `Walk` required; `Jog Carry Talk Watch Gather Work Chop Plant Harvest Water Throw Sit Lie Torch Lantern Eat Push` optional with fallbacks (see table above). Mixamo names (`Walking`, `Running`, `Sitting Idle`…) are understood. Mixamo: "FBX for Unity", "Without Skin", "In Place". Clips inside the character's own file win; without any, the game's own clips are retargeted to humanoid characters. |
| `Icons/` | `<building key>.png`, `deco_<decoration key>.png` | Build-menu/card picture (256×256, transparent). Without one, a replaced model is photographed by the game. |
| `UI/Icons/` | `<icon name>.png` | UI icons (128×128). Names below. |
| `UI/Skin/` | `<skin name>.png` | 9-slice frames/buttons; borders below. Any resolution with the same proportions as the game's picture. |
| `UI/Images/` | `logo_mark`, `loading_bg`, `glow` | Large pictures. |
| `Textures/Terrain/` | `Grass Mud Sand RockGround` (+ `_normal`) | Seamless ground textures, tiled every 5 m. (The game draws the ground matte: normal maps are ignored.) |
| `Map/` | `Map.png` (or `Map.txt`), optional `Name.txt` | Your own valley: top-down picture, top = north, one pixel per 2 m cell, resampled to 128×128; nearest colour of meadow `#6EAA46`, forest `#1E5A28`, water `#286EDC`, sand `#EBD782`, stony hill `#969696`, mountain `#504641`, ore `#BE5A28`, suggested campfire `#FF00FF`. New settlements start on it (`own_valley`); old saves keep their valley (Long Valley or a generated `gen_<code>`). Lands to explore are cut 16×16 from it (`Core/OwnMap.cs`). |

**How it works:** runtime lookups through `Resources` (`unity/Assets/Scripts/CustomAssets.cs`): `BuildingModels.Create/Decoration`, `Villager.Spawn`, `Thumbnails.Get`, `Theme.Icon/SkinTexture/Image`, `WorldTerrain`. Characters are animated with the Playables API on their Animator (no controller asset), root motion off. Non-URP materials (Standard, Legacy…) are converted to URP/Lit at build time (`.mat` assets) and at runtime (anything left), keeping colour, albedo, normal, metallic, occlusion, emission and cutout/transparency.

**Import automation** (`unity/Assets/Editor/CustomAssetsEditor.cs`): models keep their file units, bake axis conversion, read/write off, no colliders/cameras/lights, materials from the file (URP/Lit, embedded textures). `People/**` → Humanoid avatar from the model, every clip looping, travel dropped (in place), Animations-folder clips renamed to their file name. Textures: UI pictures uncompressed/clamped, building pictures with mipmaps, 3D textures ASTC (≤ 2048), normal maps by suffix (`_n _normal _nrm _norm`), data maps (`_metallic _roughness _ao _mask _orm`) linear.

**Check:** menu **FOUNDERS → Check custom assets**, batch `-executeMethod FoundersEditor.CustomAssetsEditor.CheckBatch`, and every CI build logs the same report (`Custom assets check`): used overrides, unknown names with "did you mean", unit/size/triangle warnings, missing animations, non-URP materials.

**Getting it onto the phone:** GitHub → folder → *Add file → Upload files* → commit to `main`; the push builds the game (*Actions → Unity build (iOS)*), the IPA lands in *Releases → unity-latest*. Web uploads are limited to **25 MB per file**. **The repository is PUBLIC: only commit art you have the right to publish. Purchased assets (Asset Store, Sketchfab, Fab, …) and Mixamo files almost always may not be redistributed – make the repository private first.**

**Keep the tables current:** `python3 tools/custom/list_keys.py` (`--check` to verify).

---

## Tablice valjanih imena / Valid names

<!-- KEYS:BEGIN (tools/custom/list_keys.py) -->
### Zgrade / Buildings – `Buildings/<ključ>.fbx`

| Ključ (ime datoteke) / Key (file name) | Koristi se za / Used for | Parcela / Plot (cells, metres) |
|---|---|---|
| `abbey` | Abbey (`abbey`) | 4×4 (8×8 m) |
| `ancestor_shrine` | Ancestor Stones (`ancestor_shrine`) | 2×2 (4×4 m) |
| `animal_pen` | Animal Pen (`animal_pen`) | 1×1 (2×2 m) |
| `apiary` | Apiary (`apiary`) | 1×1 (2×2 m) |
| `bronze_workshop` | Bronze Workshop (`bronze_workshop`) | 1×1 (2×2 m) |
| `campfire` | Campfire (`campfire`) | 1×1 (2×2 m) |
| `campfire_ring` | Campfire (`campfire`) od razine / from level 5 – „Stone Campfire” | 1×1 (2×2 m) |
| `community_circle` | Community Circle (`community_circle`) | 2×2 (4×4 m) |
| `copper_smelter` | Copper Smelter (`copper_smelter`) | 1×1 (2×2 m) |
| `daub_house` | Shelter (`shelter`) od razine / from level 10 – „Cottage”<br>Woven Shelter (`woven_shelter`) od razine / from level 10 – „Cottage”<br>Daub House (`daub_house`) | 1×1 (2×2 m)<br>1×1 (2×2 m)<br>1×1 (2×2 m) |
| `drying_rack` | Drying Rack (`drying_rack`) | 1×1 (2×2 m) |
| `elders_lodge` | Elders' Lodge (`elders_lodge`) | 1×1 (2×2 m) |
| `farm_field` | Farm Field (`farm_field`) | 2×2 (4×4 m) |
| `fishing_weir` | Fishing Weir (`fishing_weir`) | 1×1 (2×2 m) |
| `foraging_camp` | Foraging Camp (`foraging_camp`) | 1×1 (2×2 m) |
| `granary` | Granary (`granary`) | 1×1 (2×2 m) |
| `healers_hut` | Healer's Hut (`healers_hut`) | 1×1 (2×2 m) |
| `hearth_great` | Village Hearth (`village_hearth`) od razine / from level 25 – „Great Hearth” | 1×1 (2×2 m) |
| `hearth_pavilion` | Village Hearth (`village_hearth`) od razine / from level 15 – „Hearth Pavilion” | 1×1 (2×2 m) |
| `herb_garden` | Herb Garden (`herb_garden`) | 1×1 (2×2 m) |
| `hunters_lodge` | Hunter's Lodge (`hunters_lodge`) | 1×1 (2×2 m) |
| `longhouse` | Longhouse (`longhouse`) | 2×2 (4×4 m) |
| `lookout_tower` | Lookout Tower (`lookout_tower`) | 1×1 (2×2 m) |
| `lords_manor` | Lord's Manor (`lords_manor`) | 4×4 (8×8 m) |
| `market` | Market (`market`) | 3×2 (6×4 m) |
| `market_stall` | Market Stall (`market_stall`) | 1×1 (2×2 m) |
| `medieval_bakery` | Bakery (`medieval_bakery`) | 2×2 (4×4 m) |
| `medieval_barn` | Barn and Paddock (`medieval_barn`) | 3×3 (6×6 m) |
| `medieval_bathhouse` | Bathhouse (`medieval_bathhouse`) | 2×2 (4×4 m) |
| `medieval_carpenter` | Carpenter's Yard (`medieval_carpenter`) | 2×2 (4×4 m) |
| `medieval_church` | Church (`medieval_church`) | 3×6 (6×12 m) |
| `medieval_cottage` | Shelter (`shelter`) od razine / from level 15 – „Timber House”<br>Woven Shelter (`woven_shelter`) od razine / from level 15 – „Timber House”<br>Daub House (`daub_house`) od razine / from level 15 – „Timber House”<br>Stone Cottage (`medieval_cottage`) | 1×1 (2×2 m)<br>1×1 (2×2 m)<br>1×1 (2×2 m)<br>2×2 (4×4 m) |
| `medieval_house` | Shelter (`shelter`) od razine / from level 20 – „Two-Storey House”<br>Woven Shelter (`woven_shelter`) od razine / from level 20 – „Two-Storey House”<br>Daub House (`daub_house`) od razine / from level 20 – „Two-Storey House”<br>Stone Cottage (`medieval_cottage`) od razine / from level 10 – „Townhouse”<br>Town House (`medieval_house`) | 1×1 (2×2 m)<br>1×1 (2×2 m)<br>1×1 (2×2 m)<br>2×2 (4×4 m)<br>2×2 (4×4 m) |
| `medieval_market` | Market Hall (`medieval_market`) | 3×3 (6×6 m) |
| `medieval_mill` | Watermill (`medieval_mill`) | 2×2 (4×4 m) |
| `medieval_scriptorium` | Scriptorium (`medieval_scriptorium`) | 2×2 (4×4 m) |
| `medieval_smithy` | Smithy (`medieval_smithy`) | 2×3 (4×6 m) |
| `medieval_tavern` | Tavern (`medieval_tavern`) | 2×2 (4×4 m) |
| `medieval_townhouse` | Longhouse (`longhouse`) od razine / from level 15 – „Great Longhouse”<br>Tall Townhouse (`medieval_townhouse`) | 2×2 (4×4 m)<br>2×3 (4×6 m) |
| `medieval_warehouse` | Warehouse (`medieval_warehouse`) | 3×3 (6×6 m) |
| `mine` | Mine (`mine`) | 1×1 (2×2 m) |
| `pasture` | Pasture (`pasture`) | 2×2 (4×4 m) |
| `pottery_kiln` | Pottery Kiln (`pottery_kiln`) | 1×1 (2×2 m) |
| `sawpit` | Sawpit (`sawpit`) | 1×1 (2×2 m) |
| `shelter` | Shelter (`shelter`) | 1×1 (2×2 m) |
| `smokehouse` | Smokehouse (`smokehouse`) | 1×1 (2×2 m) |
| `stone_keep` | Stone Keep (`stone_keep`) | 4×4 (8×8 m) |
| `stone_site` | Stone Gathering Site (`stone_site`) | 1×1 (2×2 m) |
| `storage_pit` | Storage Pit (`storage_pit`) | 1×1 (2×2 m) |
| `toolmaker` | Toolmaker's Shelter (`toolmaker`) | 1×1 (2×2 m) |
| `toolsmith` | Toolsmith (`toolsmith`) | 1×1 (2×2 m) |
| `trading_post` | Trading Post (`trading_post`) | 2×2 (4×4 m) |
| `village_hearth` | Village Hearth (`village_hearth`) | 1×1 (2×2 m) |
| `weavers_hut` | Weaver's Hut (`weavers_hut`) | 1×1 (2×2 m) |
| `wood_camp` | Wood Gatherers' Camp (`wood_camp`) | 1×1 (2×2 m) |
| `woven_shelter` | Shelter (`shelter`) od razine / from level 5 – „Hut”<br>Woven Shelter (`woven_shelter`) | 1×1 (2×2 m)<br>1×1 (2×2 m) |

### Ukrasi / Decorations – `Decorations/<ključ>.fbx`

| Ključ / Key | Ime / Name | Vrsta / Category | Napomena / Note |
|---|---|---|---|
| ~~`dirt_path`~~ | Dirt Path | Roads | cesta se crta po tlu – **ne može se zamijeniti** / painted road, not replaceable |
| ~~`stone_road`~~ | Stone Road | Roads | cesta se crta po tlu – **ne može se zamijeniti** / painted road, not replaceable |
| `wooden_bridge` | Wooden Bridge | Roads | most: ishodište modela = površina vode, dug 2 m po osi Z / bridge: origin = water surface, 2 m along Z |
| `wattle_fence` | Wattle Fence | Fences | s modelom se dijelovi ne spajaju; okreće se / pieces no longer join, they turn |
| `palisade` | Palisade | Fences | s modelom se dijelovi ne spajaju; okreće se / pieces no longer join, they turn |
| `stone_wall` | Stone Wall | Fences | s modelom se dijelovi ne spajaju; okreće se / pieces no longer join, they turn |
| `torch_post` | Torch Post | Lights |  |
| `fire_brazier` | Brazier | Lights |  |
| `oil_lantern` | Lantern Post | Lights |  |
| `bush` | Bush | Nature |  |
| `young_tree` | Tree | Nature |  |
| `flower_bed` | Wildflowers | Nature |  |
| `well` | Well | Monuments |  |
| `totem` | Totem | Monuments |  |
| `standing_stone` | Standing Stone | Monuments |  |
| ~~`cobble_road`~~ | Cobbled Street | Roads | cesta se crta po tlu – **ne može se zamijeniti** / painted road, not replaceable |
| `banner` | Town Banner | Monuments |  |

### Slike zgrada / Building pictures – `Icons/<ključ>.png`

Za zgradu: isti ključ kao u tablici zgrada (npr. `Icons/longhouse.png`). For a building: the same key as in the buildings table.

Za ukras / For a decoration: `deco_dirt_path`, `deco_stone_road`, `deco_wooden_bridge`, `deco_wattle_fence`, `deco_palisade`, `deco_stone_wall`, `deco_torch_post`, `deco_fire_brazier`, `deco_oil_lantern`, `deco_bush`, `deco_young_tree`, `deco_flower_bed`, `deco_well`, `deco_totem`, `deco_standing_stone`, `deco_cobble_road`, `deco_banner`

### Ikone sučelja / UI icons – `UI/Icons/<ime>.png` (kvadratne, 128×128 px)

`alert`, `approval`, `arrow`, `autumn`, `axe`, `back`, `bell`, `bronze`, `build`, `campfire`, `check`, `clock`, `close`, `coins`, `compass`, `copy`, `customs`, `discontent`, `eye`, `eye_off`, `face_f1`, `face_f2`, `face_f3`, `face_m1`, `face_m2`, `face_m3`, `fence`, `fish`, `flame`, `food`, `goals`, `goods`, `hides`, `home`, `info`, `knowledge`, `landmark`, `lantern`, `law_apprentices`, `law_bitter_remedies`, `law_burial_rites`, `law_dawn_to_dusk`, `law_day_of_rest`, `law_elders_council`, `law_fire_wardens`, `law_harvest_festival`, `law_hearty_meals`, `law_long_days`, `law_shared_stew`, `law_story_nights`, `law_watch_the_sick`, `law_watchmen`, `legacy`, `level_up`, `lock`, `minus`, `moon`, `next`, `ore`, `people`, `pickaxe`, `planks`, `plus`, `research`, `road`, `rotate`, `settings`, `sick`, `sparkle`, `spear`, `spring`, `stone`, `storage`, `summer`, `thermometer`, `tools`, `trash`, `tree`, `trophy`, `user`, `wheat`, `winter`, `wood`, `world`

### Okviri i gumbi / UI skins (9-slice) – `UI/Skin/<ime>.png`

Rubovi (lijevo, gore, desno, dolje) se ne rastežu; sredina se rasteže (kod `panel`, `plank`, `section` se ponavlja). Druga veličina slike je u redu ako su proporcije iste – rubovi se skaliraju s njom. Borders (left, top, right, bottom) never stretch; the middle does (tiles for panel/plank/section). Another resolution is fine with the same proportions – the borders scale with it.

| Ime / Name | Slika igre / Game picture (px) | Rubovi / Borders L,T,R,B (px) | Sredina / Middle |
|---|---|---|---|
| `panel` | 576×585 | 48, 48, 48, 57 | ponavlja / tiled |
| `plank` | 564×114 | 42, 33, 42, 42 | ponavlja / tiled |
| `section` | 360×360 | 36, 36, 36, 36 | ponavlja / tiled |
| `btn_green` | 144×128 | 33, 30, 33, 39 | rasteže / stretched |
| `btn_blue` | 144×128 | 33, 30, 33, 39 | rasteže / stretched |
| `btn_orange` | 144×128 | 33, 30, 33, 39 | rasteže / stretched |
| `btn_grey` | 144×128 | 33, 30, 33, 39 | rasteže / stretched |
| `btn_red` | 144×128 | 33, 30, 33, 39 | rasteže / stretched |
| `btn_brown` | 144×128 | 33, 30, 33, 39 | rasteže / stretched |
| `tab_on` | 144×104 | 33, 30, 33, 36 | rasteže / stretched |
| `tab_off` | 144×100 | 33, 30, 33, 33 | rasteže / stretched |
| `slot` | 144×152 | 36, 36, 36, 42 | rasteže / stretched |
| `tile` | 144×152 | 39, 39, 39, 48 | rasteže / stretched |
| `cell_common` | 144×152 | 39, 39, 39, 48 | rasteže / stretched |
| `cell_uncommon` | 144×152 | 39, 39, 39, 48 | rasteže / stretched |
| `cell_rare` | 144×152 | 39, 39, 39, 48 | rasteže / stretched |
| `cell_epic` | 144×152 | 39, 39, 39, 48 | rasteže / stretched |
| `cell_legendary` | 144×152 | 39, 39, 39, 48 | rasteže / stretched |
| `capsule` | 120×78 | 36, 36, 36, 36 | rasteže / stretched |
| `bar_track` | 90×42 | 21, 21, 21, 21 | rasteže / stretched |
| `fill_green` | 90×42 | 21, 21, 21, 21 | rasteže / stretched |
| `fill_gold` | 90×42 | 21, 21, 21, 21 | rasteže / stretched |
| `fill_blue` | 90×42 | 21, 21, 21, 21 | rasteže / stretched |
| `fill_red` | 90×42 | 21, 21, 21, 21 | rasteže / stretched |
| `badge_green` | 60×64 | 21, 21, 21, 24 | rasteže / stretched |
| `badge_red` | 60×64 | 27, 27, 27, 30 | rasteže / stretched |
| `hud_capsule` | 180×78 | 39, 36, 39, 36 | rasteže / stretched |
| `hud_plus` | 66×70 | 30, 30, 30, 33 | rasteže / stretched |
| `hud_hex` | 144×146 | 66, 63, 66, 72 | rasteže / stretched |
| `hud_hex_frame` | 192×216 | 90, 99, 90, 105 | rasteže / stretched |
| `hud_round_blue` | 72×76 | 33, 33, 33, 36 | rasteže / stretched |
| `hud_round_yellow` | 72×76 | 33, 33, 33, 36 | rasteže / stretched |
| `hud_square_blue` | 120×128 | 36, 33, 36, 42 | rasteže / stretched |
| `hud_bar_track` | 270×72 | 78, 24, 24, 24 | rasteže / stretched |
| `hud_bar_fill_blue` | 90×42 | 21, 21, 21, 21 | rasteže / stretched |
| `hud_rank` | 120×48 | 24, 24, 24, 24 | rasteže / stretched |

### Velike slike / Images – `UI/Images/<ime>.png`

`glow` (360×120 px), `loading_bg` (1170×2532 px), `logo_mark` (512×512 px)

### Tlo / Ground – `Textures/Terrain/<ime>.png` (+ `<ime>_normal.png`)

`Grass`, `Mud`, `Sand`, `RockGround` – tekstura se ponavlja svakih 5 m / tiles every 5 m.

<!-- KEYS:END -->
