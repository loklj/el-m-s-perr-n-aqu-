using System;
using System.Collections.Generic;
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
        /// <summary>Formatos que el libro 3D ya abre: PDF, EPUB y cómics CBZ (o ZIP de imágenes).</summary>
        public bool CanOpen => format == "pdf" || format == "cbz" || format == "zip" || format == "epub";

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
    internal sealed class BookmarkList
    {
        public List<int> positions = new List<int>();
    }

    [Serializable]
    internal sealed class WifiStatus
    {
        public bool running;
        public string url, code, message, error;
        public int received, serial;
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
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available || Current == null) return;
            try
            {
                BookmarkList list = JsonUtility.FromJson<BookmarkList>(_java.CallStatic<string>("bookmarksJson", Current.id));
                if (list != null && list.positions != null) _bookmarks = list.positions;
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
            if (!Repaginating && entry != null && entry != Current && entry.format == "epub" &&
                entry.pages > 1 && entry.pages != pageCount)
            {
                _repaginateId = entry.id;
                Repaginating = true;
                _awaitingPagination = false;
                _relocatePage = Mathf.Clamp(Mathf.RoundToInt(entry.position / (float)(entry.pages - 1) * (pageCount - 1)), 0, pageCount - 1);
                _relocatePage -= _relocatePage % 2;
                return;
            }
            if (Repaginating && _book.DocumentId == _repaginateId)
            {
                if (_awaitingPagination)
                {
                    if (!_paginationReady) return; // La doble página anterior puede terminar antes de comenzar la recarga.
                    _awaitingPagination = false;
                    _relocatePage = Mathf.Clamp(Mathf.RoundToInt(_epubProgress * (pageCount - 1)), 0, pageCount - 1);
                    _relocatePage -= _relocatePage % 2;
                    return; // No guardar la posición provisional antes de reubicarla.
                }
                Repaginating = false;
            }
            if (entry != Current)
            {
                if (_dirty) Save(false); // La página del libro anterior no se pierde al cambiar.
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
                Notify("Formato aún no compatible: «" + entry.title + "» (" + entry.format.ToUpperInvariant() +
                    ").\nSe podrá abrir en una próxima versión.");
                return;
            }
            if (!File.Exists(entry.path))
            {
                Notify("No se encontró la copia de «" + entry.title + "».\nQuítalo de la biblioteca y vuelve a importarlo.");
                return;
            }
            if (Current != null && Current.id == entry.id) return;
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
            Notify("No se pudo dibujar el EPUB. Vuelve a abrirlo para reintentar.");
            StateChanged?.Invoke();
        }

        public void SetEpubFontScale(float scale)
        {
            if (Repaginating) return;
            scale = Mathf.Clamp(scale, 0.8f, 1.25f);
            if (Mathf.Approximately(scale, PdfPageRenderer.EpubFontScale)) return;
            PlayerPrefs.SetFloat(PdfPageRenderer.EpubFontPrefsKey, scale);
            PlayerPrefs.Save();
            if (Current != null && Current.format == "epub")
            {
                _repaginateId = Current.id;
                _epubProgress = Current.pages > 1 ? Current.position / (float)(Current.pages - 1) : 0f;
                Repaginating = _awaitingPagination = true;
                _paginationReady = false;
                _book.RequestDocument(Current.path, Current.id, Current.position);
                Notify("Actualizando páginas del EPUB…");
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
            Save(false);
            Debug.Log("[FolioXR] Biblioteca: cerrar «" + Current.title + "»");
            _book.OpenDefaultDocument();
        }

        public void ToggleBookmark()
        {
            if (Current == null || !Available) return;
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
            if (_book != null) _book.GoToPage(position);
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
                    else if (arrived && !first && !string.IsNullOrEmpty(status.message)) Notify(status.message);
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
                    int percent = status.total > 0 ? Mathf.Clamp((int)(status.copied * 100L / status.total), 0, 100) : 0;
                    ImportLine = "Importando " + Mathf.Max(1, status.index) + " de " + Mathf.Max(1, status.count) +
                        "\n" + percent + "%";
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
