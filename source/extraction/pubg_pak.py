"""Read-only UE4 v4 Pak reader for the user's local asset study.

Parses structures documented by CUE4Parse (Apache-2.0).
Game files are never opened for writing. The validated local key is kept in work/.
"""
from pathlib import Path
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
import struct, hashlib, json, zlib, gzip, ctypes, zipfile

from paths import ROOT, WORK
KEY=bytes.fromhex((WORK/'local-pak-key.hex').read_text().strip())
OODLE=None

def oodle(data,size):
    global OODLE
    if OODLE is None:
        dll=WORK/'oodle-data-shared.dll'
        if not dll.exists():
            with zipfile.ZipFile(WORK/'oodle.zip') as z:
                dll.write_bytes(z.read('bin/oodle-data-shared.dll'))
        OODLE=ctypes.CDLL(str(dll.resolve())).OodleLZ_Decompress
        OODLE.restype=ctypes.c_ssize_t
        OODLE.argtypes=[ctypes.c_void_p,ctypes.c_ssize_t,ctypes.c_void_p,ctypes.c_ssize_t,
                       ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_void_p,ctypes.c_ssize_t,
                       ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_ssize_t,ctypes.c_int]
    src=ctypes.create_string_buffer(data); dst=ctypes.create_string_buffer(size)
    got=OODLE(src,len(data),dst,size,1,0,0,None,0,None,None,None,0,3)
    if got!=size: raise ValueError(('Oodle size mismatch',got,size))
    return dst.raw

def decrypt(data):
    ctx=Cipher(algorithms.AES(KEY),modes.ECB()).decryptor()
    return ctx.update(data)+ctx.finalize()

class Reader:
    def __init__(self,data): self.data=data; self.pos=0
    def read(self,fmt):
        value=struct.unpack_from('<'+fmt,self.data,self.pos)
        self.pos+=struct.calcsize('<'+fmt)
        return value[0] if len(value)==1 else value
    def string(self):
        size=self.read('i')
        if not -10000<size<10000: raise ValueError(('Invalid FString',size,self.pos))
        raw=self.data[self.pos:self.pos+abs(size)*(2 if size<0 else 1)]
        self.pos+=len(raw)
        return raw.decode('utf-16le' if size<0 else 'utf-8').rstrip('\0')
    def entry(self):
        start=self.pos
        offset,size,usize,method=self.read('qqqI')
        sha=self.data[self.pos:self.pos+20].hex(); self.pos+=20
        blocks=[]
        if method:
            count=self.read('i')
            if not 0<=count<100000: raise ValueError(('Invalid block count',count))
            blocks=[self.read('qq') for _ in range(count)]
        flags,blocksize=self.read('BI')
        return dict(offset=offset,size=size,usize=usize,method=method,sha1=sha,
                    blocks=blocks,flags=flags,blocksize=blocksize,header_size=self.pos-start)

class Pak:
    def __init__(self,path):
        self.path=Path(path)
        with self.path.open('rb') as f:
            f.seek(-512,2); tail=f.read()
            at=tail.rfind(struct.pack('<I',0x5A6F12E1))
            self.version,indexoff,indexsize=struct.unpack_from('<iqq',tail,at+4)
            expected=tail[at+24:at+44]
            f.seek(indexoff); data=f.read(indexsize)
        if tail[at-1]: data=decrypt(data)
        if hashlib.sha1(data).digest()!=expected: raise ValueError('Index hash mismatch: '+self.path.name)
        r=Reader(data); self.mount=r.string(); count=r.read('i')
        if not 0<=count<100000: raise ValueError(('Invalid entry count',count))
        self.files={}
        for _ in range(count):
            name=r.string(); entry=r.entry(); self.files[name]=entry
    def extract(self,name):
        e=self.files[name]; parts=[]
        with self.path.open('rb') as f:
            if e['method']==0:
                f.seek(e['offset']+e['header_size'])
                size=(e['size']+15)//16*16 if e['flags']&1 else e['size']
                data=f.read(size)
                if e['flags']&1: data=decrypt(data)
                parts=[data[:e['usize']]]
            else:
                for start,end in e['blocks']:
                    size=end-start
                    f.seek(start)
                    raw=f.read((size+15)//16*16 if e['flags']&1 else size)
                    if e['flags']&1: raw=decrypt(raw)
                    raw=raw[:size]
                    if e['method']&1: data=zlib.decompress(raw)
                    elif e['method']&2: data=gzip.decompress(raw)
                    else: data=oodle(raw,min(e['blocksize'],e['usize']-sum(len(p) for p in parts)))
                    parts.append(data)
        result=b''.join(parts)
        if len(result)!=e['usize']: raise ValueError(('size mismatch',name,len(result),e['usize']))
        return result

if __name__=='__main__':
    import re
    terms=re.compile(r'ping|radio|marker|radial|communication|quickchat|wheel',re.I)
    summary=[]
    for path in sorted(ROOT.glob('*.pak')):
        if not any(s in path.name for s in ['_ui','_sound','_config','_localize']): continue
        p=Pak(path)
        rows=[dict(name=name,**entry) for name,entry in p.files.items()]
        (WORK/(path.stem+'-index.json')).write_text(json.dumps({'mount':p.mount,'files':rows},indent=2),encoding='utf-8')
        matches=[r for r in rows if terms.search(r['name'])]
        summary.append({'pak':path.name,'mount':p.mount,'files':len(rows),'matches':len(matches),'methods':sorted(set(e['method'] for e in p.files.values()))})
        if matches:
            print('\n',path.name,p.mount,'files',len(rows),'matches',len(matches),'methods',summary[-1]['methods'])
            for m in matches[:90]: print(m['name'],m['usize'])
    (WORK/'pak-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
