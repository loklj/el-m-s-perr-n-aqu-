#!/usr/bin/env python3
"""Compila Books/ShelfLayout.cs (cuentas puras de la estantería) y corre LayoutTest, sin Unity."""
import json, os, pathlib, subprocess, sys, tempfile
here = pathlib.Path(__file__).resolve().parent
repo = here.parent.parent
dotnet = pathlib.Path(os.environ.get('DOTNET_ROOT', '/usr/lib/dotnet'))
sdk = sorted((dotnet / 'sdk').glob('8.*'))[-1]
refs = sorted((dotnet / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1] / 'ref/net8.0'
with tempfile.TemporaryDirectory(prefix='folio-estanteria-') as tmp:
    out = pathlib.Path(tmp) / 'Test.dll'
    cmd = [str(dotnet / 'dotnet'), str(sdk / 'Roslyn/bincore/csc.dll'), '-nologo', '-langversion:9.0',
           '-target:exe', '-out:' + str(out)] + ['-r:' + str(p) for p in refs.glob('*.dll')] + [
           str(here / 'LayoutTest.cs'), str(repo / 'hito1/Assets/FolioXR/Scripts/Books/ShelfLayout.cs')]
    done = subprocess.run(cmd, capture_output=True, text=True)
    if done.returncode != 0:
        print(done.stdout[-4000:] + done.stderr[-2000:])
        sys.exit(1)
    out.with_suffix('.runtimeconfig.json').write_text(json.dumps(
        {'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    sys.exit(subprocess.run([str(dotnet / 'dotnet'), str(out)]).returncode)
