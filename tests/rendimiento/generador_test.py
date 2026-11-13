"""Corpus propio, semilla, cuotas y RAR externo opcional. No conserva archivos grandes."""
import importlib.util, pathlib, tempfile, unittest, zipfile, hashlib, os, subprocess, json
ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('gigantes', ROOT/'tools/test-data/gigantes.py')
g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)

class GeneratorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.path = pathlib.Path(self.tmp.name)
        g.BUDGET = g.Budget(100 << 20, self.tmp.name)
    def tearDown(self): self.tmp.cleanup(); g.BUDGET = None
    def test_hard_limit(self):
        g.BUDGET.limit = 100
        with g.output_file(self.path/'limited') as out:
            out.write(b'x'*80)
            with self.assertRaises(OSError): out.write(b'x'*21)
        self.assertEqual((self.path/'limited').stat().st_size, 80)
    def test_epub_stream_and_seed(self):
        for name, seed in [('a', 7), ('b', 7), ('c', 8)]: g.generar_epub(self.path/name, 128, seed)
        self.assertEqual(g.sha256(self.path/'a'), g.sha256(self.path/'b'))
        self.assertNotEqual(g.sha256(self.path/'a'), g.sha256(self.path/'c'))
        with zipfile.ZipFile(self.path/'a') as z:
            self.assertEqual(z.namelist()[0], 'mimetype')
            self.assertGreater(z.getinfo('cap.xhtml').file_size, 128*1024)
            self.assertIsNone(z.testzip())
    def test_pdf_100001_pages(self):
        data = g.generar_pdf(self.path/'pages.pdf', 100001, 7)
        with (self.path/'pages.pdf').open('rb') as f: self.assertIn(b'/Count 100001', f.read(4096))
        self.assertLess((self.path/'pages.pdf').stat().st_size, 100<<20)
    def test_scanned_pdf(self):
        data = g.generar_pdf_gb(self.path/'scan.pdf', .005, 7, 128)
        self.assertEqual(data['imagen_pixeles'], [128,181]); self.assertGreater(data['paginas'], 1)
        with (self.path/'scan.pdf').open('rb') as f: self.assertEqual(f.read(8), b'%PDF-1.5')
    def test_txt_stream(self):
        g.generar_txt(self.path/'utf16.txt', 1, 'utf-16', 7)
        with (self.path/'utf16.txt').open('rb') as f: self.assertEqual(f.read(2), b'\xff\xfe')
        self.assertGreaterEqual((self.path/'utf16.txt').stat().st_size, 1<<20)
    def test_cbz(self):
        g.generar_cbz(self.path/'comic.cbz', 3, 7)
        with zipfile.ZipFile(self.path/'comic.cbz') as z:
            self.assertEqual(len([n for n in z.namelist() if n.endswith('.jpg')]), 3); self.assertIsNone(z.testzip())
    def test_bad_cli_and_missing_rar(self):
        for args in [['--gb', 'nan'], ['--pdf-paginas','-2'], ['--scan-width','0'], ['--solo','rar-solido','--rar-command','folio-not-installed']]:
            r=subprocess.run(['python3',str(ROOT/'tools/test-data/gigantes.py'), *args],capture_output=True)
            self.assertNotEqual(r.returncode, 0)
    @unittest.skipUnless(os.environ.get('RAR_COMMAND'), 'RAR es herramienta externa opcional')
    def test_real_solid_rar(self):
        for name in ('a.cbr','b.cbr'): g.generar_rar_solido(str(self.path/name), 8, 7, os.environ['RAR_COMMAND'])
        self.assertEqual(g.sha256(self.path/'a.cbr'),g.sha256(self.path/'b.cbr'))
        result=subprocess.run([os.environ['RAR_COMMAND'],'lt',str(self.path/'a.cbr')],capture_output=True,text=True,check=True)
        self.assertIn('RAR 5, solid',result.stdout)
        subprocess.run([os.environ['RAR_COMMAND'],'t',str(self.path/'a.cbr')],stdout=subprocess.DEVNULL,check=True)

if __name__=='__main__': unittest.main()
