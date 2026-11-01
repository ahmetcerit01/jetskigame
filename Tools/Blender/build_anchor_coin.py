import bpy
import math
from mathutils import Vector

ROOT = r"C:\Users\Ahmet Cerit\JetskiRunner"

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

def material(name, color, metallic=.65, roughness=.24):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.metallic = metallic
    mat.roughness = roughness
    mat.use_nodes = True
    surface = mat.node_tree.nodes.get('Principled BSDF')
    surface.inputs['Base Color'].default_value = (*color, 1)
    surface.inputs['Metallic'].default_value = metallic
    surface.inputs['Roughness'].default_value = roughness
    return mat

gold = material("Gold - warm polished", (1.0, .47, .015), .78, .2)
bright = material("Gold - bevel highlight", (1.0, .78, .08), .7, .18)
dark = material("Gold - inset", (.72, .22, .005), .72, .27)

def cylinder(name, radius, depth, y, mat, vertices=64, bevel=.035):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=(0, y, 0), rotation=(math.pi/2, 0, 0))
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(mat)
    mod = obj.modifiers.new("Soft mobile bevel", 'BEVEL')
    mod.width = bevel
    mod.segments = 3
    bpy.ops.object.shade_smooth()
    return obj

cylinder("Coin body", 1.0, .35, 0, gold, bevel=.055)
for side in (-1, 1):
    cylinder("Raised rim side %s" % side, .91, .07, side*.18, bright, bevel=.022)
    cylinder("Inset side %s" % side, .755, .075, side*.203, dark, bevel=.018)
    cylinder("Center face side %s" % side, .69, .083, side*.21, gold, bevel=.018)

# Radial grooves on the edge are actual geometry, visible during rotation.
for index in range(64):
    angle = index * math.tau / 64
    bpy.ops.mesh.primitive_cube_add(size=1, location=(math.cos(angle)*.992,0,math.sin(angle)*.992))
    groove = bpy.context.object
    groove.name = "Milled edge %02d" % index
    groove.scale = (.029,.30,.018)
    groove.rotation_euler[1] = -angle
    groove.data.materials.append(bright if index % 2 == 0 else dark)

def bar(name, start, end, width, side, depth=.065, mat=bright):
    midpoint = (Vector(start) + Vector(end)) * .5
    delta = Vector(end) - Vector(start)
    bpy.ops.mesh.primitive_cube_add(location=(midpoint.x, side*.27, midpoint.z))
    obj = bpy.context.object
    obj.name = name
    obj.scale = (width * .5, depth * .5, delta.length * .5)
    obj.rotation_euler[1] = math.atan2(delta.x, delta.z)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    bevel = obj.modifiers.new("Anchor bevel", 'BEVEL')
    bevel.width = .045
    bevel.segments = 3
    bpy.ops.object.shade_smooth()
    return obj

for side in (-1, 1):
    bar("Anchor stem", (0, 0, -.40), (0, 0, .43), .16, side)
    bar("Anchor crossbar", (-.35, 0, .17), (.35, 0, .17), .13, side)
    bar("Anchor left arm", (0, 0, -.39), (-.43, 0, -.13), .14, side)
    bar("Anchor right arm", (0, 0, -.39), (.43, 0, -.13), .14, side)
    bar("Anchor left fluke", (-.43, 0, -.13), (-.51, 0, .04), .13, side)
    bar("Anchor right fluke", (.43, 0, -.13), (.51, 0, .04), .13, side)
    bpy.ops.mesh.primitive_torus_add(major_radius=.16, minor_radius=.055, major_segments=32, minor_segments=12, location=(0, side*.27, .53), rotation=(math.pi/2, 0, 0))
    ring = bpy.context.object
    ring.name = "Anchor top ring"
    ring.data.materials.append(bright)
    bpy.ops.object.shade_smooth()

for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        obj.select_set(True)

# Reduce draw calls for the mobile game by joining pieces sharing one material.
for coin_material in (gold, bright, dark):
    bpy.ops.object.select_all(action='DESELECT')
    group = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH' and obj.data.materials and obj.data.materials[0] == coin_material]
    for obj in group:
        bpy.context.view_layer.objects.active = obj
        for modifier in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj.select_set(True)
    if group:
        bpy.context.view_layer.objects.active = group[0]
        bpy.ops.object.join()
        group[0].name = 'Coin ' + coin_material.name

bpy.context.scene.render.engine = 'BLENDER_EEVEE'
bpy.context.scene.world.color = (.025, .035, .055)
bpy.ops.wm.save_as_mainfile(filepath=ROOT + r"\Assets\BlenderSource\AnchorCoin.blend")
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=ROOT + r"\Assets\Models\AnchorCoin.fbx",
    use_selection=True,
    apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z',
    axis_up='Y',
    bake_anim=False,
    add_leaf_bones=False,
    path_mode='AUTO'
)
print("ANCHOR_COIN_EXPORT_OK")

bpy.ops.object.camera_add(location=(2.5,-5.2,2.3))
camera = bpy.context.object
camera.rotation_euler = (Vector((0,0,0))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO'; camera.data.ortho_scale=3.2
bpy.context.scene.camera=camera
bpy.ops.object.light_add(type='AREA', location=(-2,-4,4))
light=bpy.context.object; light.data.energy=700; light.data.shape='DISK'; light.data.size=4
light.rotation_euler=(Vector((0,0,0))-light.location).to_track_quat('-Z','Y').to_euler()
bpy.context.scene.render.resolution_x=800; bpy.context.scene.render.resolution_y=800
bpy.context.scene.render.resolution_percentage=100
bpy.context.scene.render.filepath=ROOT+r'\Build\AnchorCoinPreview.png'
bpy.ops.render.render(write_still=True)
