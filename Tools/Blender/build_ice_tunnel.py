"""Hollow ice tunnel segment that tiles exactly: geometry and textures repeat over its 24 m length.
  blender -b -P Tools/Blender/build_ice_tunnel.py -- <out.glb> <textures dir> [render.png]
Faceted ice plates come from one periodic cell pattern shared by the mesh, colour and normal map."""
import math, os, sys
import bpy, numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
OUT, TEX = os.path.abspath(args[0]), os.path.abspath(args[1])
RENDER = os.path.abspath(args[2]) if len(args) > 2 else None
LENGTH, HALF_W, HEIGHT = 24.0, 10.5, 10.0
ARC, RINGS, SIZE = 56, 48, 1024
rng = np.random.default_rng(11)
bpy.ops.wm.read_factory_settings(use_empty=True)

# Ice plates: Worley cells that wrap along the tunnel axis (v) so the two ends match.
CU, CV = 26, 34
gu, gv = np.meshgrid(np.arange(CU), np.arange(CV), indexing="ij")
pts_u = (gu + rng.random(gu.shape)) / CU
pts_v = (gv + rng.random(gv.shape)) / CV
plate = rng.random(gu.shape).ravel()
pu, pv = pts_u.ravel(), pts_v.ravel()

def cells(u, v):
    """F1, F2 distance and the nearest plate's height for arrays of u, v in 0..1 (v wraps)."""
    du = (u[..., None] - pu) * (CU / CV) * 2.0                # roughly square plates on the wall
    dv = v[..., None] - pv
    dv -= np.round(dv)
    d = np.sqrt(du * du + dv * dv)
    part = np.partition(d, 1, axis=-1)
    return part[..., 0], part[..., 1], plate[np.argmin(d, axis=-1)]

def rib(v):                                                  # thick ribs of ice every 8 m, hiding the eye's sense of repeat
    return np.clip(np.cos(v * math.pi * 2 * 3), 0, 1) ** 8

# ---------- textures ----------
us = (np.arange(SIZE) + .5) / SIZE
U, V = np.meshgrid(us, us, indexing="xy")   # rows = v, columns = u
rows = []
for start in range(0, SIZE, 64):
    rows.append(cells(U[start:start + 64], V[start:start + 64]))
F1 = np.concatenate([r[0] for r in rows]); F2 = np.concatenate([r[1] for r in rows]); H = np.concatenate([r[2] for r in rows])
edge = np.clip((F2 - F1) * 34, 0, 1)
height = H * .75 * np.clip(edge * 2.4, 0, 1) + rib(V) * .8
deep, mid, pale = np.array([.03, .2, .42]), np.array([.12, .5, .76]), np.array([.72, .95, 1.0])
t = (H * .85 + .15 * np.sin(U * 40) * np.sin(V * math.pi * 2 * 4))[..., None]
color = deep + (mid - deep) * np.clip(t * 1.5, 0, 1) + (pale - mid) * np.clip(t * 1.5 - .9, 0, 1)
crack = (1 - np.clip((F2 - F1) * 90, 0, 1))[..., None]        # bright fractures along the plate edges
color = color * (1 - crack * .25) + pale * crack * .75
color = color + (pale - color) * (rib(V)[..., None] * .35)
sky = np.clip((U - .5) ** 2 * -4 + 1, 0, 1)[..., None]        # the crown of the arch is paler, lit from above
color = np.clip(color * (.75 + sky * .4), 0, 1)

def save(name, rgb):
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    img.colorspace_settings.name = "Non-Color"     # values are written as they are
    px = np.concatenate([rgb, np.ones(rgb.shape[:2] + (1,))], axis=2)
    img.pixels.foreach_set(px.astype(np.float32).ravel())
    img.filepath_raw = os.path.join(TEX, name + ".png"); img.file_format = "PNG"; img.save()
    return img

os.makedirs(TEX, exist_ok=True)
save("IceTunnel_color", color)
du = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * .5
dv = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * .5
strength = 9.0
n = np.dstack((-du * strength, -dv * strength, np.ones_like(height)))
n /= np.linalg.norm(n, axis=2, keepdims=True)
save("IceTunnel_normal", n * .5 + .5)

# ---------- mesh ----------
def surface(u, v):
    ang = -.1 * math.pi + u * 1.2 * math.pi                  # the walls carry on below the water line
    f1, f2, h = cells(np.array([[u]]), np.array([[v]]))
    plates = float(h[0, 0]) * .75 + float(rib(np.array(v))) * .9
    inward = plates + .35 * math.sin(ang * 5 + 1.3) * math.cos(v * math.pi * 2 * 2)
    s = 1 - inward / max(HALF_W, HEIGHT)
    return (math.cos(ang) * HALF_W * s, -v * LENGTH, math.sin(ang) * HEIGHT * s)

verts, faces, uvs = [], [], []
for j in range(RINGS + 1):
    for i in range(ARC + 1):
        v = (j % RINGS) / RINGS                               # last ring reuses the first ring's shape exactly
        x, y, z = surface(i / ARC, v)
        verts.append((x, y - (LENGTH if j == RINGS else 0), z))
for j in range(RINGS):
    for i in range(ARC):
        a = j * (ARC + 1) + i; b = a + 1; c = a + ARC + 2; d = a + ARC + 1
        faces.append((a, d, c, b))                            # wound to face the inside of the tunnel
mesh = bpy.data.meshes.new("IceTunnel"); mesh.from_pydata(verts, [], faces); mesh.update()
uv = mesh.uv_layers.new(name="UVMap")
for poly in mesh.polygons:
    for li in poly.loop_indices:
        vi = mesh.loops[li].vertex_index
        uv.data[li].uv = ((vi % (ARC + 1)) / ARC, (vi // (ARC + 1)) / RINGS)
ob = bpy.data.objects.new("IceTunnel", mesh); bpy.context.scene.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
m = ob.modifiers.new("Shell", "SOLIDIFY"); m.thickness = 1.2; m.offset = 1
bpy.ops.object.modifier_apply(modifier=m.name)
for p in mesh.polygons: p.use_smooth = False
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(24))

mat = bpy.data.materials.new("M_IceTunnel"); mat.use_nodes = True; mesh.materials.append(mat)
nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]; bsdf.inputs["Roughness"].default_value = .3
c = nt.nodes.new("ShaderNodeTexImage"); c.image = bpy.data.images["IceTunnel_color"]
nrm = nt.nodes.new("ShaderNodeTexImage"); nrm.image = bpy.data.images["IceTunnel_normal"]
nm = nt.nodes.new("ShaderNodeNormalMap")
nt.links.new(c.outputs["Color"], bsdf.inputs["Base Color"]); nt.links.new(nrm.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
print("TUNNEL faces", len(mesh.polygons), "verts", len(mesh.vertices))
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=False, export_materials="NONE", export_tangents=True)

if RENDER:
    sc = bpy.context.scene; sc.render.engine = "BLENDER_EEVEE"; sc.render.resolution_x = 700; sc.render.resolution_y = 900
    w = bpy.data.worlds.new("w"); w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (.5, .7, .85, 1); sc.world = w
    # three segments in a row to expose any seam
    for k in (1, 2):
        o2 = ob.copy(); o2.location.y = -LENGTH * k; sc.collection.objects.link(o2)
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun); sun.data.energy = 2; sun.rotation_euler = (math.radians(70), 0, 0)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 26
    cam.location = (0, 4, 3.7); cam.rotation_euler = (math.radians(82), 0, math.radians(180))
    sc.render.filepath = RENDER; bpy.ops.render.render(write_still=True)
