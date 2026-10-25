# Resolves, for each kit tree that has a card, the LOD0 meshes and the materials the game gives them.
import os, re, json, glob, sys
ROOT = '/home/claude/founders-assets/Nature'
guid2path = {}
for meta in glob.glob(ROOT + '/**/*.meta', recursive=True):
    with open(meta, errors='ignore') as f:
        for line in f:
            if line.startswith('guid:'):
                guid2path[line.split()[1]] = meta[:-5]; break

def blocks(path):
    txt = open(path, errors='ignore').read()
    out = {}
    for m in re.finditer(r'--- !u!(\d+) &(-?\d+)[^\n]*\n(.*?)(?=\n--- !u!|\Z)', txt, re.S):
        out[m.group(2)] = (m.group(1), m.group(3))
    return out

def fbx_names(fbx):
    meta = open(fbx + '.meta', errors='ignore').read()
    names = {}
    for m in re.finditer(r'first:\s*\n\s*\d+:\s*(-?\d+)\s*\n\s*second:\s*(\S+)', meta):
        names[m.group(1)] = m.group(2)
    return names

def tex_of(mat_txt, props):
    for p in props:
        m = re.search(r'- %s:\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]+)' % p, mat_txt)
        if m and m.group(1) in guid2path: return guid2path[m.group(1)]
    return None

def col_of(mat_txt, props):
    for p in props:
        m = re.search(r'- %s: \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}' % p, mat_txt)
        if m: return [float(m.group(i)) for i in range(1, 5)]
    return None

PRO = ROOT + '/Innerverse Games/(UNP) Ultimate Nature PRO/Materials/'
SWAP = {'UNS_Color_Palette': PRO + 'Misc/Color_Palette.mat', 'UNS_Spruce_Tree_Branch': PRO + 'Environment/Foliage/Leaves/Fir_Branch.mat'}
def material(path):
    path = SWAP.get(os.path.basename(path)[:-4], path)
    t = open(path, errors='ignore').read()
    name = os.path.basename(path)[:-4]
    color = col_of(t, ['_BaseColor', '_MainColor', '_Color']) or [1, 1, 1, 1]
    second = col_of(t, ['_SecondColor'])
    low = name.lower()
    if second and any(k in low for k in ['lea', 'branch', 'grass', 'fern', 'reeds', 'clover']):
        mixed = [(color[i] + (second[i] - color[i]) * 0.6) * 1.1 for i in range(3)]
        color = [min(1, mixed[0] * 1.08), min(1, mixed[1] * 1.06), min(1, mixed[2] * 0.75), 1]
    cut = re.search(r'- _(?:AlphaCutoff|Cutoff): ([\d.]+)', t)
    return {'name': name, 'tex': tex_of(t, ['_BaseMap', '_Texture', '_MainTex', '_Albedo', '_AlbedoMap']),
            'color': color[:3], 'cutoff': float(cut.group(1)) if cut else 0.5}

specs = []
for card in sorted(glob.glob(ROOT + '/**/*_Card.fbx', recursive=True)):
    tree = os.path.basename(card)[:-9]
    prefab = glob.glob(ROOT + '/**/Prefabs/**/%s.prefab' % tree, recursive=True)
    if not prefab: print('no prefab', tree); continue
    b = blocks(prefab[0])
    filters, renderers = {}, {}
    for fid, (cls, body) in b.items():
        go = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body)
        if not go: continue
        if cls == '33':
            m = re.search(r'm_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]+)', body)
            if m: filters[go.group(1)] = (m.group(1), m.group(2))
        elif cls == '23':
            renderers[go.group(1)] = re.findall(r'- \{fileID: \d+, guid: ([0-9a-f]+)', body.split('m_Materials:')[1].split('m_StaticBatchInfo')[0])
    gonames = {fid: re.search(r'm_Name: (\S+)', body).group(1) for fid, (cls, body) in b.items() if cls == '1'}
    meshes = []
    for go, (mid, mguid) in filters.items():
        fbx = guid2path.get(mguid)
        if not fbx: continue
        mesh = gonames.get(go)
        if not mesh or 'LOD0' not in mesh: continue
        mats = [material(guid2path[g]) for g in renderers.get(go, []) if g in guid2path]
        meshes.append({'fbx': fbx, 'mesh': mesh, 'mats': mats})
    specs.append({'tree': tree, 'card': card, 'meshes': meshes})
json.dump(specs, open('/tmp/claude-0/work/imp/specs.json', 'w'), indent=1)
for s in specs: print(s['tree'], [(m['mesh'], [(x['name'], os.path.basename(x['tex'] or '-'), [round(c,2) for c in x['color']]) for x in m['mats']]) for m in s['meshes']])
