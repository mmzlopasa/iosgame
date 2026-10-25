# Painted tree cards

Far away, the PRO trees are drawn as "cards": two crossed flat pictures. The pack ships those pictures only as
a white outline (`Textures/Foliage/Cards/Cards.png`), so far woods looked like flat green or navy cut-outs.
These scripts render every PRO tree in Blender, from exactly the two sides its card shows, and put the
renders into the same layout. The result (`Cards_Painted.png`) is made from the licensed packs, so it lives in
the private repository `mmzlopasa/founders-assets` at `Nature/FoundersBaked/Cards_Painted.png`; CI copies it
to `Assets/FoundersBaked/`, where `NatureKitSetup` turns it into the (unlit) card material.

```
python3 resolve.py                                   # -> specs.json (meshes + the materials the game uses)
python runbpy.py bake.py specs.json out [Tree ...]   # Blender (bpy 4.2), Cycles; one PNG per card side
python3 compose.py out <Cards.png> Cards_Painted.png  # the atlas, colour bled outwards for mipmaps
```

`resolve.py` expects the assets repository at `/home/claude/founders-assets`; edit `ROOT` otherwise.
The lighting is painted in (soft high sun from the front, warm sky light), so the cards are unlit in game
and only dim with the evening (`Nature.UpdateCardLight`).
