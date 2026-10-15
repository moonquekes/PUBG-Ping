"""Validate a local static ASCII key candidate against an archive's full SHA-1.
Never executes the game or accesses a running process; no network requests.
"""
from pathlib import Path
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
from paths import WORK, ROOT
import struct,re,hashlib,json

pak=ROOT/'pakchunk0-WindowsNoEditor_ui_hud.pak'
with pak.open('rb') as f:
    f.seek(-512,2);tail=f.read();at=tail.rfind(struct.pack('<I',0x5A6F12E1))
    if at<1:raise ValueError('Unsupported pak footer')
    version,offset,size=struct.unpack_from('<iqq',tail,at+4)
    if version!=4 or size>16*1024*1024:raise ValueError('Unsupported archive version/size')
    expected=tail[at+24:at+44];f.seek(offset);encrypted=f.read(size)

def valid(key):
    ctx=Cipher(algorithms.AES(key),modes.ECB()).decryptor()
    first=ctx.update(encrypted[:32]);length=struct.unpack_from('<i',first)[0]
    if not 1<=length<=256 or first[4:7]!=b'../':return False
    ctx=Cipher(algorithms.AES(key),modes.ECB()).decryptor()
    return hashlib.sha1(ctx.update(encrypted)+ctx.finalize()).digest()==expected

keyfile=WORK/'local-pak-key.hex'
if keyfile.exists() and valid(bytes.fromhex(keyfile.read_text().strip())):
    print('Cached local key validated against the full archive index SHA-1.')
else:
    exe=ROOT.parent.parent/'Binaries'/'Win64'/'TslGame.exe'
    blob=exe.read_bytes();candidates=set()
    for match in re.finditer(rb'[\x21-\x7e]{32,96}\x00',blob):
        raw=match.group()[:-1]
        if len(raw) in (32,64) and len(set(raw))>15:
            candidates.add(raw[:32])
            if len(raw)==64:candidates.add(raw[32:])
    found=next((key for key in candidates if valid(key)),None)
    if found is None:raise ValueError('No validated static key for this build. No game process was accessed or modified.')
    keyfile.write_text(found.hex(),encoding='ascii')
    print('Local static candidate found and validated. The key is saved only in the local extraction cache.')
(WORK/'key-validation.json').write_text(json.dumps({'pak':pak.name,'index_sha1_matches':True,'mode':'offline local file read'},indent=2),encoding='utf-8')
