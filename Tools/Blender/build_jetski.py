import bpy, math
from mathutils import Vector
ROOT=r'C:\Users\Ahmet Cerit\JetskiRunner'
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
mats={}
for name,col in {'Hull':(.025,.045,.065),'Turquoise':(.015,.72,.76),'Orange':(1,.24,.015),'Seat':(.065,.075,.09),'Silver':(.42,.52,.56),'Skin':(.66,.32,.13),'Hair':(.095,.035,.012)}.items():
    m=bpy.data.materials.new(name); m.diffuse_color=(*col,1); m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*col,1); bs.inputs['Roughness'].default_value=.36
    mats[name]=m
def finish(o,name,mat,bevel=0):
    o.name=name; o.data.materials.append(mats[mat])
    if bevel:
        mod=o.modifiers.new('Edge bevel','BEVEL'); mod.width=bevel; mod.segments=4
    for poly in o.data.polygons: poly.use_smooth=True
    if bevel:
        normal=o.modifiers.new('Weighted surface normals','WEIGHTED_NORMAL'); normal.keep_sharp=True; normal.weight=50
    return o
def box(name,pos,size,mat,bevel=.03):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos); o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,bevel)
def ell(name,pos,scale,mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=20,radius=1,location=pos); o=bpy.context.object; o.scale=scale
    return finish(o,name,mat)
def limb(name,a,b,r1,r2,mat):
    d=Vector(b)-Vector(a)
    bpy.ops.mesh.primitive_cone_add(vertices=24,radius1=r1,radius2=r2,depth=d.length,location=(Vector(a)+Vector(b))/2)
    o=bpy.context.object; o.rotation_euler=d.to_track_quat('Z','Y').to_euler(); return finish(o,name,mat,.018)
def hull(name,stations,mat):
    verts=[]
    for y,w,z,h in stations:
        verts.extend([(-w,y,z),(w,y,z),(w*.78,y,z-h),(-w*.78,y,z-h)])
    faces=[(3,2,1,0)]
    for i in range(len(stations)-1):
        for j in range(4):
            a=i*4+j; b=i*4+(j+1)%4; faces.append((a,b,b+4,a+4))
    n=(len(stations)-1)*4; faces.append((n,n+1,n+2,n+3))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(o)
    finish(o,name,mat,.045)
    sub=o.modifiers.new('Curved bodywork','SUBSURF'); sub.levels=2; sub.render_levels=2
    return o
# Blender +Y is the bow and +Z is up.
hull('Sculpted graphite hull',[(-1.65,.49,.12,.23),(-1.28,.70,.16,.33),(-.55,.75,.22,.42),(.45,.66,.30,.44),(1.15,.44,.37,.30),(1.65,.07,.34,.12)],'Hull')
hull('Turquoise deck',[(-1.56,.47,.23,.10),(-1.20,.66,.30,.12),(-.35,.65,.40,.14),(.55,.57,.51,.18),(1.18,.36,.52,.17),(1.58,.06,.38,.09)],'Turquoise')
for side in [-1,1]:
    box('Rear orange side panel',(side*.47,-1.12,.33),(.23,.62,.055),'Orange').rotation_euler[2]=side*-.13
    box('Rubber footwell',(side*.48,-.35,.40),(.23,1.12,.055),'Seat')
    box('Side silver accent',(side*.67,-.54,.22),(.035,.88,.06),'Silver')
    box('Rear grab handle',(side*.38,-1.43,.36),(.07,.25,.07),'Seat')
hull('Raised padded saddle',[(-1.14,.23,.48,.13),(-.8,.28,.56,.15),(-.15,.24,.64,.16),(.30,.17,.67,.18)],'Seat')
box('Orange rear deck stripe',(0,-1.39,.29),(.30,.36,.045),'Orange')
box('Front orange stripe',(0,1.05,.55),(.22,.55,.035),'Orange').rotation_euler[0]=-.13
limb('Steering column',(0,.42,.48),(0,.57,.92),.07,.065,'Hull')
limb('Handlebar',(-.4,.57,.92),(.4,.57,.92),.035,.035,'Silver')
for side in [-1,1]: limb('Grip',(side*.27,.57,.92),(side*.44,.57,.92),.049,.049,'Seat')
ell('Instrument console',(0,.67,.83),(.19,.20,.08),'Hull')
box('Console screen',(0,.68,.90),(.13,.12,.02),'Silver')
limb('Jet outlet',(0,-1.56,-.05),(0,-1.78,-.05),.14,.12,'Silver')
box('Fitted shorts waist',(0,-.49,.91),(.60,.36,.23),'Seat',.065)
for s in [-1,1]:
    box('Shorts leg cuff',(s*.28,-.34,.79),(.23,.20,.13),'Seat',.045)
    limb('Bare thigh',(s*.35,-.31,.74),(s*.48,-.04,.64),.14,.12,'Skin')
    ell('Rounded knee',(s*.48,-.04,.64),(.12,.12,.12),'Skin')
    limb('Shin',(s*.48,-.04,.64),(s*.47,-.60,.42),.105,.075,'Skin')
    ell('Water shoe',(s*.47,-.66,.43),(.105,.21,.075),'Hull')
ell('Torso',(0,-.25,1.26),(.31,.23,.43),'Seat')
o=box('Orange life vest back',(0,-.43,1.31),(.48,.16,.52),'Orange',.075); o.rotation_euler[0]=-.22
box('Lower vest belt',(0,-.48,1.12),(.48,.065,.055),'Seat',.02)
box('Back reflective strip',(0,-.525,1.46),(.28,.012,.025),'Silver',.009)
for s in [-1,1]:
    box('Vest shoulder strap',(s*.22,-.25,1.62),(.12,.32,.075),'Orange',.025)
    limb('Upper arm',(s*.30,-.15,1.47),(s*.49,.08,1.20),.115,.095,'Skin')
    ell('Shoulder',(s*.30,-.15,1.47),(.115,.115,.115),'Skin')
    ell('Elbow',(s*.49,.08,1.20),(.096,.096,.096),'Skin')
    limb('Forearm',(s*.49,.08,1.20),(s*.37,.56,.94),.09,.065,'Skin')
    ell('Gloved hand',(s*.37,.57,.94),(.078,.095,.065),'Hull')
limb('Neck',(0,-.06,1.55),(0,.02,1.72),.105,.10,'Skin')
ell('Head',(0,.04,1.86),(.205,.19,.25),'Skin')
ell('Close-cropped hair base',(0,.005,2.01),(.21,.193,.075),'Hair')
for x,tip_x in [(-.14,-.19),(-.05,-.09),(.05,.03),(.14,.16)]:
    curve=bpy.data.curves.new('Swept hair','CURVE'); curve.dimensions='3D'; curve.resolution_u=16
    curve.bevel_depth=.07; curve.bevel_resolution=3
    spline=curve.splines.new('BEZIER'); spline.bezier_points.add(2)
    for point,co,radius in zip(spline.bezier_points,[(x,-.12,2.03),(x,-.055,2.115),(tip_x,.095,2.085)],[1.0,.9,.20]):
        point.co=co; point.handle_left_type='AUTO'; point.handle_right_type='AUTO'; point.radius=radius
    hair=bpy.data.objects.new('Swept hair section',curve); bpy.context.collection.objects.link(hair); hair.data.materials.append(mats['Hair'])
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active=hair; hair.select_set(True); bpy.ops.object.convert(target='MESH'); hair.select_set(False)
for s in [-1,1]: ell('Ear',(s*.195,.035,1.85),(.045,.06,.075),'Skin')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.wm.save_as_mainfile(filepath=ROOT+r'\Assets\BlenderSource\JetskiRider.blend')
bpy.ops.export_scene.fbx(filepath=ROOT+r'\Assets\Models\JetskiRider.fbx',use_selection=True,axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
# A rear three-quarter inspection render, using the actual mesh.
bpy.ops.object.camera_add(location=(3,-6,4)); cam=bpy.context.object; cam.rotation_euler=(Vector((0,0,.85))-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.type='ORTHO'; cam.data.ortho_scale=5.1; bpy.context.scene.camera=cam
for pos,power,size in [((1,-3,6),1100,5),((-4,1,3),900,4)]:
    bpy.ops.object.light_add(type='AREA',location=pos); light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; light.rotation_euler=(Vector((0,0,.6))-light.location).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'; scene.world.color=(.15,.15,.15); scene.render.resolution_x=900; scene.render.resolution_y=900; scene.render.resolution_percentage=100; scene.render.filepath=ROOT+r'\Build\JetskiRiderPreview.png'; bpy.ops.render.render(write_still=True)
