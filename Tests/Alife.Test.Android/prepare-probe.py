"""Generate the device page with the current DeskPet gesture code, not a copied implementation."""
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
modules = repo / 'Sources/Alife.Function/Alife.Function.DeskPet/Live2DDeskPet/Modules'
scripts = []
for name in ('DragModule', 'ResizeModule'):
    source = (modules / f'{name}.cs').read_text(encoding='utf-8-sig')
    scripts.append(source.split('public string JsCode => @"', 1)[1].split('\n";', 1)[0])
page = Path(__file__).with_name('window-probe.html').read_text(encoding='utf-8-sig')
output = repo / '.build-validation/android-window-probe.html'
output.parent.mkdir(exist_ok=True)
output.write_text(page.replace('/* DESKPET_GESTURES */', '\n'.join(scripts)), encoding='utf-8')
print(output)
