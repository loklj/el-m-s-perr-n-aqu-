"""Corpus original, minimo y determinista. No contiene documentos de terceros."""
from pathlib import Path
from zipfile import ZipFile, ZipInfo, ZIP_STORED, ZIP_DEFLATED
import struct, zlib
root=Path('tests/fixtures/documentos'); root.mkdir(parents=True,exist_ok=True)
def chunk(kind,data): return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',2,2,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(b'\0\x10\x80\xc0\x10\x80\xc0'*2))+chunk(b'IEND',b'')
def archive(name,parts):
 with ZipFile(root/name,'w') as z:
  for path,data in parts.items():
   info=ZipInfo(path,(2026,11,11,0,0,0));info.compress_type=ZIP_STORED if path=='mimetype' else ZIP_DEFLATED
   z.writestr(info,data.encode('utf-8') if isinstance(data,str) else data)
w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'
r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'
a='http://schemas.openxmlformats.org/drawingml/2006/main'
rels='http://schemas.openxmlformats.org/package/2006/relationships'
ct='http://schemas.openxmlformats.org/package/2006/content-types'
body='''<w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Documento propio</w:t></w:r></w:p>
<w:p><w:r><w:t xml:space="preserve">Texto áéíóú ñ &amp; &lt;seguro&gt; </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>negrita</w:t></w:r><w:r><w:rPr><w:rStyle w:val="Emphasis"/></w:rPr><w:t>cursiva</w:t></w:r></w:p>
<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Primero</w:t></w:r></w:p>
<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>Segundo</w:t></w:r></w:p>
<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="3000"/><w:gridCol w:w="3000"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>Celda A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Celda B</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
<w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="914400" cy="914400"/><wp:docPr id="1" name="Original"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:nvPicPr><pic:cNvPr id="1" name="Original"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rId1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p><w:sectPr/>'''
doc=lambda body:f'<w:document xmlns:w="{w}" xmlns:r="{r}" xmlns:a="{a}"><w:body>{body}</w:body></w:document>'
parts={'[Content_Types].xml':f'<Types xmlns="{ct}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/></Types>',
'_rels/.rels':f'<Relationships xmlns="{rels}"><Relationship Id="document" Type="{r}/officeDocument" Target="word/document.xml"/></Relationships>',
'word/document.xml':doc(body),'word/styles.xml':f'<w:styles xmlns:w="{w}"><w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style><w:style w:type="character" w:styleId="Emphasis"><w:name w:val="Emphasis"/><w:rPr><w:i/></w:rPr></w:style></w:styles>',
'word/numbering.xml':f'<w:numbering xmlns:w="{w}"><w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num></w:numbering>',
'word/_rels/document.xml.rels':f'<Relationships xmlns="{rels}"><Relationship Id="rId1" Type="{r}/image" Target="media/propia.png"/><Relationship Id="styles" Type="{r}/styles" Target="styles.xml"/><Relationship Id="numbering" Type="{r}/numbering" Target="numbering.xml"/></Relationships>','word/media/propia.png':png}
archive('propio.docx',parts)
archive('danado.docx',{**parts,'word/document.xml':'<w:document'})
archive('entidad.docx',{**parts,'word/document.xml':'<!DOCTYPE x [<!ENTITY x SYSTEM "file:///etc/passwd">]>'+doc('<w:p><w:r><w:t>&x;</w:t></w:r></w:p>')})
archive('externo.docx',{**parts,'word/_rels/document.xml.rels':f'<Relationships xmlns="{rels}"><Relationship Id="rId1" Type="{r}/image" Target="https://example.invalid/no.png" TargetMode="External"/></Relationships>'})
ns='''xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"'''
content=f'''<office:document-content {ns} office:version="1.2"><office:automatic-styles><style:style style:name="B" style:family="text"><style:text-properties fo:font-weight="bold"/></style:style><style:style style:name="I" style:family="text"><style:text-properties fo:font-style="italic"/></style:style><text:list-style style:name="L"><text:list-level-style-number text:level="1" style:num-format="1"/></text:list-style></office:automatic-styles><office:body><office:text><text:h text:outline-level="1">Documento propio</text:h><text:p>Texto áéíóú ñ &amp; &lt;seguro&gt; <text:span text:style-name="B">negrita</text:span><text:span text:style-name="I">cursiva</text:span></text:p><text:list text:style-name="L"><text:list-item><text:p>Primero</text:p></text:list-item><text:list-item><text:p>Segundo</text:p></text:list-item></text:list><table:table table:name="Tabla"><table:table-column table:number-columns-repeated="2"/><table:table-row><table:table-cell office:value-type="string"><text:p>Celda A</text:p></table:table-cell><table:table-cell office:value-type="string"><text:p>Celda B</text:p></table:table-cell></table:table-row></table:table><text:p><draw:frame draw:name="Original" text:anchor-type="as-char" svg:width="1cm" svg:height="1cm"><draw:image xlink:href="Pictures/propia.png" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/></draw:frame></text:p></office:text></office:body></office:document-content>'''
manifest='''<manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.2"><manifest:file-entry manifest:full-path="/" manifest:version="1.2" manifest:media-type="application/vnd.oasis.opendocument.text"/><manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/><manifest:file-entry manifest:full-path="Pictures/propia.png" manifest:media-type="image/png"/></manifest:manifest>'''
odt={'mimetype':'application/vnd.oasis.opendocument.text','content.xml':content,'styles.xml':f'<office:document-styles {ns} office:version="1.2"><office:styles/></office:document-styles>','META-INF/manifest.xml':manifest.replace('</manifest:manifest>', '<manifest:file-entry manifest:full-path="styles.xml" manifest:media-type="text/xml"/></manifest:manifest>'),'Pictures/propia.png':png}
archive('propio.odt',odt);archive('danado.odt',{**odt,'content.xml':'<incompleto>'})
archive('cifrado-sintetico.odt',{**odt,'META-INF/manifest.xml':manifest.replace('</manifest:manifest>','<manifest:file-entry manifest:full-path="secret.xml" manifest:media-type="text/xml"><manifest:encryption-data/></manifest:file-entry></manifest:manifest>')})
rtf=r'''{\rtf1\ansi\ansicpg1252\uc1{\fonttbl{\f0\froman Times New Roman;}}{\stylesheet{\s1\outlinelevel0 Heading 1;}}
\pard\s1 Documento propio\par
\pard Texto \'e1\'e9\'ed\'f3\'fa \'f1 & <seguro> {\b negrita}{\i cursiva} \u8364? {\b\u-10179?\u-8704?}\par
\pard{\listtext\bullet\tab}Primero\par
\pard{\listtext\bullet\tab}Segundo\par
\trowd\cellx2000\cellx4000\pard\intbl Celda A\cell Celda B\cell\row
\pard {\pict\pngblip\picw2\pich2 IMAGE}\par
{\*\desconocido NO MOSTRAR}{\field{\*\fldinst HYPERLINK "https://example.invalid"}{\fldrslt Enlace visible}}\par
}'''.replace('IMAGE',png.hex())
(root/'propio.rtf').write_bytes(rtf.encode('ascii'));(root/'danado.rtf').write_bytes(rtf[:-2].encode('ascii'))
(root/'objeto.rtf').write_bytes(br'{\rtf1\ansi Antes {\object{\*\objdata 010203}SECRETO} despues\par}')
(root/'desconocido.bin').write_bytes(b'Formato de prueba no reconocido\x00\x01')
# Firmas sinteticas: no se presentan como DOC/KFX validos ni como libros cifrados.
(root/'ole-firma.doc').write_bytes(bytes.fromhex('d0cf11e0a1b11ae1')+b'\0'*504)
(root/'kfx-firma.kfx').write_bytes(b'CONT'+b'\0'*92)
