from pathlib import Path
from pubg_pak import Reader, WORK
from pubg_uasset import Props,names_map
import json,struct

root=WORK/'raw'
locdata=(root/'zh-CN-TSL_UI.locres').read_bytes()
r=Reader(locdata);r.pos=16;version=r.read('B');strings_at=r.read('q')
if version!=1:raise ValueError(('Unsupported locres version',version))
saved=r.pos;r.pos=strings_at
count=r.read('i');strings=[r.string() for _ in range(count)];r.pos=saved
local={}
for _ in range(r.read('I')):
    namespace=r.string();values={}
    for _ in range(r.read('I')):
        key=r.string();sourcehash,index=r.read('Ii');values[key]=strings[index]
    local[namespace]=values
print('Localization namespaces',len(local),'radio namespaces',[n for n in local if 'Radio' in n or 'EMOJI' in n])

base={}
for name in ['ST_RadioMessage','ST_EMOJI']:
    names=names_map((root/(name+'.uasset')).read_bytes());data=(root/(name+'.uexp')).read_bytes()
    p=Props(data,names);p.properties();guid=p.read('i')
    if guid:p.pos+=16
    r=Reader(data);r.pos=p.pos;namespace=r.string();entries={}
    for _ in range(r.read('i')):
        key=r.string();text=r.string();entries[key]=text
    base[name]=entries
    print(name,'namespace',namespace,'entries',len(entries))

world=json.loads((root/'DT_RadioMessage_World.json').read_text())
rows=[]
for name, props in world['rows'].items():
    row={'row':name}
    for key,prop in props.items():
        value=prop['value']
        if prop['type']=='NameProperty':
            index,number=struct.unpack('<ii',bytes.fromhex(value));value=world['names'][index]
        elif isinstance(value,dict):value=value.get('strings')
        row[key]=value
    rows.append(row)
markers=json.loads((root/'DT_MarkerDefinition.json').read_text())['rows']
from paths import BUNDLE
textures=BUNDLE/'assets'/'game'
def localized(table,key):
    for namespace,entries in local.items():
        if table in namespace and key in entries:return entries[key]
    return base[table].get(key,key)

catalog=[]
for row in rows:
    if not any(row['row']==prefix+str(i) for prefix in ['Combat_Slot','Loot_Slot','Emoji_Slot'] for i in range(1,9)): continue
    table='ST_EMOJI' if row['row'].startswith('Emoji_') else 'ST_RadioMessage'
    label=localized(table,row['SlotDisplayName'][0])
    texture=Path(row['SlotIcon'][0].split('.')[0]).name+'.png'
    world=''
    marker=markers.get(row['MarkerDefinitionKey'])
    if marker:
        v=marker['WorldIconTexture']['value']
        if isinstance(v,dict) and v.get('strings'):world='world_'+Path(v['strings'][0].split('.')[0]).name+'.png'
    if not (textures/texture).exists():raise FileNotFoundError(texture)
    if world and not (textures/world).exists():raise FileNotFoundError(world)
    category=row['AlarmSoundCategory'].split('::')[-1].lower()
    item=dict(Id=row['row'],Label=label,Page=row['PageNumber']-1,Slot=row['SlotPosition']-1,
              Texture=texture,WorldTexture=world,SoundKind=category,
              Color='#F06459' if category=='warning' else '#EDC05B',
              PageTitle=localized('ST_RadioMessage',row['PageDisplayName'][0]),
              SourceMessageKey=row['MessageKey'][0])
    catalog.append(item)
catalog.sort(key=lambda r:(r['Page'],r['Slot']))
(textures/'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
for item in catalog:print(item['Page']+1,item['Slot']+1,item['Label'],item['Texture'],item['SoundKind'])
