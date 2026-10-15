from pathlib import Path
from pubg_pak import Pak, ROOT, WORK
from PIL import Image, ImageDraw, ImageFont
import struct, re, io, json, hashlib

from paths import BUNDLE
OUT=BUNDLE/'assets'/'game'
OUT.mkdir(parents=True,exist_ok=True)

def texture(exp):
    m=re.search(rb'PF_(BC7|DXT1|DXT5|B8G8R8A8|G8)\x00',exp)
    if not m: raise ValueError('Unsupported pixel format')
    fmt=m.group()[:-1].decode(); end=m.end()
    first,count,cooked=struct.unpack_from('<iii',exp,end)
    if not 1<=count<=16 or cooked!=1: raise ValueError(('invalid cooked mips',first,count,cooked))
    flags,elements,size,offset=struct.unpack_from('<Iiiq',exp,end+12)
    pos=end+32
    if pos+size+8>len(exp): raise ValueError(('external or invalid payload',flags,size,pos,len(exp)))
    pixels=exp[pos:pos+size]
    w,h=struct.unpack_from('<ii',exp,pos+size)
    if not 1<=w<=4096 or not 1<=h<=4096: raise ValueError(('bad dimensions',w,h))
    if fmt=='PF_B8G8R8A8': image=Image.frombytes('RGBA',(w,h),pixels,'raw','BGRA')
    elif fmt=='PF_G8': image=Image.frombytes('L',(w,h),pixels).convert('RGBA')
    else:
        fourcc=b'DX10' if fmt=='PF_BC7' else fmt[3:].encode()
        header=struct.pack('<7I',124,0x81007,h,w,size,0,1)+b'\0'*44
        header+=struct.pack('<2I4s5I',32,4,fourcc,0,0,0,0,0)
        header+=struct.pack('<5I',0x1000,0,0,0,0)
        if fmt=='PF_BC7': header+=struct.pack('<5I',98,3,0,1,0)
        image=Image.open(io.BytesIO(b'DDS '+header+pixels)).convert('RGBA')
    return image,fmt

manifest=[]
for path in sorted(ROOT.glob('*.pak')):
    if '_ui' not in path.name: continue
    pak=Pak(path)
    for name in pak.files:
        logical=(pak.mount+name).replace('../../../TslGame/Content/','')
        if not name.endswith('.uexp') or not logical.startswith(('UI/RadioMessage/','UI/Emoji/')): continue
        try:
            raw=pak.extract(name); image,fmt=texture(raw)
            prefix='world_' if '/WorldPing/' in name else ''
            filename=prefix+Path(name).stem+'.png'
            image.save(OUT/filename)
            manifest=[r for r in manifest if r['file']!=filename]
            manifest.append(dict(file=filename,source_pak=pak.path.name,source_path=pak.mount+name,
                                 width=image.width,height=image.height,pixel_format=fmt,
                                 sha256=hashlib.sha256((OUT/filename).read_bytes()).hexdigest()))
        except Exception as e: print('SKIP',name,str(e))
(OUT/'texture-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print('Decoded original textures:',len(manifest))

large=[row for row in manifest if row['width']>=200]
cols=8; cellw=180; cellh=170
sheet=Image.new('RGB',(cols*cellw,80+((len(large)+cols-1)//cols)*cellh),(23,27,33))
draw=ImageDraw.Draw(sheet)
font=ImageFont.truetype(r'C:\Windows\Fonts\segoeui.ttf',12)
big=ImageFont.truetype(r'C:\Windows\Fonts\msyh.ttc',22)
draw.text((24,18),'从本机 PUBG 提取的原始图标 · 非重绘',font=big,fill='#EDC05B')
for i,row in enumerate(large):
    x=(i%cols)*cellw; y=80+(i//cols)*cellh
    icon=Image.open(OUT/row['file']).convert('RGBA'); icon.thumbnail((96,100))
    sheet.paste(icon,(x+(cellw-icon.width)//2,y+8),icon)
    label=row['file'].replace('T_RadioMessage_','').replace('T_Emoji_','Emoji_').replace('_240.png','')
    draw.text((x+8,y+118),label[:25],font=font,fill='white')
    draw.text((x+8,y+137),str(row['width'])+'px / '+row['pixel_format'],font=font,fill='#8B97A7')
target=OUT.parents[1]/'original-assets-contact.png';sheet.save(target)
print('Gallery:',target)
