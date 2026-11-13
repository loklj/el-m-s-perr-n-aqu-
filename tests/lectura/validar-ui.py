"""Compila BookLibrary/MenuModel reales; el resto son contratos simulados, no Unity."""
import json,os,pathlib,subprocess,tempfile
root=pathlib.Path(__file__).resolve().parents[2];dotnet=pathlib.Path(os.environ['DOTNET_ROOT']);sdk=sorted((dotnet/'sdk').glob('8.*'))[-1];refs=sorted((dotnet/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
with tempfile.TemporaryDirectory(prefix='folio-reading-ui-') as d:
 target=pathlib.Path(d)/'Test.dll'
 cmd=[str(dotnet/'dotnet'),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-nowarn:0649,0067','-langversion:9.0','-define:UNITY_ANDROID','-target:exe','-out:'+str(target)]
 cmd+=['-r:'+str(p) for p in refs.glob('*.dll')]
 cmd+=[str(root/p) for p in ['tests/formatos/bridge/Stubs.cs','tests/lectura/UiStubs.cs','tests/lectura/Encargo18Stubs.cs','hito1/Assets/FolioXR/Scripts/Books/BookCollections.cs','tests/lectura/UiTest.cs','hito1/Assets/FolioXR/Scripts/Reading/ReadingLayout.cs','hito1/Assets/FolioXR/Scripts/Reading/ReadingDiagnostics.cs','hito1/Assets/FolioXR/Scripts/Reading/ReadingPerformanceMonitor.cs','hito1/Assets/FolioXR/Scripts/Reading/PageTheme.cs','hito1/Assets/FolioXR/Scripts/Reading/PdfPageRenderer.cs','hito1/Assets/FolioXR/Scripts/Books/BookLibrary.cs','hito1/Assets/FolioXR/Scripts/Menu/MenuModel.cs']]
 result=subprocess.run(cmd)
 if result.returncode:raise SystemExit(result.returncode)
 target.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
 result=subprocess.run([str(dotnet/'dotnet'),str(target)])
 if result.returncode:raise SystemExit(result.returncode)
