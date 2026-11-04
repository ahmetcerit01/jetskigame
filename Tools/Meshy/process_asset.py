"""Blender step for a raw Meshy GLB: real-world size, base on the ground, centred, decimated to budget, review renders.
  blender -b -P Tools/Meshy/process_asset.py -- raw.glb out.glb --fit height --size 18 --faces 12000 --renders dir
Colour and detail are baked from the raw mesh onto new UVs. Prints stats as JSON on a PROCESS line."""
import argparse, json, math, os, sys
import bmesh, bpy
from mathutils import Vector

a = argparse.ArgumentParser()
a.add_argument("raw"); a.add_argument("out")
a.add_argument("--fit", default="height"); a.add_argument("--size", type=float, required=True)
a.add_argument("--faces", type=int, required=True); a.add_argument("--renders"); a.add_argument("--texture", type=int, default=2048); a.add_argument("--textures-dir", required=True); a.add_argument("--voxel", type=int, default=0, help="voxel remesh first, at size/voxel")
a = a.parse_args(sys.argv[sys.argv.index("--") + 1:])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.abspath(a.raw))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
ctx = bpy.context
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
ctx.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
ob = ctx.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o is not ob: bpy.data.objects.remove(o)
raw_faces = len(ob.data.polygons)

def bounds():
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    return Vector(min(p[i] for p in pts) for i in range(3)), Vector(max(p[i] for p in pts) for i in range(3))

lo, hi = bounds(); dim = hi - lo
measure = dim.z if a.fit == "height" else max(dim.x, dim.y)
s = a.size / measure
ob.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
lo, hi = bounds()
ob.location = (-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)
bpy.ops.object.transform_apply(location=True)

# Collapse decimation tears the original UVs (black cracks), so build a clean low-poly with fresh UVs and
# bake colour and surface detail onto it from the untouched high-poly.
high = ob
high.select_set(True)
low = high.copy(); low.data = high.data.copy(); low.name = "low"
ctx.collection.objects.link(low)
bpy.ops.object.select_all(action="DESELECT"); low.select_set(True); ctx.view_layer.objects.active = low
# Meshy splits vertices along its UV islands; decimating those open shells pulls them apart into real gaps.
# Weld them first (the UVs are rebuilt anyway).
verts_before = len(low.data.vertices)
bm = bmesh.new(); bm.from_mesh(low.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=a.size * 2e-5)
bm.to_mesh(low.data); bm.free(); low.data.update()
welded = verts_before - len(low.data.vertices)
if a.voxel:   # for messy shells that collapse decimation cannot handle: rebuild one clean surface first
    m = low.modifiers.new("Remesh", "REMESH"); m.mode = "VOXEL"; m.voxel_size = a.size / a.voxel; m.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=m.name)
tris = sum(len(p.vertices) - 2 for p in low.data.polygons)   # voxel remesh makes quads; the budget is triangles
if tris > a.faces:
    m = low.modifiers.new("Decimate", "DECIMATE"); m.ratio = a.faces / tris
    bpy.ops.object.modifier_apply(modifier=m.name)
low.data.materials.clear()
while low.data.uv_layers: low.data.uv_layers.remove(low.data.uv_layers[0])
low.data.uv_layers.new(name="UVMap")
bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=.004)
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))

mat = bpy.data.materials.new("M_" + os.path.splitext(os.path.basename(a.out))[0]); mat.use_nodes = True
low.data.materials.append(mat)
nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = .75
def bake_image(name, colorspace):
    img = bpy.data.images.new(name, a.texture, a.texture, alpha=False)
    img.colorspace_settings.name = colorspace
    node = nt.nodes.new("ShaderNodeTexImage"); node.image = img
    nt.nodes.active = node; node.select = True
    return img, node
sc = ctx.scene; sc.render.engine = "CYCLES"; sc.cycles.samples = 8; sc.cycles.device = "CPU"
sc.render.bake.use_selected_to_active = True; sc.render.bake.margin = 12
reach = max(dim) * s * .012
sc.render.bake.cage_extrusion = reach; sc.render.bake.max_ray_distance = reach * 2.5
high.select_set(True); low.select_set(True); ctx.view_layer.objects.active = low
col, col_node = bake_image("T_color", "sRGB")
sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
bpy.ops.object.bake(type="DIFFUSE")
nrm, nrm_node = bake_image("T_normal", "Non-Color")
bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
col.pack(); nrm.pack()
nt.links.new(col_node.outputs["Color"], bsdf.inputs["Base Color"])
nm = nt.nodes.new("ShaderNodeNormalMap"); nm.space = "TANGENT"
nt.links.new(nrm_node.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
bpy.data.objects.remove(high)
ob = low
lo, hi = bounds(); dim = hi - lo
tex = [(i.name, i.size[0], i.size[1]) for i in bpy.data.images if i.size[0]]
print("PROCESS " + json.dumps({"raw_faces": raw_faces, "welded_verts": welded, "faces": len(ob.data.polygons), "dims_m": [round(x, 2) for x in dim], "textures": tex, "materials": len(ob.data.materials)}))
# Mesh only in the GLB: textures embedded in a GLB stay uncompressed in Unity. They go out as files for its importer.
base = os.path.splitext(os.path.abspath(a.out))[0]
os.makedirs(a.textures_dir, exist_ok=True)
stem = os.path.join(os.path.abspath(a.textures_dir), os.path.basename(base))
for img, suffix in ((col, "_color"), (nrm, "_normal")):
    img.filepath_raw = stem + suffix + ".png"; img.file_format = "PNG"; img.save()
bpy.ops.export_scene.gltf(filepath=os.path.abspath(a.out), export_format="GLB", use_selection=False, export_materials="NONE", export_tangents=True)

if a.renders:
    os.makedirs(a.renders, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 640; sc.render.resolution_y = 860; sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w"); sc.world.use_nodes = True
    sc.world.node_tree.nodes["Background"].inputs[0].default_value = (.62, .68, .75, 1)
    sc.world.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
    sun.data.energy = 3; sun.rotation_euler = (math.radians(50), 0, math.radians(30))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.lens = 70
    h = dim.z; radius = max(dim) * 2.6; centre = Vector((0, 0, h / 2))
    for name, ang in (("front", -90), ("right", 0), ("back", 90), ("left", 180)):
        cam.location = centre + Vector((math.cos(math.radians(ang)) * radius, math.sin(math.radians(ang)) * radius, h * .12))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(os.path.abspath(a.renders), name + ".png")
        bpy.ops.render.render(write_still=True)
