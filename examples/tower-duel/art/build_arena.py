"""Build a standalone KayKit arena; run using Blender --background --python.
Source pack subset: examples/tower-duel/art/source (CC0, original geometry).
Outputs: artifacts/kaykit-arena. Does not modify existing games.
"""
import bpy, math, json, shutil, random, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).resolve().parent/'source'
OUT = ROOT/'artifacts/kaykit-arena'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.name='TwinbridgeArena'
scene.unit_settings.system='METRIC'
collections={}
for name in ['Layout','Buildings','Nature','Lights','Cameras','Collision']:
    c=bpy.data.collections.new('COL_'+name); scene.collection.children.link(c); collections[name]=c

def move(o,group):
    for c in list(o.users_collection): c.objects.unlink(o)
    collections[group].objects.link(o)

def mat(name,color):
    m=bpy.data.materials.new('MAT_'+name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=.85
    return m

grass=mat('Grass',(.24,.48,.16)); stone=mat('Foundation',(.17,.23,.27))
sand=mat('Path',(.65,.52,.30)); water=mat('Water',(.035,.42,.62)); bank=mat('Bank',(.38,.43,.34))
blue=mat('Blue',(.045,.31,.70)); red=mat('Red',(.70,.09,.075)); foam=mat('WaterHighlight',(.17,.67,.77))
atlas=mat('KayKit',(.8,.8,.8))
texture=SOURCE/'Assets/fbx/buildings/blue/hexagons_medieval.png'
shutil.copy2(texture,OUT/'hexagons_medieval.png')
im=bpy.data.images.load(str(OUT/'hexagons_medieval.png'))
node=atlas.node_tree.nodes.new('ShaderNodeTexImage'); node.image=im
atlas.node_tree.links.new(node.outputs['Color'],atlas.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])

def cube(name,loc,size,material,bevel=0,group='Layout'):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object; o.name='SM_'+name; o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(material); move(o,group)
    if bevel:
        mod=o.modifiers.new('Soft edges','BEVEL'); mod.width=bevel; mod.segments=1
        bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
    return o

cache={}
def asset(rel,name,loc,scale=1,rot=0,group='Buildings'):
    if rel not in cache:
        before=set(bpy.data.objects); bpy.ops.import_scene.fbx(filepath=str(SOURCE/'Assets/fbx'/rel))
        objs=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
        # Flatten imported hierarchy without changing world geometry.
        for o in objs:
            matrix=o.matrix_world.copy(); o.parent=None; o.matrix_world=matrix
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs: o.select_set(True)
        bpy.context.view_layer.objects.active=objs[0]
        if len(objs)>1: bpy.ops.object.join()
        template=bpy.context.object
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        template.data.materials.clear(); template.data.materials.append(atlas)
        for p in template.data.polygons: p.material_index=0
        cache[rel]=template.data
        bpy.data.objects.remove(template,do_unlink=True)
    o=bpy.data.objects.new('SM_'+name,cache[rel]); collections[group].objects.link(o)
    o.location=loc; o.scale=(scale,)*3 if isinstance(scale,(int,float)) else scale
    o.rotation_euler.z=rot
    return o

# A 20 x 30 metre symmetric playing field. Blender Z is up.
cube('ArenaPlinth',(0,0,-.95),(22,32,1.3),stone,.5)
for side in [-1,1]:
    cube('GrassBank_'+str(side),(0,side*8.25,-.25),(20,13.5,.5),grass,.14)
cube('River',(0,0,-.25),(20,3,.15),water,.04)
for side in [-1,1]:
    for x,w in [(-8.5,3), (0,7), (8.5,3)]:
        cube('Riverbank',(x,side*1.48,-.12),(w,.3,.45),bank,.06)
    team='blue' if side<0 else 'red'; tint=blue if side<0 else red
    cube(team+'_KeepPad',(0,side*11,.045),(4.4,4.2,.09),sand,.2)
    asset(f'buildings/{team}/building_castle_{team}.fbx',team+'_MainCastle',(0,side*11,.09),1.25,math.pi if side>0 else 0)
    for lane in [-5,5]:
        cube(team+'_Lane',(lane,side*6.4,.025),(2.5,9.8,.05),sand,.08)
        cube(team+'_TowerPad',(lane,side*7.7,.07),(3.4,3.4,.14),bank,.15)
        cube(team+'_TeamMarker',(lane,side*7.7,.15),(3.05,3.05,.035),tint,.12)
        asset(f'buildings/{team}/building_tower_A_{team}.fbx',team+'_Tower_'+str(lane),(lane,side*7.7,.18),1.45,math.pi if side>0 else 0)
        # Connection from central keep to each lane.
        cube(team+'_RearPath',(lane/2,side*10.4,.03),(5,1.3,.06),sand,.08)
for lane in [-5,5]:
    asset('buildings/neutral/building_bridge_A.fbx','Bridge_'+str(lane),(lane,0,-.72),(2.0,2.1,.65))
    # Flat walkable deck, independent of decorative bridge arch.
    cube('BridgeDeck_'+str(lane),(lane,0,.08),(2.5,3.3,.16),sand,.04)
    for edge in [-1,1]:
        cube('BridgeRail',(lane+edge*1.23,0,.43),(.14,3.5,.18),bank,.03)
        for y in [-1.55,1.55]: cube('BridgePost',(lane+edge*1.23,y,.3),(.25,.25,.65),bank,.035)
random.seed(38)
for side in [-1,1]:
    for edge in [-1,1]:
        for i in range(8):
            x=edge*(8.3+random.uniform(-.25,.25)); y=side*(2.7+i*1.5)
            asset('decoration/nature/tree_single_A.fbx',f'Tree_{side}_{edge}_{i}',(x,y,0),random.uniform(1.25,1.9),random.random()*6.28,'Nature')
        for i in range(3):
            asset('decoration/nature/rock_single_A.fbx',f'Rock_{side}_{edge}_{i}',(edge*9.1,side*(4+i*3.5),0),1.2,0,'Nature')
for i in range(14):
    x=-9+i*1.35
    if abs(abs(x)-5)>1.5:
        cube('RiverRipple',(x,random.uniform(-.8,.8),-.16),(.55,.045,.012),foam,.005)

# Simple collider proxies, disabled in the art export/render.
for side in [-1,1]: cube('COL_Bank_'+str(side),(0,side*8.25,-.25),(20,13.5,.5),grass,group='Collision')
for lane in [-5,5]: cube('COL_Bridge_'+str(lane),(lane,0,.04),(2.5,3.3,.08),sand,group='Collision')
collections['Collision'].hide_render=True
collections['Collision'].hide_viewport=True

world=bpy.data.worlds.new('World_Arena'); scene.world=world; world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.09,.14,.21,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.5
bpy.ops.object.light_add(type='AREA',location=(-10,-8,22)); light=bpy.context.object; light.name='Key_Softbox'; light.data.energy=2600; light.data.shape='DISK'; light.data.size=15; move(light,'Lights')
bpy.ops.object.light_add(type='SUN',location=(0,0,12)); light=bpy.context.object; light.name='Sun'; light.rotation_euler=(.35,-.4,-.3); light.data.energy=2; light.data.angle=.12; move(light,'Lights')
bpy.ops.object.camera_add(location=(24,-32,38)); cam=bpy.context.object; cam.name='Camera_Presentation'; cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.type='ORTHO'; cam.data.ortho_scale=43; scene.camera=cam; move(cam,'Cameras')
scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
scene.render.resolution_x=1400; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
bpy.context.view_layer.update()
meshes=[o for key in ['Layout','Buildings','Nature'] for o in collections[key].objects if o.type=='MESH']
rows=[]
for o in meshes:
    o.data.calc_loop_triangles()
    rows.append({'name':o.name,'triangles':len(o.data.loop_triangles),'materials':len(o.data.materials),'uv':len(o.data.uv_layers)})
total=sum(r['triangles'] for r in rows)
assert total < 100000, total
report={'triangles':total,'mesh_objects':len(meshes),'unique_meshes':len(set(o.data for o in meshes)),'material_count':len(set(m for o in meshes for m in o.data.materials)),'budget':100000,'decimation':'Not needed: measured below prototype budget. Linked geometry reused.','limitations':['Art prototype; gameplay, navigation and device performance not validated.','Native KayKit UV atlas preserved; procedural terrain uses solid materials.'],'objects':rows}
(OUT/'audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
(OUT/'layout.json').write_text(json.dumps({'units':'metres','unity_ground':'XZ','field_width':20,'field_length':30,'river_half_width':1.5,'bridge_centres_x':[-5,5],'bridge_width':2.5,'blue_keep':[0,-11],'red_keep':[0,11],'tower_positions':[[-5,-7.7],[5,-7.7],[-5,7.7],[5,7.7]]},indent=2),encoding='utf-8')
shutil.copy2(SOURCE/'License.txt',OUT/'KayKit-LICENSE.txt')
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active=meshes[0]
# FBX exporter needs independent mesh material tables; retain linked data in blend.
original_meshes={o:o.data for o in meshes}
for o in meshes: o.data=o.data.copy()
bpy.ops.export_scene.fbx(filepath=str(OUT/'TwinbridgeArena.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,path_mode='COPY',embed_textures=False)
for o,mesh in original_meshes.items():
    exported=o.data; o.data=mesh; bpy.data.meshes.remove(exported)
im.pack()
im.filepath='//hexagons_medieval.png'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'TwinbridgeArena.blend'))
if '--skip-render' not in sys.argv:
    scene.render.filepath=str(OUT/'arena-preview.png'); bpy.ops.render.render(write_still=True)
    cam.location=(0,-26,38); cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.ortho_scale=37
    scene.render.filepath=str(OUT/'arena-gameplay-view.png'); bpy.ops.render.render(write_still=True)
print('ARENA_COMPLETE '+str(total))
