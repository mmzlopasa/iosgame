# CC0 building packs for later eras

| Era | Pack | Licence | Notes |
|---|---|---|---|
| Medieval | Quaternius "Medieval Village MegaKit" (Standard), [OpenGameArt](https://opengameart.org/content/medieval-village-megakit) | CC0 | Modular walls, roofs, windows and doors with PBR textures. `blender_medieval.py` assembles complete houses from them. |
| Nature (now) | Quaternius "Stylized Nature MegaKit" (Standard), [OpenGameArt](https://opengameart.org/content/stylized-nature-megakit) | CC0 | Trees, rocks, bushes, plants. `build_stylized.py`. |
| Props (now) | Kenney "Survival Kit", [kenney.nl](https://kenney.nl/assets/survival-kit) | CC0 | Campfires, tents, tools, resource piles. `build_stylized.py`. |
| People (now) | Quaternius "Universal Base Characters" (Standard), [itch.io](https://quaternius.itch.io/universal-base-characters) | CC0 | Heads, eyes, eyebrows and hairstyles on the Universal Animation Library skeleton. `build_characters.py` (downloads via `itch_fetch.py`). |
| People (now) | Quaternius "Modular Character Outfits - Fantasy" (Standard), [itch.io](https://quaternius.itch.io/modular-character-outfits-fantasy) | CC0 | Peasant and ranger clothes (shirts, jerkins, hoods, trousers, boots) for those bodies; `blender_characters.py` turns the textures into flat-colour zones and adds skirts. |
| Animations (now) | Quaternius "Universal Animation Library" 1 + 2 (Standard), [OpenGameArt](https://opengameart.org/content/universal-animation-library) | CC0 | Walking, work and idle clips for every villager. `build_people.py` (Villager.fbx). |
| Animals (now) | Quaternius "Farm Animals", [OpenGameArt](https://opengameart.org/content/lowpoly-animated-farm-animal-pack) | CC0 | Cow, sheep, pig, horse, llama with animations. `build_animals.py`. |
| Modern | Kenney "City Kit (Suburban)" and "City Kit (Commercial)", [kenney.nl](https://kenney.nl/assets/city-kit-suburban) | CC0 | Simple low-poly with flat colours. |
| Future | Quaternius "Ultimate Space Kit", [OpenGameArt](https://opengameart.org/content/ultimate-space-kit-by-quaternius) | CC0 | Simple low-poly domes, habitats and solar panels. |

Previews: `docs/previews/*_pack.jpg`.

Assemble the medieval houses (writes glTF files plus a preview render):

    blender -b --factory-startup -P tools/assets/blender_medieval.py -- <kit>/glTF <out> --preview
