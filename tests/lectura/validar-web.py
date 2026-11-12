"""Comprueba CSS real emitido por Java en Chromium, no en Horizon OS."""
import asyncio,hashlib,json,pathlib,zipfile
from playwright.async_api import async_playwright
root=pathlib.Path(__file__).resolve().parents[2]
async def main():
 count=0
 def check(value,why):
  nonlocal count
  assert value,why
  count+=1
 with zipfile.ZipFile(root/'hito1/Assets/Plugins/Android/FolioFonts.aar') as archive:
  assets={p.removeprefix('assets/'):archive.read(p) for p in archive.namelist() if p.startswith('assets/')}
 for entry in json.loads((root/'tests/lectura/fuentes.json').read_text())['files']:
  check(hashlib.sha256(assets[entry['file'].removeprefix('assets/')]).hexdigest()==entry['sha256'],'Fuente sin modificar')
 async with async_playwright() as p:
  browser=await p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox'])
  page=await browser.new_page(viewport={'width':596,'height':842})
  async def route(request):
   key=request.request.url.removeprefix('https://folio.invalid/')
   if key in assets:await request.fulfill(body=assets[key],content_type='font/ttf')
   elif key.startswith('layout') and key.endswith('.html'):
    await request.fulfill(body=(pathlib.Path('/tmp/folio-reading-layouts')/key).read_bytes(),content_type='text/html')
   else:await request.abort()
  await page.route('**/*',route)
  counts=[];destinations=[]
  for i,margin in enumerate([48,48,72,24]):
   await page.goto(f'https://folio.invalid/layout{i}.html')
   await page.evaluate('document.fonts.ready')
   result=await page.evaluate('''(margin)=>{
    const target=document.getElementById('p60');const local=Math.floor(target.getClientRects()[0].left/596);
    return {loaded:document.fonts.check('16px FolioText'),pages:Math.ceil((document.body.scrollWidth+margin)/596),local,
      width:parseFloat(getComputedStyle(document.body).columnWidth),gap:parseFloat(getComputedStyle(document.body).columnGap)};
   }''',margin)
   check(result['loaded'],'Fuente cargada antes del conteo');check(result['width']+result['gap']==596,'Paso estable');
   check(result['pages']>1 and 0<=result['local']<result['pages'],'Destino valido');
   await page.evaluate('(x)=>document.body.style.transform="translateX(-"+x*596+"px)"',result['local'])
   rect=await page.locator('#p60').evaluate('(e)=>({x:e.getClientRects()[0].left,y:e.getClientRects()[0].top})')
   check(0<=rect['x']<596 and 0<=rect['y']<842,'Salto al destino visible')
   counts.append(result['pages']);destinations.append(result['local'])
  check(counts[2]>counts[3],'Interlineado/margenes repaginan');check(len(set(counts))>1,'Conteos distintos');
  await browser.close()
 print('OK:',count,'comprobaciones de fuentes/CSS en Chromium; paginas',counts,'destinos',destinations)
asyncio.run(main())
