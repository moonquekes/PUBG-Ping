"""Small tagged-property reader for the inspected local UE4 assets."""
import struct, json
from pathlib import Path

def names_map(data):
    for header in range(20,min(220,len(data)-8)):
        count,offset=struct.unpack_from('<ii',data,header)
        if not (5<count<2000 and 60<offset<len(data)-20): continue
        for hashes in (4,0):
            pos=offset; names=[]
            try:
                for _ in range(count):
                    n=struct.unpack_from('<i',data,pos)[0];pos+=4
                    if not 1<=n<=500: raise ValueError()
                    raw=data[pos:pos+n]
                    if raw[-1]!=0 or any(c<32 or c>126 for c in raw[:-1]): raise ValueError()
                    names.append(raw[:-1].decode()); pos+=n+hashes
                if 'None' in names: return names
            except (ValueError,IndexError,struct.error): pass
    raise ValueError('Name map not found')

class Props:
    def __init__(self,data,names,pos=0): self.data=data; self.names=names;self.pos=pos
    def read(self,fmt):
        value=struct.unpack_from('<'+fmt,self.data,self.pos);self.pos+=struct.calcsize('<'+fmt)
        return value[0] if len(value)==1 else value
    def name(self):
        idx,number=self.read('ii')
        if not 0<=idx<len(self.names) or not 0<=number<100000: raise ValueError(('Bad FName',idx,number,self.pos))
        return self.names[idx]+('_'+str(number-1) if number else '')
    def properties(self):
        values={}
        while self.pos+8<=len(self.data):
            at=self.pos; name=self.name()
            if name=='None': return values
            kind=self.name();size,index=self.read('ii')
            if size<0 or size>len(self.data): raise ValueError(('Invalid property',name,kind,size,at))
            extra=None
            if kind in ('ByteProperty','EnumProperty','ArrayProperty'): extra=self.name()
            elif kind=='StructProperty': extra=self.name();self.pos+=16
            elif kind=='BoolProperty': extra=self.read('B')
            elif kind=='MapProperty': extra=(self.name(),self.name())
            has_guid=self.read('B')
            if has_guid not in (0,1): raise ValueError(('Invalid property GUID flag',name,kind,has_guid,at))
            if has_guid:self.pos+=16
            raw=self.data[self.pos:self.pos+size]; self.pos+=size
            value=raw.hex()
            if kind=='BoolProperty': value=bool(extra)
            elif kind in ('EnumProperty','ByteProperty') and len(raw)==8:
                i,num=struct.unpack('<ii',raw);value=self.names[i] if 0<=i<len(self.names) else value
            elif kind=='IntProperty' and len(raw)==4:value=struct.unpack('<i',raw)[0]
            elif kind=='FloatProperty' and len(raw)==4:value=struct.unpack('<f',raw)[0]
            elif kind=='ObjectProperty' and len(raw)==4:value={'import_index':struct.unpack('<i',raw)[0]}
            else:
                import re
                strings=[m.group().decode() for m in re.finditer(rb'[ -~]{4,}',raw)]
                if strings:value={'strings':strings,'raw':raw.hex()}
            values[name]={'type':kind,'value':value,'extra':extra}
        raise ValueError('Missing None terminator')

if __name__=='__main__':
    from paths import WORK
    root=WORK/'raw'
    for base in ['DT_RadioMessage_World','DT_MarkerDefinition','DT_RadioMessageAlarmSoundCategory']:
        names=names_map((root/(base+'.uasset')).read_bytes());data=(root/(base+'.uexp')).read_bytes()
        p=Props(data,names);head=p.properties(); print(base,'header',head,'next',data[p.pos:p.pos+24].hex(' '))
        # UObject optional GUID flag precedes UDataTable's row count in these cooked assets.
        guid_flag=p.read('i')
        if guid_flag:p.pos+=16
        count=p.read('i');rows={}
        try:
            for _ in range(count):
                name=p.name();rows[name]=p.properties()
            print('rows',len(rows),'remaining',len(data)-p.pos)
        except Exception as e: print('READ ERROR',p.pos,str(e))
        (root/(base+'.json')).write_text(json.dumps({'names':names,'header':head,'row_count':count,'rows':rows},indent=2),encoding='utf-8')
