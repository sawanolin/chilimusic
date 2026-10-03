from PIL import Image
from pathlib import Path
root=Path(__file__).resolve().parents[1]
im=Image.open(root/'design/chilimusic-icon.png').convert('RGBA')
im.resize((256,256),Image.Resampling.LANCZOS).save(root/'src/Resources/chilimusic.png')
im.save(root/'src/Resources/player.ico',sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
