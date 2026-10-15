from pathlib import Path
from pubg_pak import Pak,ROOT,WORK

dest=WORK/'raw';dest.mkdir(exist_ok=True)
sound=Pak(ROOT/'pakchunk0-WindowsNoEditor_sound.pak')
name='WwiseAudio/Win64/UI_SoundBank.bnk'
(dest/'UI_SoundBank.bnk').write_bytes(sound.extract(name))
bp=Pak(ROOT/'pakchunk0-WindowsNoEditor_ui_bps.pak')
for name in bp.files:
    if any(s in name for s in ['DT_RadioMessage_World.','DT_MarkerDefinition.','DT_RadioMessageAlarmSoundCategory.']):
        (dest/Path(name).name).write_bytes(bp.extract(name))
etc=Pak(ROOT/'pakchunk0-WindowsNoEditor_etc.pak')
for name in etc.files:
    if 'ST_RadioMessage' in name or 'ST_EMOJI' in name:
        (dest/Path(name).name).write_bytes(etc.extract(name))
name='Content/Localization/TSL_UI/zh-CN/TSL_UI.locres'
(dest/'zh-CN-TSL_UI.locres').write_bytes(etc.extract(name))
print('Original soundbank, radio tables and Chinese localization exported read-only.')
