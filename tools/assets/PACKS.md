# CC0 building packs for later eras

| Era | Pack | Licence | Notes |
|---|---|---|---|
| Medieval | Quaternius "Medieval Village MegaKit" (Standard), [OpenGameArt](https://opengameart.org/content/medieval-village-megakit) | CC0 | Modular walls, roofs, windows and doors with PBR textures. `blender_medieval.py` assembles complete houses from them. |
| Modern | Kenney "City Kit (Suburban)" and "City Kit (Commercial)", [kenney.nl](https://kenney.nl/assets/city-kit-suburban) | CC0 | Simple low-poly with flat colours. |
| Future | Quaternius "Ultimate Space Kit", [OpenGameArt](https://opengameart.org/content/ultimate-space-kit-by-quaternius) | CC0 | Simple low-poly domes, habitats and solar panels. |

Previews: `docs/previews/*_pack.jpg`.

Assemble the medieval houses (writes glTF files plus a preview render):

    blender -b --factory-startup -P tools/assets/blender_medieval.py -- <kit>/glTF <out> --preview
