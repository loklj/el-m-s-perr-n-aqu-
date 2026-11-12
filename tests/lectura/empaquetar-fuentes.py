"""Descarga fuentes originales por revision y genera un AAR solo de assets (opcional)."""
import urllib.request,zipfile,pathlib,hashlib,json
commit='c30b221beb0c1da59d563feb91ec8656a1fa972a'
root=pathlib.Path(__file__).resolve().parents[2]
entries={'AndroidManifest.xml':b'<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.loklj.folioxr.fonts"/>'}
rows=[]
for family,name,key in [('gentiumbookplus','GentiumBookPlus','serif'),('atkinsonhyperlegible','AtkinsonHyperlegible','sans')]:
 url=f'https://raw.githubusercontent.com/google/fonts/{commit}/ofl/{family}/'
 license=urllib.request.urlopen(url+'OFL.txt').read()
 for folder in ['docs/licencias','hito1/Assets/StreamingAssets/Licencias']:
  (root/folder/(name+'-OFL.txt')).write_text("\n".join(line.rstrip() for line in license.decode().rstrip().splitlines())+"\n")
 entries['assets/folio-fonts/'+name+'-OFL.txt']=license
 for style in ['Regular','Italic','Bold','BoldItalic']:
  original=name+'-'+style+'.ttf';data=urllib.request.urlopen(url+original).read();target='assets/folio-fonts/'+key+'-'+style+'.ttf'
  entries[target]=data;rows.append(dict(file=target,source=url+original,sha256=hashlib.sha256(data).hexdigest(),bytes=len(data)))
with zipfile.ZipFile(root/'hito1/Assets/Plugins/Android/FolioFonts.aar','w',compression=zipfile.ZIP_DEFLATED) as z:
 for name,data in entries.items():
  info=zipfile.ZipInfo(name,(2026,11,12,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;z.writestr(info,data)
(root/'tests/lectura/fuentes.json').write_text(json.dumps({'revision':commit,'files':rows},indent=2)+'\n')
print(sum(r['bytes'] for r in rows),'bytes de fuentes; AAR', (root/'hito1/Assets/Plugins/Android/FolioFonts.aar').stat().st_size)
