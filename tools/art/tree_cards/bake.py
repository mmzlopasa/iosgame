# Bakes painted impostors of the kit trees into the layout of the pack's white "Cards.png" mask.
import bpy, sys, json, os, math
from mathutils import Vector, Matrix
args = sys.argv[sys.argv.index('--') + 1:]
specs = json.load(open(args[0])); out_dir = args[1]; only = args[2:] 
os.makedirs(out_dir, exist_ok=True)

def material(spec):
    m = bpy.data.materials.new(spec['name'])
    m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes['Principled BSDF']
    bsdf.inputs['Roughness'].default_value = 1.0
    bsdf.inputs['Specular IOR Level'].default_value = 0.0
    col = nt.nodes.new('ShaderNodeRGB'); col.outputs[0].default_value = (*spec['color'], 1)
    mul = nt.nodes.new('ShaderNodeMix'); mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'; mul.inputs['Factor'].default_value = 1
    nt.links.new(col.outputs[0], mul.inputs['B'])
    if spec['tex']:
        img = bpy.data.images.load(spec['tex'])
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = img
        if 'Palette' in spec['name']: t.interpolation = 'Closest'
        nt.links.new(t.outputs['Color'], mul.inputs['A'])
        if 'Palette' not in spec['name']:
            # Hard cut-out, as in the game (alpha clip).
            gt = nt.nodes.new('ShaderNodeMath'); gt.operation = 'GREATER_THAN'; gt.inputs[1].default_value = spec['cutoff']
            nt.links.new(t.outputs['Alpha'], gt.inputs[0]); nt.links.new(gt.outputs[0], bsdf.inputs['Alpha'])
    else:
        mul.inputs['A'].default_value = (1, 1, 1, 1)
    nt.links.new(mul.outputs['Result'], bsdf.inputs['Base Color'])
    return m

def quads(card):
    bpy.ops.import_scene.fbx(filepath=card)
    res = []
    for o in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
        me = o.data; uv = me.uv_layers.active.data
        for p in me.polygons:
            pts = [(o.matrix_world @ me.vertices[me.loops[li].vertex_index].co, Vector(uv[li].uv)) for li in p.loop_indices]
            res.append(pts)
        bpy.data.objects.remove(o)
    for o in list(bpy.context.scene.objects): bpy.data.objects.remove(o)
    return res

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = False
scene.render.film_transparent = True
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.color_mode = 'RGBA'

result = {}
for spec in specs:
    if only and spec['tree'] not in only: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 48; scene.cycles.device = 'CPU'
    scene.render.film_transparent = True; scene.view_settings.view_transform = 'Standard'
    scene.render.image_settings.color_mode = 'RGBA'
    qs = quads(spec['card'])
    fbx = spec['meshes'][0]['fbx']
    bpy.ops.import_scene.fbx(filepath=fbx)
    wanted = {m['mesh']: m['mats'] for m in spec['meshes']}
    for o in list(scene.objects):
        if o.type == 'MESH' and o.name.split('.')[0] in wanted:
            mats = wanted[o.name.split('.')[0]]
            pal = next((x for x in mats if 'Palette' in x['name']), None)
            other = next((x for x in mats if 'Palette' not in x['name']), None)
            for slot in o.material_slots:
                src = pal if (slot.material and 'Palette' in slot.material.name) or other is None else other
                slot.material = material(src)
        elif o.type == 'MESH':
            bpy.data.objects.remove(o)
    world = bpy.data.worlds.new('w'); scene.world = world; world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.70, 0.72, 0.66, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); scene.collection.objects.link(sun)
    sun.data.energy = 1.9; sun.data.angle = math.radians(8); sun.data.color = (1.0, 0.95, 0.85)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); scene.collection.objects.link(cam); scene.camera = cam
    cam.data.type = 'ORTHO'
    rects = []
    for i, q in enumerate(qs):
        us = [p[1].x for p in q]; vs = [p[1].y for p in q]
        umin, umax, vmin, vmax = min(us), max(us), min(vs), max(vs)
        left = [p[0] for p in q if abs(p[1].x - umin) < 1e-4]; right = [p[0] for p in q if abs(p[1].x - umax) < 1e-4]
        r = (sum(right, Vector()) / len(right)) - (sum(left, Vector()) / len(left))
        width = r.length; r.normalize()
        up = Vector((0, 0, 1)); back = r.cross(up)
        zs = [p[0].z for p in q]; height = max(zs) - min(zs)
        center = sum((p[0] for p in q), Vector()) / 4
        W, H = 4096, 4096
        x0, x1 = round(umin * W), round(umax * W); y0, y1 = round((1 - vmax) * H), round((1 - vmin) * H)
        scene.render.resolution_x, scene.render.resolution_y = x1 - x0, y1 - y0
        cam.data.ortho_scale = max(width, height) if (x1 - x0) / max(1, (y1 - y0)) <= width / height + 1e-6 else max(width, height)
        cam.data.sensor_fit = 'VERTICAL' if height >= width else 'HORIZONTAL'
        cam.data.ortho_scale = height if height >= width else width
        cam.matrix_world = Matrix.Translation(center + back * 60) @ Matrix((r, up, back)).transposed().to_4x4()
        cam.data.clip_end = 200
        # Sun from the front-left, high: the side the camera sees is lit, its back in soft shade.
        sun.matrix_world = (Matrix.Rotation(math.radians(-25), 4, up) @ Matrix((r, up, back)).transposed().to_4x4()) @ Matrix.Rotation(math.radians(-55), 4, 'X')
        path = f"{out_dir}/{spec['tree']}_{i}.png"
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        rects.append([x0, y0, x1, y1, path])
    result[spec['tree']] = rects
    print('BAKED', spec['tree'], rects, flush=True)
json.dump(result, open(f'{out_dir}/rects_{"_".join(only) or "all"}.json', 'w'))
