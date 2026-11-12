"""Compila el puente real con Roslyn y stubs. Requiere DOTNET_ROOT con SDK .NET 8."""
import json, os, pathlib, subprocess, tempfile
repo = pathlib.Path(__file__).resolve().parents[2]
dotnet = pathlib.Path(os.environ['DOTNET_ROOT'])
sdk = sorted((dotnet / 'sdk').glob('8.*'))[-1]
refs = sorted((dotnet / 'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1] / 'ref/net8.0'
with tempfile.TemporaryDirectory(prefix='folio-bridge-') as directory:
    target = pathlib.Path(directory) / 'Bridge.dll'
    command = [str(dotnet / 'dotnet'), str(sdk / 'Roslyn/bincore/csc.dll'), '-nologo', '-langversion:9.0', '-define:UNITY_ANDROID', '-target:exe', '-out:' + str(target)]
    command += ['-r:' + str(p) for p in refs.glob('*.dll')]
    command += [str(repo / p) for p in ['tests/formatos/bridge/Stubs.cs', 'tests/formatos/bridge/Test.cs', 'hito1/Assets/FolioXR/Scripts/Reading/PdfPageRenderer.cs', 'hito1/Assets/FolioXR/Scripts/Reading/PageTheme.cs']]
    subprocess.run(command, check=True)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': '8.0.0'}}}))
    subprocess.run([str(dotnet / 'dotnet'), str(target)], check=True)
