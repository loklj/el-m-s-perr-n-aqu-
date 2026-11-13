"""Ejecuta miembros reales de FolioBook con contratos Unity mínimos; no simula el motor gráfico.
Se extrae el código de navegación/presentación, sin copiar su implementación a los stubs.
"""
import json, os, pathlib, re, subprocess, tempfile
root=pathlib.Path(__file__).resolve().parents[2]
source=(root/'hito1/Assets/FolioXR/Scripts/Reading/FolioBook.cs').read_text()
def method(name):
    match=re.search(r'^        (?:public|private) (?:static )?[^\n]+\b'+name+r'\([^\n]*\)\s*(?:=>[^;]+;|\{)',source,re.M)
    if not match: raise RuntimeError('Miembro no encontrado: '+name)
    start=match.start()
    if '=>' in match.group(): return match.group()
    end=match.end(); depth=1
    # Estos miembros no contienen llaves en literales ni comentarios.
    while depth:
        if source[end]=='{':depth+=1
        elif source[end]=='}':depth-=1
        end+=1
    return source[start:end]
names=['GetSingleCover','SetSingleCoverFor','RefreshSpreadLayout','SetRightToLeft','GoToPage','NextSpread','PreviousSpread','ShowSpread','ShowPage']
properties=[]
for name in ['LeftPageIndex','LeftSlotPage','RightSlotPage','RightPageVisible']:
    properties.append(re.search(r'public (?:int|bool) '+name+r' =>[^;]+;',source).group())
# Conserva también la clave real de persistencia.
properties.append(re.search(r'public const string SingleCoverPrefsPrefix = [^;]+;',source).group())
text='using System;using System.Collections;using UnityEngine;using FolioXR.Accessibility;namespace FolioXR.Reading {public sealed partial class FolioBook {\n'+'\n'.join(properties+[method(n) for n in names])+'\n}}'
dotnet=pathlib.Path(os.environ['DOTNET_ROOT']);sdk=sorted((dotnet/'sdk').glob('8.*'))[-1];refs=sorted((dotnet/'packs/Microsoft.NETCore.App.Ref').glob('8.*'))[-1]/'ref/net8.0'
with tempfile.TemporaryDirectory(prefix='folio-portada-') as d:
    d=pathlib.Path(d);(d/'FolioBook.cs').write_text(text);target=d/'Test.dll'
    cmd=[str(dotnet/'dotnet'),str(sdk/'Roslyn/bincore/csc.dll'),'-nologo','-nowarn:0649,0414','-langversion:9.0','-target:exe','-out:'+str(target)]
    cmd+=['-r:'+str(p) for p in refs.glob('*.dll')]
    cmd+=[str(d/'FolioBook.cs')]+[str(root/p) for p in ['hito1/Assets/FolioXR/Scripts/Reading/BookSpread.cs','tests/lectura/PortadaStubs.cs','tests/lectura/PortadaTest.cs']]
    subprocess.run(cmd,check=True)
    target.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions':{'tfm':'net8.0','framework':{'name':'Microsoft.NETCore.App','version':'8.0.0'}}}))
    subprocess.run([str(dotnet/'dotnet'),str(target)],check=True)
