from pathlib import Path
import os
BUNDLE=Path(__file__).resolve().parents[2]
WORK=Path(os.environ.get('PUBG_ASSET_CACHE',str(BUNDLE/'data'/'extraction-cache'))).resolve()
WORK.mkdir(parents=True,exist_ok=True)
ROOT=Path(os.environ.get('PUBG_PAK_DIR',r'D:\SteamLibrary\steamapps\common\PUBG\TslGame\Content\Paks'))
