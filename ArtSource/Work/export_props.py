"""
Builds the objective prop models used by the game from the raw downloads in ArtSource/Downloads.

Run inside Blender 5.x (it clears the open file's scene, so use ArtSource/Work/Props_work.blend):
    blender -b ArtSource/Work/Props_work.blend --python ArtSource/Work/export_props.py
or from the Blender Python console / MCP:  exec(open(r"<project>/ArtSource/Work/export_props.py").read())

For every prop in PROPS it:
  1. imports the source file (glTF/GLB), keeps the listed meshes and bakes their world transforms,
  2. applies the listed edits (rotation, part moves, desk narrowing, decimation),
  3. scales it to its real-world size, puts the origin at the bottom centre, front facing -Y
     (Unity +Z after the FBX axis conversion),
  4. renames the materials to "<Prop>_<Part>",
  5. exports Assets/_Project/Resources/Props/<Prop>.fbx (no embedded textures).
Texture sets (albedo RGBA, OpenGL normal, URP mask R=metallic A=smoothness) are written to
Assets/_Project/Textures/Props/<Set>/ and described in Assets/_Project/Textures/Props/props.json, which the Unity
editor script PropAssetPipeline turns into URP Lit materials.
"""
import json
import math
import os

import bmesh
import bpy
import mathutils
import numpy as np

ROOT = os.path.normpath(os.path.join(os.path.dirname(bpy.data.filepath), "..", ".."))
DL = os.path.join(ROOT, "ArtSource", "Downloads")
OUT_FBX = os.path.join(ROOT, "Assets", "_Project", "Resources", "Props")
OUT_TEX = os.path.join(ROOT, "Assets", "_Project", "Textures", "Props")

SF = os.path.join(DL, "SF_Props")
PH = os.path.join(DL, "PolyHavenModels")

# glow = emission strength (emission map = albedo) so pickups read in the dark like the old greybox items.
# size = (axis, metres): the prop is scaled uniformly so that this bounding-box axis has that length.
PROPS = [
    dict(name="Safe", src=os.path.join(SF, "antique_iron_safe.glb"), size=("z", 0.85), tex=1024),
    dict(name="Gramophone", src=os.path.join(SF, "vintage_gramophone.glb"), size=("z", 0.62), tex=1024),
    dict(name="OfficeKey", src=os.path.join(SF, "old_gold_key.glb"), rot=(90, 0, 0), size=("x", 0.13),
         decimate=4000, tex=512, glow=0.5),
    dict(name="ServiceBell", src=os.path.join(SF, "reception_bell._wood_shiny_copper.glb"), size=("x", 0.13),
         tex=512, glow=0.3),
    dict(name="Chocolates", src=os.path.join(SF, "chocolatebox.glb"), size=("x", 0.2), tex=1024, glow=0.35),
    dict(name="FuseBox", src=os.path.join(SF, "freebie_-_game_art_vintage_fuse_box.glb"), size=("z", 0.8),
         door=-3.0, tex=1024),
    dict(name="KnifeSwitchOpen", src=os.path.join(SF, "early_1900s_knife_switches_type_d.glb"), pick=["Object_4"],
         size=("z", 0.0), scale=1.05, tex=1024, glow=0.2, texset="KnifeSwitch"),
    dict(name="KnifeSwitchClosed", src=os.path.join(SF, "early_1900s_knife_switches_type_d.glb"), pick=["Object_6"],
         size=("z", 0.0), scale=1.05, tex=1024, glow=0.2, texset="KnifeSwitch"),
    dict(name="Batteries", src=os.path.join(SF, "aa_battery.glb"), size=("x", 0.085), tex=512, glow=0.4),
    dict(name="Doll", src=os.path.join(SF, "doll_zip", "source", "model.glb"), size=("z", 0.55), tex=2048),
    dict(name="Fuse", src=os.path.join(DL, "PP_Fuse", "fuse.glb"), size=("z", 0.13), tex=256, glow=0.5),
    dict(name="MusicBox", src=os.path.join(SF, "music_box_-_mid_poly.glb"), size=("x", 0.3), tex=1024, glow=0.15,
         texcaps={"Hinges_Screws_Mirror_Mat": 512, "InnerSyst_Mat": 512, "Legs_Key_Mat": 512, "Glass_Mat": 512}),
    dict(name="Teapot", src=os.path.join(PH, "tea_set_01", "tea_set_01_2k.gltf"),
         pick=["tea_set_01_teapot_01", "tea_set_01_teapot_01_lid"], size=("z", 0.0), decimate=6000, tex=1024,
         glow=0.3, texset="TeaSet"),
    dict(name="SugarBowl", src=os.path.join(PH, "tea_set_01", "tea_set_01_2k.gltf"),
         pick=["tea_set_01_sugar_cup_01", "tea_set_01_sugar_cup_01_lid"], size=("z", 0.0), decimate=4000, tex=1024,
         glow=0.3, texset="TeaSet"),
    dict(name="TeaCup", src=os.path.join(PH, "tea_set_01", "tea_set_01_2k.gltf"),
         pick=["tea_set_01_cup_small_01", "tea_set_01_saucer_circular_04"], stack=True, size=("z", 0.0),
         decimate=4000, tex=1024, texset="TeaSet"),
    dict(name="Plate", src=os.path.join(PH, "tea_set_01", "tea_set_01_2k.gltf"),
         pick=["tea_set_01_plate_large_circular_03"], size=("z", 0.0), decimate=1500, tex=1024, texset="TeaSet"),
    dict(name="Cake", src=os.path.join(PH, "strawberry_chocolate_cake", "strawberry_chocolate_cake_2k.gltf"),
         size=("x", 0.2), decimate=8000, tex=1024, glow=0.25),
    dict(name="Desk", src=os.path.join(PH, "metal_office_desk", "metal_office_desk_2k.gltf"), size=("z", 0.0),
         narrow=(0.505, 1.6), tex=2048, tint=(0.72, 0.7, 0.66)),
    dict(name="Notebook", src=os.path.join(PH, "binder_notebook", "binder_notebook_2k.gltf"),
         pick=["binder_notebook"], exact=True, size=("z", 0.0), decimate=3000, tex=1024, glow=0.15),
    dict(name="Chair", src=os.path.join(PH, "painted_wooden_chair_02", "painted_wooden_chair_02_2k.gltf"),
         size=("z", 0.0), tex=1024),
    dict(name="NightstandA", src=os.path.join(PH, "ClassicNightstand_01", "ClassicNightstand_01_2k.gltf"),
         size=("z", 0.0), tex=1024),
    dict(name="NightstandB", src=os.path.join(PH, "painted_wooden_nightstand", "painted_wooden_nightstand_2k.gltf"),
         size=("z", 0.0), tex=1024),
]


# ---------------------------------------------------------------------------------------------- helpers

def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.node_groups):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def import_source(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o not in before]


def bake_meshes(objs, pick, exact):
    """Copies the picked meshes into new objects with their world transform applied."""
    result = []
    for o in objs:
        if o.type != 'MESH':
            continue
        if pick:
            if exact and o.name not in pick:
                continue
            if not exact and not any(o.name == p or o.name.startswith(p + ".") for p in pick):
                continue
        me = o.data.copy()
        me.transform(o.matrix_world)
        n = bpy.data.objects.new(o.name + "_baked", me)
        bpy.context.scene.collection.objects.link(n)
        result.append(n)
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)
    return result


def bounds(objs):
    mn = mathutils.Vector((1e9,) * 3)
    mx = mathutils.Vector((-1e9,) * 3)
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            mn = mathutils.Vector(map(min, mn, w))
            mx = mathutils.Vector(map(max, mx, w))
    return mn, mx


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.name = name
    return o


def transform_mesh(o, matrix):
    o.data.transform(matrix)
    o.data.update()


def loose_parts(bm):
    seen = set()
    parts = []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack = [v]
        comp = []
        seen.add(v.index)
        while stack:
            a = stack.pop()
            comp.append(a)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in seen:
                    seen.add(b.index)
                    stack.append(b)
        parts.append(comp)
    return parts


def swing_door_flat(o, door_max_y):
    """FuseBox: the door hangs open at 90 degrees, sticking ~0.6 m into the room. Swings it to 180 degrees so it lies
    flat beside the box, just in front of the box's front plane, to the left of the box. Door parts = loose parts lying
    entirely in front of `door_max_y` (source units)."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    door, body = [], []
    for p in loose_parts(bm):
        (door if max(v.co.y for v in p) < door_max_y else body).extend(p)
    if not door:
        print("  FuseBox: no door parts found")
        bm.free()
        return
    rot = mathutils.Matrix.Rotation(math.radians(-90.0), 4, 'Z')
    for v in door:
        v.co = rot @ v.co
    body_min_x = min(v.co.x for v in body)
    front_y = min(v.co.y for v in body)
    shift = mathutils.Vector((body_min_x - 0.5 - max(v.co.x for v in door), front_y - max(v.co.y for v in door), 0.0))
    for v in door:
        v.co += shift
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    print(f"  FuseBox: door ({len(door)} vertices) swung flat, left of x={body_min_x:.2f}, front plane y={front_y:.2f}")


def narrow_desk(o, inner, width):
    """Metal desk: 2.0 m is too wide for a wall slot. Keeps the drawer pedestals intact and shortens the middle
    (knee space and top) so the desk is `width` wide."""
    mn, mx = bounds([o])
    half = (mx.x - mn.x) * 0.5
    cx = (mx.x + mn.x) * 0.5
    k = (width * 0.5 - (half - inner)) / inner
    for v in o.data.vertices:
        x = v.co.x - cx
        ax = abs(x)
        nx = x * k if ax <= inner else math.copysign(inner * k + (ax - inner), x)
        v.co.x = cx + nx
    o.data.update()


def stack_cup_on_saucer(objs):
    cup = next(o for o in objs if "cup" in o.name)
    saucer = next(o for o in objs if "saucer" in o.name)
    cmn, cmx = bounds([cup])
    smn, smx = bounds([saucer])
    offset = mathutils.Vector(((smn.x + smx.x) * 0.5 - (cmn.x + cmx.x) * 0.5,
                               (smn.y + smx.y) * 0.5 - (cmn.y + cmx.y) * 0.5,
                               smx.z - 0.006 - cmn.z))
    transform_mesh(cup, mathutils.Matrix.Translation(offset))


def decimate(o, target):
    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if tris <= target:
        return tris
    mod = o.modifiers.new("Decimate", 'DECIMATE')
    mod.ratio = target / tris
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def smooth(o):
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    if o.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40.0))


# ---------------------------------------------------------------------------------------------- materials

def principled(mat):
    if not mat or not mat.use_nodes:
        return None
    return next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)


def trace_image(socket):
    """Follows a socket's link back to an image. Returns (image, channel, factor) where channel is 'RGB', 'A', 'R',
    'G' or 'B' and factor a constant multiplier from MATH/MIX nodes on the way."""
    factor = 1.0
    channel = None
    s = socket
    for _ in range(6):
        if not s.is_linked:
            return None, None, factor
        link = s.links[0]
        n = link.from_node
        if n.type == 'TEX_IMAGE':
            ch = 'A' if link.from_socket.name == 'Alpha' else (channel or 'RGB')
            return n.image, ch, factor
        if n.type == 'SEPARATE_COLOR':
            channel = {'Red': 'R', 'Green': 'G', 'Blue': 'B'}[link.from_socket.name]
            s = n.inputs[0]
        elif n.type == 'MATH':
            linked = [i for i in n.inputs[:2] if i.is_linked]
            const = [i for i in n.inputs[:2] if not i.is_linked]
            if n.operation == 'MULTIPLY' and const:
                factor *= const[0].default_value
            s = linked[0] if linked else n.inputs[0]
        elif n.type in ('MIX', 'MIX_RGB'):
            linked = [i for i in n.inputs if i.is_linked and i.type in ('RGBA', 'VECTOR')]
            if not linked:
                return None, None, factor
            s = linked[0]
        elif n.type == 'NORMAL_MAP':
            s = n.inputs['Color']
        else:
            linked = [i for i in n.inputs if i.is_linked]
            if not linked:
                return None, None, factor
            s = linked[0]
    return None, None, factor


def pixels(img, size):
    """Image pixels (float32 RGBA, rows bottom-up) resized to at most `size` px on the longest side."""
    w, h = img.size
    scale = min(1.0, size / max(w, h))
    tw, th = max(1, int(round(w * scale))), max(1, int(round(h * scale)))
    work = img.copy()
    if (tw, th) != (w, h):
        work.scale(tw, th)
    arr = np.empty(tw * th * 4, dtype=np.float32)
    work.pixels.foreach_get(arr)
    bpy.data.images.remove(work)
    return arr.reshape(th, tw, 4)


def resize_to(arr, shape):
    if arr.shape[:2] == shape:
        return arr
    th, tw = shape
    ys = (np.arange(th) * arr.shape[0] / th).astype(int)
    xs = (np.arange(tw) * arr.shape[1] / tw).astype(int)
    return arr[ys][:, xs]


def save_png(arr, path, alpha):
    h, w = arr.shape[:2]
    img = bpy.data.images.new("tmp_save", w, h, alpha=alpha, float_buffer=False)
    if not alpha:
        arr = arr.copy()
        arr[..., 3] = 1.0
    img.pixels.foreach_set(arr.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def channel_of(arr, ch):
    idx = {'R': 0, 'G': 1, 'B': 2, 'A': 3, 'RGB': 0}[ch]
    return arr[..., idx]


def export_texture_set(set_name, mat, cap, written):
    """Writes <Set>_Albedo/_Normal/_Mask.png for a glTF-style material. Returns the material description."""
    p = principled(mat)
    info = dict(set=set_name, alpha="opaque", baseColor=[1, 1, 1, 1], metallic=0.0, smoothness=0.5,
                albedo=False, normal=False, mask=False)
    if p is None:
        return info
    folder = os.path.join(OUT_TEX, set_name)
    base_img, _, _ = trace_image(p.inputs['Base Color'])
    alpha_img, alpha_ch, _ = trace_image(p.inputs['Alpha'])
    metal_img, metal_ch, metal_f = trace_image(p.inputs['Metallic'])
    rough_img, rough_ch, rough_f = trace_image(p.inputs['Roughness'])
    normal_img, _, _ = trace_image(p.inputs['Normal'])
    if not base_img:
        info["baseColor"] = list(p.inputs['Base Color'].default_value)
    if alpha_img or mat.surface_render_method == 'BLENDED':
        info["alpha"] = "blend"
        if not alpha_img:
            info["baseColor"][3] = p.inputs['Alpha'].default_value
    if not metal_img:
        info["metallic"] = p.inputs['Metallic'].default_value
    if not rough_img:
        info["smoothness"] = 1.0 - p.inputs['Roughness'].default_value
    else:
        info["smoothness"] = 1.0

    if set_name in written:
        info.update(written[set_name])
        return info
    os.makedirs(folder, exist_ok=True)
    flags = {}
    if base_img:
        albedo = pixels(base_img, cap)
        if alpha_img:
            a = resize_to(pixels(alpha_img, cap), albedo.shape[:2])
            albedo[..., 3] = channel_of(a, alpha_ch)
        save_png(albedo, os.path.join(folder, set_name + "_Albedo.png"), alpha=bool(alpha_img))
        flags["albedo"] = True
        shape = albedo.shape[:2]
    else:
        shape = None
    if normal_img:
        nrm = pixels(normal_img, cap)
        save_png(nrm, os.path.join(folder, set_name + "_Normal.png"), alpha=False)
        flags["normal"] = True
        shape = shape or nrm.shape[:2]
    if metal_img or rough_img:
        mcap = max(256, cap // 2)
        src = pixels(metal_img or rough_img, mcap)
        mask = np.zeros_like(src)
        if metal_img:
            mask[..., 0] = np.clip(channel_of(src, metal_ch) * metal_f, 0, 1)
        else:
            mask[..., 0] = p.inputs['Metallic'].default_value
        r_src = src if (rough_img == metal_img or not rough_img) else resize_to(pixels(rough_img, mcap), src.shape[:2])
        if rough_img:
            mask[..., 3] = 1.0 - np.clip(channel_of(r_src, rough_ch) * rough_f, 0, 1)
        else:
            mask[..., 3] = 1.0 - p.inputs['Roughness'].default_value
        save_png(mask, os.path.join(folder, set_name + "_Mask.png"), alpha=True)
        flags["mask"] = True
    written[set_name] = flags
    info.update(flags)
    return info


# ---------------------------------------------------------------------------------------------- main

def build_prop(spec, written, manifest):
    name = spec["name"]
    objs = bake_meshes(import_source(spec["src"]), spec.get("pick"), spec.get("exact", False))
    if not objs:
        print(name, "NO MESHES")
        return
    if spec.get("stack"):
        stack_cup_on_saucer(objs)
    o = join(objs, name)
    if "rot" in spec:
        rx, ry, rz = (math.radians(a) for a in spec["rot"])
        transform_mesh(o, mathutils.Euler((rx, ry, rz), 'XYZ').to_matrix().to_4x4())
    if "door" in spec:
        swing_door_flat(o, spec["door"])
    if "narrow" in spec:
        narrow_desk(o, *spec["narrow"])

    axis, length = spec["size"]
    mn, mx = bounds([o])
    s = spec.get("scale", 1.0)
    if length > 0:
        s = length / (mx - mn)["xyz".index(axis)]
    centre = mathutils.Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z))
    transform_mesh(o, mathutils.Matrix.Scale(s, 4) @ mathutils.Matrix.Translation(-centre))

    tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if "decimate" in spec and tris > spec["decimate"]:
        # Decimation breaks the imported custom normals: rebuild them from the face angles.
        tris = decimate(o, spec["decimate"])
        smooth(o)

    mats = []
    for slot in o.material_slots:
        src_mat = slot.material
        part = (src_mat.name if src_mat else "Default").replace(".", "_").replace(" ", "_")
        set_name = spec.get("texset") or (name if len(o.material_slots) == 1 else f"{name}_{part}")
        cap = spec.get("texcaps", {}).get(src_mat.name if src_mat else "", spec.get("tex", 1024))
        desc = export_texture_set(set_name, src_mat, cap, written)
        mat_name = f"{name}_{part}" if len(o.material_slots) > 1 else name
        new_mat = bpy.data.materials.new(mat_name)
        slot.material = new_mat
        desc.update(name=mat_name, glow=spec.get("glow", 0.0))
        if "tint" in spec:
            # Darkens/greys a texture that looks too clean for the abandoned cafe (sRGB tint -> linear factor).
            desc["baseColor"] = [c * (t ** 2.2) for c, t in zip(desc["baseColor"][:3], spec["tint"])] + [desc["baseColor"][3]]
        mats.append(desc)
        manifest["materials"].append(desc)

    mn, mx = bounds([o])
    size = mx - mn
    # Facing check: the deeper half of the silhouette's upper part tells where the back is (chairs, shelves).
    top = [v.co for v in o.data.vertices if v.co.z > mn.z + 0.75 * size.z]
    back_y = sum(v.y for v in top) / max(1, len(top))
    print(f"{name}: tris {tris} size {size.x:.3f} x {size.y:.3f} x {size.z:.3f} (w x d x h) "
          f"upper-centroid y {back_y:+.3f} mats {[m['name'] for m in mats]}")

    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    os.makedirs(OUT_FBX, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_FBX, name + ".fbx"), use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False, path_mode='STRIP',
        embed_textures=False, bake_anim=False, add_leaf_bones=False)
    manifest["props"].append(dict(name=name, materials=[m["name"] for m in mats], tris=tris,
                                  size=[round(size.x, 4), round(size.z, 4), round(size.y, 4)]))


def main(only=None):
    clear_scene()
    written = {}
    manifest = {"materials": [], "props": []}
    path = os.path.join(OUT_TEX, "props.json")
    if only and os.path.exists(path):
        with open(path) as f:
            old = json.load(f)
        manifest["props"] = [p for p in old["props"] if p["name"] not in only]
        keep = {m for p in manifest["props"] for m in p["materials"]}
        manifest["materials"] = [m for m in old["materials"] if m["name"] in keep]
    for spec in PROPS:
        if only and spec["name"] not in only:
            continue
        build_prop(spec, written, manifest)
    os.makedirs(OUT_TEX, exist_ok=True)
    order = [s["name"] for s in PROPS]
    manifest["props"].sort(key=lambda p: order.index(p["name"]) if p["name"] in order else 999)
    with open(path, "w") as f:
        json.dump(manifest, f, indent=1)
    print("manifest:", path, len(manifest["props"]), "props", len(manifest["materials"]), "materials")


if __name__ == "__main__":
    main()
