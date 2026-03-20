import pyautogui
import os
import datetime
import json

# 定位系统缓存目录 (AppData/Local/Alife/Cache)
cache_root = os.path.join(os.environ.get('LOCALAPPDATA', os.environ.get('TEMP')), 'Alife', 'Cache')
if not os.path.exists(cache_root):
    os.makedirs(cache_root)

filename = f"capture_{datetime.datetime.now().strftime('%Y%m%d_%H%M%S')}.png"
path = os.path.join(cache_root, filename)

pyautogui.screenshot().save(path)
print(json.dumps({"status": "success", "file_path": path}))
