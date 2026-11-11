using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Books
{
    [Serializable]
    public sealed class OpdsServer
    {
        public string id, name, url;
    }

    [Serializable]
    internal sealed class OpdsServerList
    {
        public List<OpdsServer> servers = new List<OpdsServer>();
    }

    [Serializable]
    public sealed class OpdsBook
    {
        public string href, type;
        public bool drm;

        public string FormatName
        {
            get
            {
                string t = (type ?? string.Empty).ToLowerInvariant();
                return t.Contains("epub") ? "EPUB" : t.Contains("pdf") ? "PDF" : t.Contains("rar") || t.Contains("cbr") ? "CBR"
                    : t.Contains("zip") || t.Contains("cbz") ? "CBZ" : t.Contains("mobi") ? "MOBI" : t.Contains("azw") ? "AZW3"
                    : t.StartsWith("text/plain", StringComparison.Ordinal) ? "TXT" : "archivo";
            }
        }

        /// <summary>Preferencia al descargar: lo que el libro 3D ya abre mejor.</summary>
        public int Rank
        {
            get
            {
                switch (FormatName)
                {
                    case "EPUB": return 0;
                    case "PDF": return 1;
                    case "CBZ": return 2;
                    case "MOBI": case "AZW3": return 3;
                    case "TXT": return 4;
                    default: return 5;
                }
            }
        }
    }

    [Serializable]
    public sealed class OpdsEntry
    {
        public string title, author, nav;
        public List<OpdsBook> books = new List<OpdsBook>();

        public bool IsNavigation => !string.IsNullOrEmpty(nav) && (books == null || books.Count == 0);

        /// <summary>Mejor adquisición legible, o null si todas tienen DRM.</summary>
        public OpdsBook Best
        {
            get
            {
                OpdsBook best = null;
                if (books != null)
                    foreach (OpdsBook book in books)
                        if (!book.drm && (best == null || book.Rank < best.Rank)) best = book;
                return best;
            }
        }
    }

    [Serializable]
    public sealed class OpdsFeed
    {
        public string title, next, prev, search;
        public List<OpdsEntry> entries = new List<OpdsEntry>();
    }

    /// <summary>
    /// Encargo 14 (escrito por Claude). Navegación de catálogos OPDS desde el menú: servidores, carpetas,
    /// páginas y descarga a la biblioteca. Los servidores se agregan desde la página del PC (carga por Wi-Fi).
    /// </summary>
    public sealed class OpdsBrowser : MonoBehaviour
    {
        private const string JavaClass = "com.loklj.folioxr.FolioOpds";
        public const int EntriesPerPage = 6;

        public static OpdsBrowser Instance { get; private set; }
        public static event Action StateChanged;

        public bool Available { get; private set; }
        public IReadOnlyList<OpdsServer> Servers => _servers;
        public OpdsServer Server { get; private set; }
        public OpdsFeed Feed { get; private set; }
        public bool Browsing => Server != null;
        public bool Loading => _feedKey != null;
        public string Error { get; private set; }
        public int LocalPage { get; private set; }
        public bool Downloading => _downloadKey != null;
        public string DownloadLine { get; private set; } = string.Empty;
        public int Depth => _history.Count;

        private List<OpdsServer> _servers = new List<OpdsServer>();
        private readonly List<string> _history = new List<string>(); // URL de cada nivel ("" = raíz).
        private string _feedKey, _downloadKey, _downloadTitle, _lastStatus;
        private float _nextPoll, _nextServerRefresh;
        private int _serial;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaClass _java;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindFirstObjectByType<OpdsBrowser>(FindObjectsInactive.Include) == null)
                new GameObject("Catálogos OPDS (Folio XR)").AddComponent<OpdsBrowser>();
        }

        private void Awake()
        {
            Instance = this;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _java = new AndroidJavaClass(JavaClass);
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _java.CallStatic("init", activity);
                Available = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] OPDS: no disponible: " + e.Message);
            }
#endif
            RefreshServers();
        }

        /// <summary>Relee la lista (los servidores se agregan desde el PC mientras el visor está abierto).</summary>
        public void RefreshServers()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available) return;
            try
            {
                OpdsServerList list = JsonUtility.FromJson<OpdsServerList>(_java.CallStatic<string>("listServersJson"));
                List<OpdsServer> servers = list != null && list.servers != null ? list.servers : new List<OpdsServer>();
                bool changed = servers.Count != _servers.Count;
                for (int i = 0; !changed && i < servers.Count; i++) changed = servers[i].id != _servers[i].id;
                _servers = servers;
                if (changed) StateChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] OPDS: no se pudo leer la lista de catálogos: " + e.Message);
            }
#endif
        }

        public void OpenServer(OpdsServer server)
        {
            if (server == null) return;
            Server = server;
            _history.Clear();
            Request(string.Empty);
            Debug.Log("[FolioXR] OPDS: abrir «" + server.name + "»");
        }

        public void OpenNav(string url)
        {
            if (string.IsNullOrEmpty(url) || Server == null) return;
            Request(url);
        }

        public void NextRemotePage()
        {
            if (Feed != null && !string.IsNullOrEmpty(Feed.next)) Request(Feed.next);
        }

        public void NextLocalPage()
        {
            if (Feed == null || Feed.entries == null) return;
            int pages = Mathf.Max(1, (Feed.entries.Count + EntriesPerPage - 1) / EntriesPerPage);
            LocalPage = (LocalPage + 1) % pages;
            StateChanged?.Invoke();
        }

        public void Back()
        {
            if (_history.Count <= 1)
            {
                Exit();
                return;
            }
            _history.RemoveAt(_history.Count - 1);
            string previous = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            Request(previous);
        }

        public void Exit()
        {
            CancelFeed();
            Server = null;
            Feed = null;
            Error = null;
            _history.Clear();
            StateChanged?.Invoke();
        }

        public void RemoveServer(OpdsServer server)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Available || server == null) return;
            try
            {
                _java.CallStatic<bool>("removeServer", server.id);
                Debug.Log("[FolioXR] OPDS: catálogo quitado «" + server.name + "»");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolioXR] OPDS: no se pudo quitar: " + e.Message);
            }
#endif
            if (Server != null && server != null && Server.id == server.id) Exit();
            RefreshServers();
            StateChanged?.Invoke();
        }

        public void Download(OpdsEntry entry)
        {
            OpdsBook book = entry != null ? entry.Best : null;
            if (book == null || Server == null || Downloading) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            _downloadKey = "opds-descarga-" + (++_serial);
            _downloadTitle = string.IsNullOrEmpty(entry.title) ? "libro" : entry.title;
            DownloadLine = "Descargando «" + Shorten(_downloadTitle) + "»…";
            try
            {
                _java.CallStatic("requestDownload", Server.id, book.href, book.type ?? string.Empty, _downloadTitle,
                    _downloadKey);
                Debug.Log("[FolioXR] OPDS: descargar «" + _downloadTitle + "» (" + book.FormatName + ")");
            }
            catch (Exception e)
            {
                _downloadKey = null;
                BookLibrary.NotifyUser("No se pudo iniciar la descarga.");
                Debug.LogWarning("[FolioXR] OPDS: " + e.Message);
            }
#endif
            StateChanged?.Invoke();
        }

        private void Request(string url)
        {
            CancelFeed();
            Error = null;
            LocalPage = 0;
            _history.Add(url ?? string.Empty);
#if UNITY_ANDROID && !UNITY_EDITOR
            _feedKey = "opds-catalogo-" + (++_serial);
            try
            {
                _java.CallStatic("requestFeed", Server.id, url ?? string.Empty, _feedKey);
            }
            catch (Exception e)
            {
                _feedKey = null;
                Error = "No se pudo leer el catálogo.";
                Debug.LogWarning("[FolioXR] OPDS: " + e.Message);
            }
#endif
            StateChanged?.Invoke();
        }

        private void CancelFeed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_feedKey != null) _java.CallStatic("clearStatus", _feedKey);
#endif
            _feedKey = null;
        }

        private static string Shorten(string text) => text.Length <= 28 ? text : text.Substring(0, 27) + "…";

        private void Update()
        {
            if (!Available || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime >= _nextServerRefresh)
            {
                _nextServerRefresh = Time.unscaledTime + 3f;
                RefreshServers();
            }
            if (_feedKey != null)
            {
                string status = _java.CallStatic<string>("getStatus", _feedKey);
                if (status != null && status != "PENDING")
                {
                    _java.CallStatic("clearStatus", _feedKey);
                    _feedKey = null;
                    if (status.StartsWith("OK:", StringComparison.Ordinal))
                    {
                        Feed = JsonUtility.FromJson<OpdsFeed>(status.Substring(3)) ?? new OpdsFeed();
                        if (Feed.entries == null) Feed.entries = new List<OpdsEntry>();
                        Debug.Log("[FolioXR] OPDS: «" + Feed.title + "», " + Feed.entries.Count + " entradas");
                    }
                    else
                    {
                        Error = status.StartsWith("ERROR:", StringComparison.Ordinal) ? status.Substring(6) : status;
                        Debug.LogWarning("[FolioXR] OPDS: " + Error);
                    }
                    StateChanged?.Invoke();
                }
            }
            if (_downloadKey != null)
            {
                string status = _java.CallStatic<string>("getStatus", _downloadKey);
                if (status == null || status == _lastStatus) return;
                _lastStatus = status;
                if (status.StartsWith("PENDING:", StringComparison.Ordinal))
                {
                    string[] parts = status.Substring(8).Split('/');
                    long copied = parts.Length > 0 && long.TryParse(parts[0], out long c) ? c : 0;
                    long total = parts.Length > 1 && long.TryParse(parts[1], out long t) ? t : 0;
                    DownloadLine = "Descargando «" + Shorten(_downloadTitle) + "»\n" +
                        (total > 0 ? Mathf.Clamp((int)(copied * 100 / total), 0, 100) + "%" : (copied >> 20) + " MB");
                }
                else
                {
                    _java.CallStatic("clearStatus", _downloadKey);
                    _downloadKey = null;
                    DownloadLine = string.Empty;
                    bool ok = status.StartsWith("OK:", StringComparison.Ordinal);
                    string message = status.Substring(status.IndexOf(':') + 1);
                    if (ok && BookLibrary.Instance != null) BookLibrary.Instance.ReloadBooks();
                    BookLibrary.NotifyUser(message);
                }
                StateChanged?.Invoke();
            }
#endif
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_java != null) _java.Dispose();
#endif
            if (Instance == this) Instance = null;
        }
    }
}
