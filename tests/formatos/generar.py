"""Fixtures sintéticos propios, sin libros ajenos ni DRM real. Ejecutar desde la raíz."""
import struct, pathlib, zlib, binascii, zipfile
P=pathlib.Path('tests/fixtures/formatos'); P.mkdir(parents=True,exist_ok=True)
def u16(v): return struct.pack('>H',v)
def u32(v): return struct.pack('>I',v)
def put(b,p,v): b[p:p+4]=u32(v)
def pdb(records):
 h=bytearray(78); h[:10]=b'Folio test'; h[60:68]=b'BOOKMOBI'; h[76:78]=u16(len(records)); off=78+8*len(records)
 for r in records: h+=u32(off)+b'\0'*4;off+=len(r)
 return h+b''.join(records)
def header(text,compression=1,version=6,images=2):
 h=bytearray(280); h[0:2]=u16(compression); put(h,4,len(text));h[8:10]=u16(1);h[10:12]=u16(4096)
 h[16:20]=b'MOBI';put(h,20,264);put(h,28,65001);put(h,36,version);put(h,108,images)
 for i in [192,248,252]:put(h,i,0xffffffff)
 return h
def chunk(t,d):return u32(len(d))+t+d+u32(binascii.crc32(t+d)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',8,8,8,2,0,0,0))+chunk(b'IDAT',zlib.compress((b'\0'+b'\x20\x60\xa0'*8)*8))+chunk(b'IEND',b'')
text='<html><body><p>Folio: español, acción y ñ.</p><img recindex="1"/></body></html>'.encode()
(P/'propio.mobi').write_bytes(pdb([header(text),text,png]))
# PalmDOC con cadenas literales escapadas; la prueba JVM añade referencias superpuestas.
compressed=b''.join(bytes([len(text[i:i+8])])+text[i:i+8] for i in range(0,len(text),8))
(P/'palmdoc.azw').write_bytes(pdb([header(text,2),compressed,png]))
h=header(text);h[12:14]=u16(2);(P/'cifrado-sintetico.mobi').write_bytes(pdb([h,text,png]))
# Diccionario HUFF/CDIC: 256 símbolos literales y códigos canónicos de ocho bits.
huff=b'HUFF'+u32(24)+u32(24)+u32(1048)+b'\0'*8
huff+=b''.join(u32((255<<8)|128|8) for i in range(256))
huff+=b''.join(u32(0 if i==8 else 1)+u32(255 if i==8 else 0) for i in range(1,33))
cdic=b'CDIC'+u32(16)+u32(256)+u32(8)+b''.join(u16(512+3*i) for i in range(256))+b''.join(u16(32769)+bytes([255-i]) for i in range(256))
h=header(text,17480);put(h,112,3);put(h,116,2)
(P/'huff.mobi').write_bytes(pdb([h,text,png,huff,cdic]))
def vli(n):
 a=[(n&127)|128];n>>=7
 while n:a.insert(0,n&127);n>>=7
 return bytes(a)
def index(tags,label,values):
 meta=bytearray(56);meta[:4]=b'INDX';put(meta,4,56);put(meta,24,1)
 tagx=b'TAGX'+u32(12+4*(len(tags)+1))+u32(1)+b''.join(bytes(t) for t in tags)+b'\0\0\0\1'
 data=bytearray(56);data[:4]=b'INDX';put(data,4,56);put(data,24,1)
 entry=bytes([len(label)])+label+bytes([sum(t[2] for t in tags)])+b''.join(vli(v) for v in values)
 put(data,20,56+len(entry));return [meta+tagx,data+entry+b'IDXT'+u16(56)]
skel=b'<html><body></body></html>';frag='<p>KF8 propio, corazón.</p><img src="kindle:embed:0001?mime=image/png"/>'.encode();raw=skel+frag
h=header(raw,version=8);put(h,248,3);put(h,252,5);put(h,192,7)
fragidx=index([(2,1,1,0),(3,1,2,0),(4,1,4,0),(6,2,8,0)],b'12',[0,0,0,0,len(frag)])
skelidx=index([(1,1,1,0),(6,2,2,0)],b'parte0',[1,0,len(skel)])
fdst=b'FDST'+u32(12)+u32(1)+u32(0)+u32(len(raw))
(P/'kf8.azw3').write_bytes(pdb([h,raw,png]+fragidx+skelidx+[fdst]))
(P/'truncado.mobi').write_bytes(b'PalmDB roto')
for codec in ['utf-16le','utf-16be','iso-8859-1']:(P/('texto-'+codec+'.txt')).write_bytes(('Texto propio. Acción y niño.\n'*4).encode(codec))
with zipfile.ZipFile(P/'zip-disfrazado.cbr','w') as z:
 for name in ['pagina2.png','pagina10.png']:z.writestr(zipfile.ZipInfo(name,(2026,11,11,0,0,0)),png)
# RAR4 almacenado propio, con CRC de cada encabezado. Sin compresión RAR.
def rar_header(kind,flags,body):
 data=struct.pack('<BHH',kind,flags,7+len(body))+body
 return struct.pack('<H',binascii.crc32(data)&65535)+data
rar=b'Rar!\x1a\x07\x00'+rar_header(0x73,0,b'\0'*6)
name=b'pagina1.png'
body=struct.pack('<IIBIIBBHI',len(png),len(png),3,binascii.crc32(png)&0xffffffff,0,20,0x30,len(name),0x20)+name
rar+=rar_header(0x74,0x8000,body)+png+rar_header(0x7b,0x4000,b'')
(P/'rar4-almacenado.cbr').write_bytes(rar)
(P/'rar5-firma.cbr').write_bytes(b'Rar!\x1a\x07\x01\x00')
