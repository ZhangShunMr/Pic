#!/usr/bin/env python3
"""Static command/property binding audit; does not instantiate or validate WPF UI."""
import json
import re
from pathlib import Path
from xml.etree import ElementTree
root=Path(__file__).resolve().parents[1]
view=root/'src/Blower.Desktop/MainWindow.xaml'
ElementTree.parse(view)
texts='\n'.join(p.read_text() for p in (root/'src/Blower.Desktop/ViewModels').glob('*.cs'))
roots=set(re.findall(r'\{Binding ([A-Za-z][A-Za-z0-9]*)', view.read_text()))
# Item templates use EditorField and WorkspaceNode contexts, included in texts above.
missing=sorted(x for x in roots if not re.search(r'\b'+x+r'\b',texts))
assert not missing, missing
commands=sorted(x for x in roots if x.endswith('Command'))
assert len(commands)>20
sources=list((root/'src').rglob('*.csproj'))
assert all('Microsoft.NET.Sdk.Web' not in p.read_text() for p in sources)
assert not list((root/'src').rglob('*.razor'))
result={'kind':'static XML and declared binding name audit only','binding_roots':len(roots),
        'command_bindings':len(commands),'missing':missing,'wpf_runtime_verified':False}
(root/'docs/xaml-static-evidence.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result))
