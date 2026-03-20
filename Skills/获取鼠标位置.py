import pyautogui
import json

x, y = pyautogui.position()
print(json.dumps({"x": x, "y": y}))
