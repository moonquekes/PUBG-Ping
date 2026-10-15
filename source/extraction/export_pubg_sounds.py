from pathlib import Path
import array, struct, json, zipfile, subprocess, wave, hashlib
from pubg_pak import WORK

def playback_wav(path):
    """Windows SoundPlayer mishandles 48 kHz and drops sounds shorter than its device buffer.
    Resample to 44.1 kHz mono, raise the quiet Wwise source to a normal UI peak, and pad short ticks."""
    target_rate, target_peak, pad_seconds = 44100, 22000, 0.12
    with wave.open(str(path)) as src:
        if src.getsampwidth() != 2: raise ValueError('Expected 16-bit WAV from vgmstream')
        rate, channels = src.getframerate(), src.getnchannels()
        pcm = array.array('h'); pcm.frombytes(src.readframes(src.getnframes()))
    if channels == 2: samples = array.array('h', ((pcm[i] + pcm[i + 1]) // 2 for i in range(0, len(pcm), 2)))
    elif channels == 1: samples = pcm
    else: raise ValueError('Unsupported channel count')
    if rate != target_rate and samples:
        out_len = int(round(len(samples) * target_rate / rate))
        out = array.array('h', [0]) * out_len
        scale, last = rate / target_rate, len(samples) - 1
        for i in range(out_len):
            pos = i * scale; left = int(pos); frac = pos - left
            a, b = samples[left], samples[min(left + 1, last)]
            out[i] = int(a + (b - a) * frac)
        samples = out
    peak = max((abs(s) for s in samples), default=1) or 1
    gain = target_peak / peak
    scaled = array.array('h')
    for s in samples:
        v = int(s * gain)
        scaled.append(32767 if v > 32767 else -32768 if v < -32768 else v)
    if len(scaled) / target_rate < 0.18: scaled.extend([0] * int(target_rate * pad_seconds))
    with wave.open(str(path), 'wb') as dst:
        dst.setnchannels(1); dst.setsampwidth(2); dst.setframerate(target_rate); dst.writeframes(scaled.tobytes())

def chunks(data):
    p=0
    while p+8<=len(data):
        tag,size=struct.unpack_from('<4sI',data,p)
        yield tag,data[p+8:p+8+size]
        p+=8+size

def hash_name(name):
    n=2166136261
    for c in name.lower().encode():n=((n*16777619)^c)&0xffffffff
    return n

data=(WORK/'raw'/'UI_SoundBank.bnk').read_bytes()
objects={}; media={}
sections=dict(chunks(data))
blob=sections[b'DATA']
for offset in range(0,len(sections[b'DIDX']),12):
    ident,start,size=struct.unpack_from('<III',sections[b'DIDX'],offset)
    media[ident]=blob[start:start+size]
body=sections[b'HIRC'];count=struct.unpack_from('<I',body)[0];pos=4
for _ in range(count):
    kind,size,ident=struct.unpack_from('<BII',body,pos)
    objects[ident]=(kind,body[pos+9:pos+5+size]);pos+=5+size

def trace(ident,seen):
    if ident in seen or ident not in objects:return []
    seen.add(ident);kind,b=objects[ident]
    if kind==2:
        return [struct.unpack_from('<I',b,5)[0]]
    if kind==3:
        return trace(struct.unpack_from('<I',b,2)[0],seen)
    if kind in (4,5,6,9):
        result=[]
        for offset in range(len(b)-3):
            child=struct.unpack_from('<I',b,offset)[0]
            if child in objects and objects[child][0] in (2,3,4,5,6,9):result.extend(trace(child,seen))
        return list(dict.fromkeys(result))
    return []

tools=WORK/'vgmstream'
if not tools.exists():
    tools.mkdir()
    with zipfile.ZipFile(WORK/'vgmstream.zip') as z:
        # Archive is from vgmstream's official GitHub release. Reject path traversal.
        for item in z.infolist():
            target=(tools/item.filename).resolve()
            if not target.is_relative_to(tools.resolve()):raise ValueError('Invalid archive path')
        z.extractall(tools)
cli=next(tools.rglob('vgmstream-cli.exe'))
from paths import BUNDLE
out=BUNDLE/'assets'/'game'/'sounds'
out.mkdir(parents=True,exist_ok=True)
scratch=WORK/'radio-wem';scratch.mkdir(exist_ok=True)
manifest=[]
for category in ['Normal','Important','Notify','Warning']:
    event='UI_RadioMessage_'+category
    ids=trace(hash_name(event),set())
    print(event,'media IDs:',ids)
    for index,ident in enumerate(ids):
        if ident not in media:raise ValueError(('External media needs archive extraction',ident))
        wem=scratch/(str(ident)+'.wem');wem.write_bytes(media[ident])
        target=out/(category.lower()+'_'+str(index+1)+'.wav')
        r=subprocess.run([str(cli),'-o',str(target),str(wem)],capture_output=True,text=True,errors='replace')
        if r.returncode:raise RuntimeError(r.stdout+r.stderr)
        playback_wav(target)
        with wave.open(str(target),'rb') as w:
            duration=w.getnframes()/w.getframerate();rate=w.getframerate();channels=w.getnchannels()
        manifest.append(dict(file=target.name,event=event,event_id=hash_name(event),media_id=ident,
                             source_pak='pakchunk0-WindowsNoEditor_sound.pak',
                             source_bank='WwiseAudio/Win64/UI_SoundBank.bnk',duration=duration,
                             sample_rate=rate,channels=channels,sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
        print(' decoded',target.name,duration,'s')
(out/'sound-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
