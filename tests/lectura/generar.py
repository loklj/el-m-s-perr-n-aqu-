"""Fixtures originales y deterministas para índices. Sin libros ajenos ni DRM real."""
from pathlib import Path
import struct,zlib,zipfile,runpy
P=Path('tests/fixtures/lectura');P.mkdir(parents=True,exist_ok=True)
def pdf(objects,root=1):
 out=bytearray(b'%PDF-1.7\n%\xe2\xe3\xcf\xd3\n'); offsets={}
 for n,b in objects.items():offsets[n]=len(out);out+=f'{n} 0 obj\n'.encode()+b+b'\nendobj\n'
 xref=len(out);out+=f'xref\n0 {max(objects)+1}\n0000000000 65535 f \n'.encode()
 for n in range(1,max(objects)+1):out+=(f'{offsets[n]:010d} 00000 n \n' if n in offsets else '0000000000 00000 f \n').encode()
 out+=f'trailer\n<< /Size {max(objects)+1} /Root {root} 0 R >>\nstartxref\n{xref}\n%%EOF\n'.encode();return bytes(out)
objects={1:b'<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R /Names << /Dests 9 0 R >> >>',2:b'<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>',3:b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << >> >>',4:b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] /Resources << >> >>',5:b'<< /Type /Outlines /First 6 0 R /Last 8 0 R /Count 3 >>',6:b'<< /Title (Inicio) /Parent 5 0 R /Dest [3 0 R /Fit] /First 7 0 R /Last 7 0 R /Count 1 /Next 8 0 R >>',7:b'<< /Title <FEFF00430061007000ED00740075006C006F> /Parent 6 0 R /A << /S /GoTo /D (fin) >> >>',8:b'<< /Title (Fin \\(local\\)) /Parent 5 0 R /Prev 6 0 R /Dest [4 0 R /XYZ null null null] >>',9:b'<< /Names [(fin) [4 0 R /Fit]] >>'}
(P/'indice.pdf').write_bytes(pdf(objects))
# Xref stream + ObjStm propio, con los objetos 1, 5, 6, 7, 8 y 9 comprimidos.
compressed_ids=[1,5,6,7,8,9];pieces=b'';header=b''
for n in compressed_ids:header+=f'{n} {len(pieces)} '.encode();pieces+=objects[n]+b' '
stream=header+pieces;packed=zlib.compress(stream)
objstream=f'<< /Type /ObjStm /N {len(compressed_ids)} /First {len(header)} /Length {len(packed)} /Filter /FlateDecode >>\nstream\n'.encode()+packed+b'\nendstream'
out=bytearray(b'%PDF-1.7\n');offsets={}
for n,b in {2:objects[2],3:objects[3],4:objects[4],10:objstream}.items():offsets[n]=len(out);out+=f'{n} 0 obj\n'.encode()+b+b'\nendobj\n'
offsets[11]=len(out);rows=[]
for n in range(12):
 if n in compressed_ids:rows.append(bytes([2])+struct.pack('>IH',10,compressed_ids.index(n)))
 elif n in offsets:rows.append(bytes([1])+struct.pack('>IH',offsets[n],0))
 else:rows.append(bytes([0])+struct.pack('>IH',0,65535))
# Predictor PNG Up: cada fila incluye byte de filtro 2.
prev=bytes(7);predict=b''
for row in rows:predict+=b'\2'+bytes((a-b)%256 for a,b in zip(row,prev));prev=row
encoded=zlib.compress(predict)
out+=f'11 0 obj\n<< /Type /XRef /Size 12 /Root 1 0 R /W [1 4 2] /Length {len(encoded)} /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 7 >> >>\nstream\n'.encode()+encoded+f'\nendstream\nendobj\nstartxref\n{offsets[11]}\n%%EOF\n'.encode()
(P/'indice-stream.pdf').write_bytes(out)
(P/'ciclo.pdf').write_bytes(pdf({**objects,6:objects[6].replace(b'/Next 8',b'/Next 6')}))
(P/'cifrado-metadata.pdf').write_bytes(pdf(objects).replace(b'/Root 1 0 R >>',b'/Root 1 0 R /Encrypt << /Filter /Standard >> >>'))
(P/'truncado.pdf').write_bytes(b'%PDF-1.7\nstartxref\n999999\n%%EOF')
# EPUB3 con dos destinos distintos en un capitulo, landmarks separado y profundidad.
container='<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"><rootfiles><rootfile full-path="OPS/book.opf" media-type="application/oebps-package+xml"/></rootfiles></container>'
opf='<package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">folio-lectura</dc:identifier><dc:title>Índice propio</dc:title><dc:language>es</dc:language><meta property="dcterms:modified">2026-11-12T00:00:00Z</meta></metadata><manifest><item id="c" href="c.xhtml" media-type="application/xhtml+xml"/><item id="n" href="nav.xhtml" properties="nav" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="c"/></spine></package>'
nav='<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops"><head><title>Índice</title></head><body><nav epub:type="landmarks"><a href="c.xhtml">NO es TOC</a></nav><nav epub:type="toc"><ol><li><a href="c.xhtml#uno">Primero</a><ol><li><a href="c.xhtml#dos">Segundo</a></li></ol></li><li><a href="https://example.invalid/externo">Externo</a></li></ol></nav></body></html>'
chapter='<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Prueba</title></head><body><h1 id="uno">Primero</h1>'+('<p>Texto propio para comprobar el salto y la repaginación.</p>'*60)+'<h2 id="dos">Segundo</h2><p>Final.</p></body></html>'
def epub(name,ncx=False):
 parts={'mimetype':'application/epub+zip','META-INF/container.xml':container,'OPS/book.opf':opf,'OPS/nav.xhtml':nav,'OPS/c.xhtml':chapter}
 if ncx:
  parts['OPS/book.opf']=opf.replace('id="n" href="nav.xhtml" properties="nav" media-type="application/xhtml+xml"','id="n" href="toc.ncx" media-type="application/x-dtbncx+xml"').replace('<spine>','<spine toc="n">')
  del parts['OPS/nav.xhtml'];parts['OPS/toc.ncx']='<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1"><head/><docTitle><text>Índice</text></docTitle><navMap><navPoint id="a" playOrder="1"><navLabel><text>Primero NCX</text></navLabel><content src="c.xhtml#uno"/><navPoint id="b" playOrder="2"><navLabel><text>Segundo NCX</text></navLabel><content src="c.xhtml#dos"/></navPoint></navPoint></navMap></ncx>'
 with zipfile.ZipFile(P/name,'w') as z:
  for path,data in parts.items():z.writestr(zipfile.ZipInfo(path,(2026,11,12,0,0,0)),data.encode())
epub('indice.epub');epub('indice-ncx.epub',True)
# Reusar SOLO generadores propios del encargo 10.
m=runpy.run_path('tests/formatos/generar.py');header=m['header'];put=m['put'];pdb=m['pdb'];index=m['index'];vli=m['vli']
raw=b'<html><body><h1>Primero</h1><p>Texto.</p><h2>Segundo</h2><p>Final.</p></body></html>'
meta,data=index([(1,1,1,0),(3,1,2,0),(4,1,4,0)],b'entrada',[raw.index(b'<h2>'),0,0]);put(meta,52,1)
label='Segundo con índice'.encode();cncx=vli(len(label))+label
h=header(raw,images=0xffffffff);put(h,244,2)
(P/'indice.mobi').write_bytes(pdb([h,raw,meta,data,cncx]))
guide=b'<html><head><guide><reference type="toc" filepos="0000000000"/></guide></head><body><h1>Inicio</h1><h2>Final</h2><div><a filepos="0000000000">Final HTML</a></div></body></html>'
start=guide.index(b'<div>');target=guide.index(b'<h2>');guide=guide.replace(b'filepos="0000000000"',f'filepos="{start:010}"'.encode(),1);guide=guide.replace(b'filepos="0000000000"',f'filepos="{target:010}"'.encode(),1)
(P/'indice-html.mobi').write_bytes(pdb([header(guide,images=0xffffffff),guide]))
# Actualizacion incremental: la ultima version del marcador sustituye a la anterior.
base=pdf(objects);previous=int(base.split(b'startxref\n')[-1].splitlines()[0]);offset=len(base)
extra=b'7 0 obj\n<< /Title (Revisado) /Parent 6 0 R /Dest [4 0 R /Fit] >>\nendobj\n';where=offset+len(extra)
(P/'indice-incremental.pdf').write_bytes(base+extra+f'xref\n7 1\n{offset:010} 00000 n \ntrailer\n<< /Size 10 /Root 1 0 R /Prev {previous} >>\nstartxref\n{where}\n%%EOF\n'.encode())
# KF8: destino dentro del fragmento reconstruido, no al principio del esqueleto.
kf=bytearray((Path('tests/fixtures/formatos')/'kf8.azw3').read_bytes());count=int.from_bytes(kf[76:78],'big');offsets=[int.from_bytes(kf[78+i*8:82+i*8],'big') for i in range(count)]+[len(kf)];records=[kf[offsets[i]:offsets[i+1]] for i in range(count)]
meta,data=index([(1,1,1,0),(3,1,2,0),(4,1,4,0)],b'entrada',[len(m['skel']),0,0]);put(meta,52,1);label='Sección KF8'.encode();put(records[0],244,len(records));records += [meta,data,vli(len(label))+label]
(P/'indice.azw3').write_bytes(pdb(records))
