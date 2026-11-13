using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using FolioXR.Reading;
using UnityEngine;

namespace FolioXR.Books
{
    /// <summary>Ficha de un libro de la biblioteca (campos con los nombres del JSON del plugin Java).</summary>
    [Serializable]
    public sealed class BookEntry
    {
        public string id, title, author, format, path, cover;
        public long size, lastRead;
        public int pages, position;

        public bool IsPdf => format == "pdf";
        /// <summary>Formatos que el libro 3D ya abre: PDF, fuentes HTML y cómics CBZ/CBR (o ZIP de imágenes).</summary>
        public bool CanOpen => format == "pdf" || format == "cbz" || format == "zip" || format == "epub" || format == "txt" || format == "mobi" || format == "azw" || format == "azw3" || format == "cbr" || format == "docx" || format == "odt" || format == "rtf";

        /// <summary>Progreso para el menú: porcentaje, o si aún no se abrió.</summary>
        public string ProgressText => pages <= 0
            ? "sin leer"
            : Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(position + 2, pages) * 100f / pages), 1, 100) + "%";
    }

    [Serializable]
    internal sealed class BookList
    {
        public List<BookEntry> books = new List<BookEntry>();
    }

    [Serializable]
    public sealed class NamedBookmark
    {
        public int position;
        public string label;
        public string Title => string.IsNullOrWhiteSpace(label) ? "Página " + (position + 1) : label;
    }

    [Serializable]
    internal sealed class BookmarkList
    {
        public List<int> positions = new List<int>();
        public List<NamedBookmark> entries = new List<NamedBookmark>();
    }

    [Serializable]
    internal sealed class WifiStatus
    {
        public bool running;
        public string url, code, message, error;
        public int received, serial;
        public long copied, total;
    }

    [Serializable]
    internal sealed class ImportStatus
    {
        public string state, file, message;
        public int index, count, imported, serial;
        public long copied, total;
    }

    /// <summary>
    /// Encargo 5 (escrito por Claude). Puente con el plugin Java FolioLibrary: biblioteca en SQLite,
    /// importación con el selector del sistema, progreso de lectura y marcadores.
    /// Fuera del visor (Editor) queda desactivada y el libro abre el PDF de prueba, como antes.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class BookLibrary : MonoBehaviour
    {
        private const string JavaClass = "com.loklj.folioxr.FolioLibrary";
        private const string WifiJavaClass = "com.loklj.folioxr.FolioWifiServer";
        private const float SaveDelay = 2f, PollInterval = 0.3f;

        public static BookLibrary Instance { get; private set; }
        /// <summary>Cambió la lista, el libro abierto, los marcadores o el avance de una importación.</summary>
        public static event Action StateChanged;
        /// <summary>Mensaje para mostrar al usuario (lo presenta el menú).</summary>
        public static event Action<string> Notice;

        public bool Available { get; private set; }
        public IReadOnlyList<BookEntry> Books => _books;
        public IReadOnlyList<int> Bookmarks => _bookmarks;
        public IReadOnlyList<NamedBookmark> NamedBookmarks => _namedBookmarks;
        public IReadOnlyList<ContentsEntry> Contents => _contents;
        public bool ContentsLoading { get; private set; }
        public string ContentsMessage { get; private set; } = string.Empty;
        public bool CanReadContents => Current != null && (Current.format == "pdf" || Current.format == "epub" ||
            Current.format == "mobi" || Current.format == "azw" || Current.format == "azw3");
        private List<NamedBookmark> _namedBookmarks = new List<NamedBookmark>();
        private List<ContentsEntry> _contents = new List<ContentsEntry>();
        // Búsqueda en el libro abierto: resultados acumulados y cursor para «Más resultados».
        public const int SearchBatch = 20;
        public IReadOnlyList<SearchHit> SearchResults => _searchHits;
        public string SearchQuery { get; private set; } = string.Empty;
        public string SearchMessage { get; private set; } = string.Empty;
        public bool SearchLoading { get; private set; }
        public bool SearchHasMore => !string.IsNullOrEmpty(_searchNext);
        public bool CanSearch => Current != null && PdfPageRenderer.IsSearchable(Current.path);
        private readonly List<SearchHit> _searchHits = new List<SearchHit>();
        private string _searchNext;
        private int _searchGeneration;
        private Coroutine _searchRoutine;
        private int _contentsGeneration;
        private Coroutine _contentsRoutine;
        public BookEntry Current { get; private set; }
        public bool Importing => _importState == "picking" || _importState == "copying";
        public string ImportLine { get; private set; } = string.Empty;
        // Carga desde el PC por Wi-Fi (encargo 12).
        public bool WifiRunning { get; private set; }
        public string WifiUrl { get; private set; } = string.Empty;
        public string WifiCode { get; private set; } = string.Empty;
        public int WifiReceived { get; private set; }
        private int _wifiSerial = -1;
        private float _nextWifiPoll;
        public bool IsBookmarked => Current != null && _bookmarks.Contains(Current.position);

        private List<BookEntry> _books = new List<BookEntry>();
        private List<int> _bookmarks = new List<int>();
        private FolioBook _book;
        private string _importState = "idle";
        private int _lastSerial = -1;
        private float _saveAt, _nextPoll, _nextFind;
        private bool _dirty, _polling, _hooked;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass _java;
        private AndroidJavaObject _activity;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindFirstObjectByType<BookLibrary>(FindObjectsInactive.Include) == null)
                new GameObject("Biblioteca (Folio XR)").AddComponent<BookLibrary>();
        }

        private void Awake()
        {
            Instance = this;
            PdfPageRenderer.PageCounted += OnPageCounted;
            PdfPageRenderer.RenderFailed += OnRenderFailed;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _java = new AndroidJavaClass(JavaClass);
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    _activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                Available = _activity != null && _java.CallStatic<bool>("init", _activity);
            }
            catch (Exception e)
            {
                Available = false;
                Debug.LogWarning("[FolioXR] Biblioteca: no se pudo iniciar el plugin: " + e.Message);
            }
#endif
            Reload();
            MigrateStyles();
            Debug.Log("[FolioXR] Biblioteca: " + (Available ? _books.Count + " libro(s)" : "no disponible fuera del visor"));
            Hook();
            OpenLastRead();
        }

        private void Hook()
        {
            if (_hooked) return;
            if (_book == null) _book = FindFirstObjectByType<FolioBook>();
            if (_book == null) return;
            _book.SpreadShown += OnSpreadShown;
            _book.DocumentFailed += OnDocumentFailed;
            _hooked = true;
        }

        /// <summary>Al arrancar vuelve al último libro leído, en su página.</summary>
        private void OpenLastRead()
        {
            if (!Available || _book == null) return;
            foreach (BookEntry entry in _books)
            {
                if (entry.lastRead <= 0) break; // Vienen ordenados por última lectura.
                if (!entry.CanOpen || !File.Exists(entry.path)) continue;
                _book.RequestDocument(entry.path, entry.id, entry.position);
                Debug.Log("[FolioXR] Biblioteca: se retoma «" + entry.title + "» en la página " + (entry.position + 1));
                return;
            }
        }

        private void Reload()
        {
            string openId = Current != null ? Current.id : null;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Available)
            {
                try
                {
                    BookList list = JsonUtility.FromJson<BookList>(_java.CallStatic<string>("listBooksJson"));
                    _books = list != null && list.books != null ? list.books : new List<BookEntry>();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[FolioXR] Biblioteca: no se pudo leer la lista: " + e.Message);
                }
            }
#endif
            Current = openId != null ? Find(openId) : null;
        }

        private BookEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (BookEntry entry in _books)
                if (entry.id == id) return entry;
            return null;
        }

        private void LoadBookmarks()
        {
            _bookmarks = new List<int>();
            _namedBookmarks = new List<NamedBookmark>();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available || Current == null) return;
            try
            {
                BookmarkList list = JsonUtility.FromJson<BookmarkList>(_java.CallStatic<string>("bookmarksJson", Current.id));
                if (list != null && list.positions != null) _bookmarks = list.positions;
                if (list != null && list.entries != null) _namedBookmarks = list.entries;
                if (_namedBookmarks.Count == 0) foreach (int p in _bookmarks) _namedBookmarks.Add(new NamedBookmark { position = p });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Biblioteca: no se pudieron leer los marcadores: " + e.Message);
            }
#endif
        }

        // ───────────────────────── Progreso ─────────────────────────

        private void OnSpreadShown(int leftPage, int pageCount)
        {
            BookEntry entry = Find(_book.DocumentId);
            if (!Repaginating && entry != null && entry != Current && PdfPageRenderer.IsReflow(entry.path) &&
                entry.pages > 1 && entry.pages != pageCount)
            {
                _repaginateId = entry.id;
                Repaginating = true;
                _awaitingPagination = false;
                _relocatePage = Mathf.Clamp(Mathf.RoundToInt(entry.position / (float)(entry.pages - 1) * (pageCount - 1)), 0, pageCount - 1);
                return;
            }
            if (Repaginating && _book.DocumentId == _repaginateId)
            {
                if (_awaitingPagination)
                {
                    if (!_paginationReady) return; // La doble página anterior puede terminar antes de comenzar la recarga.
                    _awaitingPagination = false;
                    _relocatePage = Mathf.Clamp(Mathf.RoundToInt(_epubProgress * (pageCount - 1)), 0, pageCount - 1);
                    return; // No guardar la posición provisional antes de reubicarla.
                }
                Repaginating = false;
            }
            if (entry != Current)
            {
                if (_dirty) Save(false); // La página del libro anterior no se pierde al cambiar.
                ClearContents();
                Current = entry;
                if (entry != null) ApplyStyle(entry);
                else if (_book.Cover != null) _book.Cover.Apply(false, 0, "Libro de muestra", _book.RightToLeft);
                LoadBookmarks();
                _dirty = entry != null; // Abrirlo ya lo vuelve «el último leído».
                _saveAt = Time.unscaledTime + 0.5f;
            }
            if (Current != null && (Current.position != leftPage || Current.pages != pageCount))
            {
                Current.position = leftPage;
                Current.pages = pageCount;
                _dirty = true;
                _saveAt = Time.unscaledTime + SaveDelay; // Varias páginas seguidas, una sola escritura.
            }
            StateChanged?.Invoke();
        }

        private void OnDocumentFailed(string id, string message)
        {
            if (id == _repaginateId) { Repaginating = _awaitingPagination = false; _relocatePage = -1; }
            BookEntry entry = Find(id);
            if (entry != null && entry.path == PdfPageRenderer.DrmFailurePath)
                message = "Este libro tiene DRM y no se puede abrir";
            if (entry != null && entry.path == PdfPageRenderer.FormatFailurePath &&
                ((PdfPageRenderer.FormatFailureDetail ?? string.Empty).StartsWith("CBR") ||
                 (PdfPageRenderer.FormatFailureDetail ?? string.Empty).StartsWith("Formato")))
                message = PdfPageRenderer.FormatFailureDetail;
            Notify(entry != null ? "«" + entry.title + "»\n" + message : message);
        }

        private void Save(bool sync)
        {
            _dirty = false;
            if (Current == null) return;
            Current.lastRead = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available) return;
            try
            {
                _java.CallStatic("setProgress", Current.id, Current.position, Current.pages, sync);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Biblioteca: no se pudo guardar el progreso: " + e.Message);
            }
#endif
        }

        // ───────────────────────── Acciones del menú ─────────────────────────

        public void Open(BookEntry entry)
        {
            if (entry == null || _book == null || Repaginating) return;
            if (!entry.CanOpen)
            {
                string reason = entry.format == "doc" ? "DOC binario requiere un conversor no incluido. Exporta a DOCX o PDF desde el PC." :
                    entry.format == "kfx" ? "No hay lector KFX incluido. Usa una edición EPUB/PDF sin DRM." :
                    "No hay lector para este formato; no se puede garantizar su estructura. Usa PDF o EPUB sin DRM.";
                Notify("Formato aún no compatible: «" + entry.title + "».\n" + reason);
                return;
            }
            if (!File.Exists(entry.path))
            {
                Notify("No se encontró la copia de «" + entry.title + "».\nQuítalo de la biblioteca y vuelve a importarlo.");
                return;
            }
            if (Current != null && Current.id == entry.id) return;
            ClearContents();
            _book.RequestDocument(entry.path, entry.id, entry.position);
            Debug.Log("[FolioXR] Biblioteca: abrir «" + entry.title + "» en la página " + (entry.position + 1));
        }

        public bool Repaginating { get; private set; }
        private string _repaginateId;
        private float _epubProgress;
        private bool _awaitingPagination, _paginationReady;
        private int _relocatePage = -1;

        private void OnPageCounted(string path)
        {
            if (Repaginating && Current != null && Current.path == path) _paginationReady = true;
        }

        private void OnRenderFailed(string path)
        {
            if (!Repaginating || (_book.DocumentId != _repaginateId && (Current == null || Current.path != path))) return;
            Repaginating = _awaitingPagination = false;
            _relocatePage = -1;
            Notify("No se pudo dibujar el libro. Vuelve a abrirlo para reintentar.");
            StateChanged?.Invoke();
        }

        public void SetEpubFontScale(float scale)
        {
            if (Repaginating) return;
            scale = Mathf.Clamp(scale, 0.8f, 1.25f);
            if (Mathf.Approximately(scale, PdfPageRenderer.EpubFontScale)) return;
            PlayerPrefs.SetFloat(PdfPageRenderer.EpubFontPrefsKey, scale);
            PlayerPrefs.Save();
            ReloadReflowCurrent();
        }

        public void SetReadingLayout(int font, float line, int margin)
        {
            if (Repaginating) return;
            PlayerPrefs.SetInt(ReadingLayout.FontKey, Mathf.Clamp(font, 0, 1));
            PlayerPrefs.SetFloat(ReadingLayout.LineKey, line == 1.2f || line == 1.8f ? line : 1.4f);
            PlayerPrefs.SetInt(ReadingLayout.MarginKey, margin == 24 || margin == 72 ? margin : 48);
            PlayerPrefs.Save();
            ReloadReflowCurrent();
        }

        public void SetTxtEncoding(string encoding)
        {
            if (Repaginating || (encoding != "auto" && encoding != "UTF-8" && encoding != "Windows-1252")) return;
            if (encoding == PdfPageRenderer.TxtEncoding) return;
            PlayerPrefs.SetString(PdfPageRenderer.TxtEncodingPrefsKey, encoding);
            PlayerPrefs.Save();
            if (Current != null && Current.format == "txt") ReloadReflowCurrent();
            else StateChanged?.Invoke();
        }

        private void ReloadReflowCurrent()
        {
            ClearContents();
            if (Current != null && PdfPageRenderer.IsReflow(Current.path))
            {
                _repaginateId = Current.id;
                _epubProgress = Current.pages > 1 ? Current.position / (float)(Current.pages - 1) : 0f;
                Repaginating = _awaitingPagination = true;
                _paginationReady = false;
                _book.RequestDocument(Current.path, Current.id, Current.position);
                Notify("Actualizando páginas del libro…");
            }
            StateChanged?.Invoke();
        }

        public void Remove(BookEntry entry)
        {
            if (entry == null || !Available || Repaginating) return;
            bool removed = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                removed = _java.CallStatic<bool>("removeBook", entry.id);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Biblioteca: no se pudo quitar: " + e.Message);
            }
#endif
            if (!removed)
            {
                Notify("No se pudo quitar «" + entry.title + "».");
                return;
            }
            if (Current != null && Current.id == entry.id)
            {
                ClearContents();
                _namedBookmarks.Clear();
                Current = null;
                _dirty = false;
                _bookmarks = new List<int>();
                if (_book != null) _book.OpenDefaultDocument();
            }
            Reload();
            StateChanged?.Invoke();
            Notify("Se quitó «" + entry.title + "» de la biblioteca.");
        }

        /// <summary>Cierra el libro abierto (guarda su página) y vuelve al de muestra.</summary>
        public void CloseCurrent()
        {
            if (Current == null || _book == null) return;
            ClearContents();
            Repaginating = _awaitingPagination = false;
            _relocatePage = -1;
            Save(false);
            Debug.Log("[FolioXR] Biblioteca: cerrar «" + Current.title + "»");
            _book.OpenDefaultDocument();
        }

        public void ToggleBookmark()
        {
            if (Current == null || !Available || Repaginating) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                bool marked = _java.CallStatic<bool>("toggleBookmark", Current.id, Current.position);
                Debug.Log("[FolioXR] Biblioteca: marcador " + (marked ? "puesto" : "quitado") + " en la página " +
                    (Current.position + 1));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Biblioteca: no se pudo cambiar el marcador: " + e.Message);
            }
#endif
            LoadBookmarks();
            StateChanged?.Invoke();
        }

        public void GoToBookmark(int position)
        {
            if (_book != null && Current != null && !Repaginating && _book.DocumentId == Current.id) _book.GoToPage(position);
        }

        public string BookmarkTitle(int page)
        {
            foreach (NamedBookmark bookmark in _namedBookmarks) if (bookmark.position == page) return bookmark.Title;
            return "Página " + (page + 1);
        }

        public void RenameBookmark(string bookId, int page, string label)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available || Find(bookId) == null || page < 0) return;
            if (!_java.CallStatic<bool>("renameBookmark", bookId, page, label ?? string.Empty))
            { Notify("No se pudo guardar el nombre del marcador."); return; }
#endif
            if (Current != null && Current.id == bookId) LoadBookmarks();
            StateChanged?.Invoke();
        }

        public void RemoveBookmark(int page)
        {
            if (Current == null || Repaginating) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available || !_java.CallStatic<bool>("removeBookmark", Current.id, page)) return;
#endif
            LoadBookmarks(); StateChanged?.Invoke();
        }

        public void CancelContents()
        {
            _contentsGeneration++;
            _book?.CancelContents();
            if (_contentsRoutine != null) StopCoroutine(_contentsRoutine);
            _contentsRoutine = null; ContentsLoading = false;
        }

        private void ClearContents()
        {
            CancelContents(); _contents.Clear(); ContentsMessage = string.Empty;
            ClearSearch();
        }

        public void CancelSearch()
        {
            _searchGeneration++;
            _book?.CancelSearch();
            if (_searchRoutine != null) StopCoroutine(_searchRoutine);
            _searchRoutine = null; SearchLoading = false;
        }

        private void ClearSearch()
        {
            CancelSearch(); _searchHits.Clear(); _searchNext = null; SearchMessage = string.Empty; SearchQuery = string.Empty;
        }

        /// <summary>Nueva búsqueda desde el principio del libro abierto.</summary>
        public void Search(string query)
        {
            query = (query ?? string.Empty).Trim();
            if (Current == null || _book == null || Repaginating) return;
            ClearSearch(); SearchQuery = query;
            if (query.Length == 0) { SearchMessage = "Escribe qué buscar."; StateChanged?.Invoke(); return; }
            if (!CanSearch) { SearchMessage = "Los PDF y cómics se muestran como imagen: no tienen texto que buscar aquí."; StateChanged?.Invoke(); return; }
            SearchLoading = true;
            _searchRoutine = StartCoroutine(RunSearch(Current.id, string.Empty, _searchGeneration));
            StateChanged?.Invoke();
        }

        /// <summary>Siguiente lote de coincidencias de la misma búsqueda.</summary>
        public void SearchMore()
        {
            if (!SearchHasMore || SearchLoading || Current == null || _book == null || Repaginating) return;
            SearchLoading = true;
            _searchRoutine = StartCoroutine(RunSearch(Current.id, _searchNext, _searchGeneration));
            StateChanged?.Invoke();
        }

        private IEnumerator RunSearch(string id, string cursor, int generation)
        {
            yield return _book.Search(SearchQuery, cursor, SearchBatch, (list, error) =>
            {
                if (generation != _searchGeneration || Current == null || Current.id != id) return;
                if (list != null && list.hits != null) _searchHits.AddRange(list.hits);
                _searchNext = error == null ? list?.next : null;
                SearchMessage = error ?? (_searchHits.Count == 0 ? "Sin coincidencias para «" + SearchQuery + "»." : list?.message ?? string.Empty);
                SearchLoading = false;
                StateChanged?.Invoke();
            });
            if (generation == _searchGeneration) _searchRoutine = null;
        }

        public void LoadContents()
        {
            if (!CanReadContents || Repaginating || _book == null) return;
            CancelContents(); _contents.Clear(); ContentsMessage = string.Empty; ContentsLoading = true;
            _contentsRoutine = StartCoroutine(ReadContents(Current.id, _contentsGeneration));
            StateChanged?.Invoke();
        }

        private IEnumerator ReadContents(string id, int generation)
        {
            yield return _book.ReadContents((list, error) =>
            {
                if (generation != _contentsGeneration || Current == null || Current.id != id) return;
                _contents = list?.entries ?? new List<ContentsEntry>();
                ContentsMessage = error ?? (_contents.Count == 0 ? "Este libro no tiene índice navegable." : list?.message ?? string.Empty);
                ContentsLoading = false;
                StateChanged?.Invoke();
            });
            if (generation == _contentsGeneration) _contentsRoutine = null;
        }

        public void Import()
        {
            if (!Available || Importing) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _java.CallStatic("pickAndImport", _activity);
                _polling = true;
                _nextPoll = 0f;
                Debug.Log("[FolioXR] Importar: se pidió el selector de archivos del sistema");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Importar: no se pudo abrir el selector: " + e.Message);
                Notify("El selector de archivos no está disponible en este visor.\nPronto podrás cargar libros desde el PC por Wi-Fi.");
            }
#endif
        }

        public void CancelImport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available) return;
            try
            {
                _java.CallStatic("cancelImport");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Importar: no se pudo cancelar: " + e.Message);
            }
#endif
        }

        /// <summary>Abre el servidor local: el PC sube libros desde su navegador con un código de 6 dígitos.</summary>
        public void StartWifi()
        {
            if (!Available) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var server = new AndroidJavaClass(WifiJavaClass))
                {
                    WifiStatus status = JsonUtility.FromJson<WifiStatus>(server.CallStatic<string>("start"));
                    if (status == null || !string.IsNullOrEmpty(status.error) || string.IsNullOrEmpty(status.url))
                    {
                        Notify(status != null && !string.IsNullOrEmpty(status.error) ? status.error
                            : "No se pudo abrir la carga por Wi-Fi.");
                        return;
                    }
                    WifiRunning = true;
                    WifiUrl = status.url;
                    WifiCode = status.code;
                    WifiReceived = 0;
                    _wifiSerial = -1;
                    Debug.Log("[FolioXR] Wi-Fi: abierto en " + WifiUrl);
                    Notify("En el navegador de tu PC abre\n" + WifiUrl + "\nCódigo: " + WifiCode);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Wi-Fi: no se pudo abrir: " + e.Message);
                Notify("No se pudo abrir la carga por Wi-Fi.");
            }
#endif
            StateChanged?.Invoke();
        }

        public void StopWifi()
        {
            if (!WifiRunning) return;
            WifiRunning = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var server = new AndroidJavaClass(WifiJavaClass)) server.CallStatic("stop");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Wi-Fi: no se pudo cerrar: " + e.Message);
            }
#endif
            Debug.Log("[FolioXR] Wi-Fi: cerrado");
            StateChanged?.Invoke();
        }

        private void PollWifi()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var server = new AndroidJavaClass(WifiJavaClass))
                {
                    WifiStatus status = JsonUtility.FromJson<WifiStatus>(server.CallStatic<string>("statusJson"));
                    if (status == null || status.serial == _wifiSerial) return;
                    bool first = _wifiSerial < 0;
                    _wifiSerial = status.serial;
                    bool arrived = status.received != WifiReceived;
                    WifiReceived = status.received;
                    if (arrived) Reload();
                    if (!status.running)
                    {
                        WifiRunning = false;
                        if (!string.IsNullOrEmpty(status.message)) Notify(status.message);
                    }
                    else if (!first && status.total == 0 && !string.IsNullOrEmpty(status.message)) Notify(status.message);
                    if (status.running && status.total > 0 && status.copied < status.total)
                        Notify("Recibiendo por Wi-Fi: " + (100.0 * status.copied / status.total).ToString("F0") + "%");
                    StateChanged?.Invoke();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Wi-Fi: no se pudo leer el estado: " + e.Message);
                WifiRunning = false;
                StateChanged?.Invoke();
            }
#endif
        }

        // ── Libros nuevos: nombre, lectura y tapas (pedido del usuario, 11 nov 2026) ──
        private const string StylePrefix = "folioxr.book.";
        private const string StyleMigratedKey = "folioxr.bookStyleMigrated";

        public static bool IsConfigured(BookEntry entry) => PlayerPrefs.GetInt(StylePrefix + entry.id + ".setup", 0) != 0;
        public static bool OwnCovers(BookEntry entry) => PlayerPrefs.GetInt(StylePrefix + entry.id + ".covers", 0) != 0;
        public static int Leather(BookEntry entry) => PlayerPrefs.GetInt(StylePrefix + entry.id + ".leather", 0);
        public bool RightToLeft(BookEntry entry) => FolioXR.Reading.FolioBook.GetRightToLeft(entry.id);

        /// <summary>Primer libro agregado que todavía no se configuró (el menú lo muestra primero).</summary>
        public BookEntry PendingSetup
        {
            get
            {
                foreach (BookEntry entry in _books)
                    if (!IsConfigured(entry)) return entry;
                return null;
            }
        }

        public void SetOwnCovers(BookEntry entry, bool value)
        {
            PlayerPrefs.SetInt(StylePrefix + entry.id + ".covers", value ? 1 : 0);
            ApplyIfCurrent(entry);
        }

        public void SetLeather(BookEntry entry, int index)
        {
            PlayerPrefs.SetInt(StylePrefix + entry.id + ".leather", index);
            ApplyIfCurrent(entry);
        }

        public static bool SingleCover(BookEntry entry) => entry != null && FolioBook.GetSingleCover(entry.id);

        public void SetSingleCover(BookEntry entry, bool value)
        {
            if (entry == null || Repaginating) return;
            if (_book != null) _book.SetSingleCoverFor(entry.id, value);
            else
            {
                PlayerPrefs.SetInt(FolioBook.SingleCoverPrefsPrefix + entry.id, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            StateChanged?.Invoke();
        }

        public void SetRightToLeft(BookEntry entry, bool value)
        {
            if (_book != null) _book.SetRightToLeftFor(entry.id, value);
            else PlayerPrefs.SetInt("folioxr.rtl." + entry.id, value ? 1 : 0);
        }

        public void FinishSetup(BookEntry entry)
        {
            PlayerPrefs.SetInt(StylePrefix + entry.id + ".setup", 1);
            PlayerPrefs.Save();
            Debug.Log("[FolioXR] Biblioteca: «" + entry.title + "» configurado (" +
                (OwnCovers(entry) ? "tapas propias" : "cuero " + FolioXR.Reading.BookCover.LeatherNames[Mathf.Clamp(Leather(entry), 0, 6)]) +
                ", " + (RightToLeft(entry) ? "manga" : "normal") + ")");
            ApplyIfCurrent(entry);
            StateChanged?.Invoke();
        }

        public void Rename(BookEntry entry, string title)
        {
            title = title == null ? string.Empty : title.Trim();
            if (title.Length == 0 || title == entry.title) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (!_java.CallStatic<bool>("renameBook", entry.id, title)) return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Biblioteca: no se pudo renombrar: " + e.Message);
                return;
            }
#endif
            entry.title = title;
            ApplyIfCurrent(entry);
            StateChanged?.Invoke();
        }

        private void ApplyIfCurrent(BookEntry entry)
        {
            PlayerPrefs.Save();
            if (Current != null && entry != null && Current.id == entry.id) ApplyStyle(entry);
        }

        private void ApplyStyle(BookEntry entry)
        {
            if (_book == null || _book.Cover == null || entry == null) return;
            _book.Cover.Apply(OwnCovers(entry), Leather(entry), entry.title, RightToLeft(entry));
        }

        /// <summary>Los libros que ya existían antes de esta versión no piden configuración.</summary>
        private void MigrateStyles()
        {
            if (PlayerPrefs.GetInt(StyleMigratedKey, 0) != 0) return;
            foreach (BookEntry entry in _books) PlayerPrefs.SetInt(StylePrefix + entry.id + ".setup", 1);
            PlayerPrefs.SetInt(StyleMigratedKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Relee la lista desde SQLite (por ejemplo, tras una descarga OPDS).</summary>
        public void ReloadBooks()
        {
            Reload();
            StateChanged?.Invoke();
        }

        /// <summary>Aviso frente al usuario desde otras partes de la biblioteca (OPDS).</summary>
        public static void NotifyUser(string message) => Notify(message);

        private static void Notify(string message)
        {
            Debug.Log("[FolioXR] Aviso: " + message.Replace("\n", " "));
            Notice?.Invoke(message);
        }

        // ───────────────────────── Ciclo ─────────────────────────

        private void Update()
        {
            // SpreadShown ocurre antes de liberar _busy en FolioBook: reintentar en el siguiente cuadro.
            if (_relocatePage >= 0 && _book != null && _book.DocumentId == _repaginateId && _book.GoToPage(_relocatePage))
                _relocatePage = -1;
            if (!_hooked && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                Hook();
            }
            if (_dirty && Time.unscaledTime >= _saveAt) Save(false);
            if (WifiRunning && Time.unscaledTime >= _nextWifiPoll)
            {
                _nextWifiPoll = Time.unscaledTime + 1f;
                PollWifi();
            }
            if (_polling && Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollInterval;
                Poll();
            }
        }

        private void Poll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ImportStatus status;
            try
            {
                status = JsonUtility.FromJson<ImportStatus>(_java.CallStatic<string>("importStatusJson"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] Importar: no se pudo leer el estado: " + e.Message);
                _polling = false;
                _importState = "idle";
                StateChanged?.Invoke();
                return;
            }
            if (status == null || status.serial == _lastSerial) return;
            _lastSerial = status.serial;
            _importState = status.state ?? "idle";
            switch (_importState)
            {
                case "picking":
                    ImportLine = "Elige los archivos\nen el selector";
                    break;
                case "copying":
                    int percent = status.total > 0 ? Mathf.Clamp((int)(100.0 * status.copied / status.total), 0, 100) : 0;
                    ImportLine = "Importando " + Mathf.Max(1, status.index) + " de " + Mathf.Max(1, status.count) +
                        "\n" + (status.total > 0 ? percent + "%" : (status.copied / (1024.0 * 1024)).ToString("F1") + " MiB copiados");
                    break;
                default:
                    // Terminó: hecho, con error, cancelado o sin selector en este visor.
                    _polling = false;
                    string message = status.message ?? string.Empty;
                    if (_importState == "unavailable")
                    {
                        Debug.LogWarning("[FolioXR] Importar: Horizon OS no abrió el selector de archivos (SAF).");
                        message = "El selector de archivos no está disponible en este visor.\n" +
                            "Pronto podrás cargar libros desde el PC por Wi-Fi.";
                    }
                    _importState = "idle";
                    ImportLine = string.Empty;
                    Reload();
                    if (message.Length > 0) Notify(message);
                    break;
            }
            StateChanged?.Invoke();
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _dirty) Save(true); // Escritura inmediata: el sistema puede cerrar la app en pausa.
            if (paused) StopWifi();           // El servidor no queda abierto con el visor suspendido.
            if (!paused) _nextPoll = 0f;      // Al volver del selector, leer el estado enseguida.
        }

        private void OnApplicationQuit()
        {
            StopWifi();
            if (_dirty) Save(true);
        }

        private void OnDestroy()
        {
            CancelContents();
            PdfPageRenderer.PageCounted -= OnPageCounted;
            PdfPageRenderer.RenderFailed -= OnRenderFailed;
            if (_hooked && _book != null)
            {
                _book.SpreadShown -= OnSpreadShown;
                _book.DocumentFailed -= OnDocumentFailed;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_java != null) _java.Dispose();
            if (_activity != null) _activity.Dispose();
#endif
            if (Instance == this) Instance = null;
        }
    }
}
