using System;
using System.Collections.Generic;
using System.Globalization;
using FolioXR.Books;
using FolioXR.Environments;
using FolioXR.Reading;
using FolioXR.Tools;
using FolioXR.Tools.Ink;
using FolioXR.Locomotion;
using UnityEngine;

namespace FolioXR.Menu
{
    public enum MenuCategory { Biblioteca, Entornos, Ajustes, ImportarYNubes, Hora }

    /// <summary>Contrato de presentación compartido; no depende de paneles ni de esferas.</summary>
    public interface IMenuView
    {
        void Bind(MenuModel model, Action close);
        void SetOpen(bool open, Transform head);
        void Refresh();
        /// <summary>Deja de escuchar al modelo (al cambiar de estilo solo la vista activa se actualiza).</summary>
        void Unbind();
        void ShowNotice(string message, Transform head);
    }

    public sealed class MenuOption
    {
        public string Text { get; }
        public Func<bool> Enabled { get; }
        public Func<bool> Marked { get; }
        public Action Action { get; }

        public MenuOption(string text, Action action = null, Func<bool> enabled = null, Func<bool> marked = null)
        {
            Text = text;
            Action = action;
            Enabled = enabled ?? (() => action != null);
            Marked = marked ?? (() => false);
        }
    }

    public sealed class MenuModel
    {
        public static readonly IReadOnlyList<string> CategoryNames = Array.AsReadOnly(new[]
            { "Biblioteca", "Entornos", "Ajustes", "Importar y nubes", "Hora" });
        public MenuCategory SelectedCategory { get; private set; }
        public IReadOnlyList<MenuOption> Options => _options;
        public int TimerMinutes { get; private set; }
        public event Action Changed;
        public event Action TimerElapsed;

        private readonly FolioBook _book;
        private readonly FolioEnvironmentController _environment;
        private readonly Func<bool> _sphereStyle;
        private readonly Action<bool> _setSphereStyle;
        private IReadOnlyList<MenuOption> _options;
        private bool _lastEnvironmentActive;
        private float _lastRenderScale;
        private double _timerEnd;
        // Borrar trazos pide un segundo toque antes de 4 s (Tanda D).
        private int _pendingClear; // 0 = nada, 1 = página, 2 = casa.
        private float _pendingClearUntil;
        // Biblioteca: seis libros por página; quitar pide elegir el libro y un segundo toque antes de 4 s.
        private const int BooksPerPage = 6;
        private int _libraryPage;
        private int _themePage, _pointCursor;

        /// <summary>Parte un nombre largo en dos líneas por el espacio más cercano al centro.</summary>
        private static string TwoLines(string text)
        {
            if (text.Length <= 12) return text;
            int best = -1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - text.Length / 2) < Mathf.Abs(best - text.Length / 2))) best = i;
            return best < 0 ? text : text.Substring(0, best) + "\n" + text.Substring(best + 1);
        }
        private bool _removing;
        private string _removeId;
        private float _removeUntil;

        public MenuModel(FolioBook book, FolioEnvironmentController environment, Func<bool> sphereStyle,
            Action<bool> setSphereStyle)
        {
            _book = book;
            _environment = environment;
            _sphereStyle = sphereStyle;
            _setSphereStyle = setSphereStyle;
            BookLibrary.StateChanged += OnLibraryChanged;
            OpdsBrowser.StateChanged += OnLibraryChanged;
            RebuildOptions();
        }

        private void OnLibraryChanged()
        {
            if (SelectedCategory == MenuCategory.Biblioteca || SelectedCategory == MenuCategory.ImportarYNubes)
                RebuildOptions();
            Changed?.Invoke();
        }

        private void TapBook(BookEntry entry)
        {
            BookLibrary library = BookLibrary.Instance;
            if (library == null) return;
            if (!_removing)
            {
                library.Open(entry);
                return;
            }
            if (_removeId != entry.id || Time.unscaledTime >= _removeUntil)
            {
                _removeId = entry.id;
                _removeUntil = Time.unscaledTime + 4f;
            }
            else
            {
                _removeId = null;
                _removing = false;
                library.Remove(entry);
            }
            RebuildOptions();
        }

        private static string Shorten(string text, int length) =>
            string.IsNullOrEmpty(text) ? "(sin título)" : text.Length <= length ? text : text.Substring(0, length - 1) + "…";

        public void Select(MenuCategory category)
        {
            if (SelectedCategory == category) return;
            SelectedCategory = category;
            RebuildOptions();
            Changed?.Invoke();
        }

        public void Invoke(int index)
        {
            if (index < 0 || index >= _options.Count) return;
            MenuOption option = _options[index];
            if (!option.Enabled()) return;
            option.Action?.Invoke();
            Changed?.Invoke(); // Cambia las marcas; la vista conserva sus textos.
        }

        public void Tick()
        {
            UpdateKeyboard();
            bool active = _environment != null && _environment.IsActive;
            float scale = _book != null ? _book.RenderScale : 0f;
            if (active != _lastEnvironmentActive)
            {
                _lastEnvironmentActive = active;
                if (SelectedCategory == MenuCategory.Entornos) RebuildOptions();
                Changed?.Invoke();
            }
            if (!Mathf.Approximately(scale, _lastRenderScale))
            {
                _lastRenderScale = scale;
                Changed?.Invoke();
            }
            if (_removeId != null && Time.unscaledTime >= _removeUntil)
            {
                _removeId = null;
                if (SelectedCategory == MenuCategory.Biblioteca) RebuildOptions();
                Changed?.Invoke();
            }
            if (_pendingClear != 0 && Time.unscaledTime >= _pendingClearUntil)
            {
                _pendingClear = 0;
                if (SelectedCategory == MenuCategory.Ajustes) RebuildOptions();
                Changed?.Invoke();
            }
            if (TimerMinutes > 0 && Time.realtimeSinceStartupAsDouble >= _timerEnd)
            {
                TimerMinutes = 0;
                Changed?.Invoke();
                TimerElapsed?.Invoke();
            }
        }

        // ── «+ Agregar libro» y configuración de un libro (pedido del usuario, 11 nov 2026) ──
        private int _libraryMode; // 0 = lista, 1 = elegir de dónde agregar.
        private string _setupId;
        private TouchScreenKeyboard _keyboard;
        private string _keyboardFor;

        private void AddBookSources(List<MenuOption> options, BookLibrary library)
        {
            options.Add(new MenuOption("Volver", () => { _libraryMode = 0; RebuildOptions(); }));
            options.Add(new MenuOption("Desde el visor\n(archivos del Quest)", () =>
            {
                _libraryMode = 0;
                library.Import();
                RebuildOptions();
            }, () => !library.Importing));
            if (!library.WifiRunning)
                options.Add(new MenuOption("Desde el PC\npor Wi-Fi", library.StartWifi));
            else
            {
                options.Add(new MenuOption("En el PC abre\n" + library.WifiUrl));
                options.Add(new MenuOption("Código: " + library.WifiCode));
                options.Add(new MenuOption("Cerrar la carga\npor Wi-Fi", library.StopWifi));
            }
            options.Add(new MenuOption("Desde un catálogo\n(OPDS)", () =>
            {
                _libraryMode = 0;
                Select(MenuCategory.ImportarYNubes);
            }));
            options.Add(new MenuOption("Desde la nube\n(próximamente)"));
        }

        /// <summary>Nombre, lectura y tapas del libro elegido. Devuelve false si el libro ya no existe.</summary>
        private bool AddSetupOptions(List<MenuOption> options, BookLibrary library)
        {
            BookEntry entry = null;
            foreach (BookEntry book in library.Books)
                if (book.id == _setupId) entry = book;
            if (entry == null)
            {
                _setupId = null;
                return false;
            }
            bool configured = BookLibrary.IsConfigured(entry);
            options.Add(new MenuOption((configured ? "Editar libro" : "Libro nuevo") + "\n" + entry.format.ToUpperInvariant()));
            options.Add(new MenuOption("Nombre:\n" + Shorten(entry.title, 30), () => OpenKeyboard(entry)));
            bool rtl = library.RightToLeft(entry);
            options.Add(new MenuOption("Lectura: " + (rtl ? "manga" : "normal"),
                () => { library.SetRightToLeft(entry, !rtl); RebuildOptions(); }, marked: () => library.RightToLeft(entry)));
            bool own = BookLibrary.OwnCovers(entry);
            options.Add(new MenuOption(own ? "Tapas: las del\narchivo" : "Tapas: de cuero\ncon el título",
                () => { library.SetOwnCovers(entry, !own); RebuildOptions(); }));
            if (!own)
            {
                int leather = Mathf.Clamp(BookLibrary.Leather(entry), 0, BookCover.LeatherNames.Length - 1);
                options.Add(new MenuOption("Cuero: " + BookCover.LeatherNames[leather],
                    () => { library.SetLeather(entry, (leather + 1) % BookCover.LeatherNames.Length); RebuildOptions(); }));
            }
            options.Add(new MenuOption(configured ? "Listo" : "Listo: guardar", () =>
            {
                library.FinishSetup(entry);
                _setupId = null;
                RebuildOptions();
            }));
            options.Add(new MenuOption("Listo y abrir", () =>
            {
                library.FinishSetup(entry);
                _setupId = null;
                library.Open(entry);
                RebuildOptions();
            }, () => entry.CanOpen));
            return true;
        }

        /// <summary>Teclado del sistema de Horizon OS para escribir el nombre del libro.</summary>
        private void OpenKeyboard(BookEntry entry)
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                BookLibrary.NotifyUser("El teclado del sistema no está disponible en este visor.");
                return;
            }
            _keyboardFor = entry.id;
            _keyboard = TouchScreenKeyboard.Open(entry.title, TouchScreenKeyboardType.Default, false, false, false, false,
                "Nombre del libro");
            Debug.Log("[FolioXR] Biblioteca: teclado abierto para renombrar");
        }

        private void UpdateKeyboard()
        {
            if (_keyboard == null) return;
            TouchScreenKeyboard.Status status = _keyboard.status;
            if (status == TouchScreenKeyboard.Status.Visible) return;
            if (status == TouchScreenKeyboard.Status.Done && BookLibrary.Instance != null)
                foreach (BookEntry entry in BookLibrary.Instance.Books)
                    if (entry.id == _keyboardFor)
                    {
                        BookLibrary.Instance.Rename(entry, _keyboard.text);
                        break;
                    }
            _keyboard = null;
            _keyboardFor = null;
            if (SelectedCategory == MenuCategory.Biblioteca) RebuildOptions();
            Changed?.Invoke();
        }

        // ── Catálogos OPDS (encargo 14) ──
        private bool _removingOpds;
        private string _removeOpdsId;
        private float _removeOpdsUntil;

        private void AddOpdsOptions(List<MenuOption> options)
        {
            OpdsBrowser opds = OpdsBrowser.Instance;
            if (opds == null || !opds.Available) return;
            if (opds.Downloading) options.Add(new MenuOption(opds.DownloadLine));
            if (!opds.Browsing)
            {
                foreach (OpdsServer server in opds.Servers)
                {
                    OpdsServer target = server;
                    bool confirming = _removeOpdsId == server.id && Time.unscaledTime < _removeOpdsUntil;
                    options.Add(new MenuOption(confirming ? "¿Quitar? Toca\notra vez"
                            : (_removingOpds ? "Quitar: " : "Catálogo: ") + server.name, () =>
                        {
                            if (!_removingOpds) { opds.OpenServer(target); return; }
                            if (_removeOpdsId != target.id || Time.unscaledTime >= _removeOpdsUntil)
                            {
                                _removeOpdsId = target.id;
                                _removeOpdsUntil = Time.unscaledTime + 4f;
                            }
                            else
                            {
                                _removeOpdsId = null;
                                _removingOpds = false;
                                opds.RemoveServer(target);
                            }
                            RebuildOptions();
                        }, marked: () => _removingOpds));
                }
                if (opds.Servers.Count > 0)
                    options.Add(new MenuOption(_removingOpds ? "Elige el catálogo\na quitar" : "Quitar un catálogo",
                        () => { _removingOpds = !_removingOpds; _removeOpdsId = null; RebuildOptions(); },
                        marked: () => _removingOpds));
                options.Add(new MenuOption("Agregar catálogo OPDS: desde la página del PC (Wi-Fi)"));
                return;
            }
            options.Add(new MenuOption(opds.Depth > 1 ? "Volver" : "Salir del catálogo", opds.Back));
            if (opds.Loading)
            {
                options.Add(new MenuOption("Cargando el catálogo…"));
                return;
            }
            if (!string.IsNullOrEmpty(opds.Error))
            {
                options.Add(new MenuOption(opds.Error));
                return;
            }
            OpdsFeed feed = opds.Feed;
            if (feed == null) return;
            if (!string.IsNullOrEmpty(feed.title)) options.Add(new MenuOption(feed.title));
            int count = feed.entries.Count;
            int pages = Mathf.Max(1, (count + OpdsBrowser.EntriesPerPage - 1) / OpdsBrowser.EntriesPerPage);
            int first = opds.LocalPage * OpdsBrowser.EntriesPerPage;
            for (int i = first; i < Mathf.Min(count, first + OpdsBrowser.EntriesPerPage); i++)
            {
                OpdsEntry entry = feed.entries[i];
                string title = string.IsNullOrEmpty(entry.title) ? "(sin título)" : entry.title;
                if (title.Length > 34) title = title.Substring(0, 33) + "…";
                if (entry.IsNavigation)
                {
                    string url = entry.nav;
                    options.Add(new MenuOption("▸ " + title, () => opds.OpenNav(url)));
                    continue;
                }
                OpdsBook best = entry.Best;
                options.Add(new MenuOption(title + "\n" + (best != null ? "Descargar " + best.FormatName : "con DRM: no se puede"),
                    () => opds.Download(entry), () => best != null && !opds.Downloading));
            }
            if (pages > 1)
                options.Add(new MenuOption("Más: " + (opds.LocalPage + 1) + " de " + pages, opds.NextLocalPage));
            if (!string.IsNullOrEmpty(feed.next))
                options.Add(new MenuOption("Siguiente página del catálogo", opds.NextRemotePage));
            if (count == 0) options.Add(new MenuOption("Este catálogo está vacío"));
        }

        private void ConfirmClear(int kind)
        {
            if (_pendingClear != kind || Time.unscaledTime >= _pendingClearUntil)
            {
                _pendingClear = kind;
                _pendingClearUntil = Time.unscaledTime + 4f;
            }
            else
            {
                _pendingClear = 0;
                if (kind == 1) InkManager.ClearVisibleBookPages();
                else InkManager.ClearHouse();
            }
            RebuildOptions();
        }

        private void SetTimer(int minutes)
        {
            TimerMinutes = minutes;
            _timerEnd = Time.realtimeSinceStartupAsDouble + minutes * 60.0;
        }

        private void RebuildOptions()
        {
            var options = new List<MenuOption>();
            switch (SelectedCategory)
            {
                case MenuCategory.Biblioteca:
                    BookLibrary library = BookLibrary.Instance;
                    if (library == null || !library.Available)
                    {
                        // Fuera del visor (Editor) no hay biblioteca: solo se informa el PDF abierto.
                        string name = System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath,
                            FolioBook.UserPdfName)) ? FolioBook.UserPdfName : FolioBook.BundledPdfName;
                        options.Add(new MenuOption("Libro abierto:\n" + name));
                        options.Add(new MenuOption("La biblioteca solo\nfunciona en el visor"));
                        break;
                    }
                    BookEntry pending = library.PendingSetup;
                    if (_setupId == null && pending != null) _setupId = pending.id; // Libro recién agregado.
                    if (_setupId != null && AddSetupOptions(options, library)) break;
                    if (_libraryMode == 1)
                    {
                        AddBookSources(options, library);
                        break;
                    }
                    if (library.Importing)
                    {
                        options.Add(new MenuOption(library.ImportLine));
                        options.Add(new MenuOption("Cancelar\nimportación", library.CancelImport));
                    }
                    else
                    {
                        options.Add(new MenuOption("+ Agregar libro", () => { _libraryMode = 1; RebuildOptions(); }));
                        options.Add(new MenuOption(_removing ? "Elige el libro\na quitar" : "Quitar de la\nbiblioteca",
                            () => { _removing = !_removing; _removeId = null; RebuildOptions(); },
                            () => library.Books.Count > 0, () => _removing));
                    }
                    options.Add(new MenuOption("Lectura: " + (_book != null && _book.RightToLeft ? "manga" : "normal"),
                        () => { _book.SetRightToLeft(!_book.RightToLeft); RebuildOptions(); }, () => _book != null,
                        () => _book != null && _book.RightToLeft));
                    options.Add(new MenuOption("Cerrar libro", library.CloseCurrent, () => library.Current != null));
                    options.Add(new MenuOption("Editar libro", () => { _setupId = library.Current.id; RebuildOptions(); },
                        () => library.Current != null));
                    BookEntry current = library.Current;
                    bool bookmarked = library.IsBookmarked;
                    options.Add(new MenuOption(current == null ? "Marcar página"
                            : (bookmarked ? "Quitar marcador\npág. " : "Marcar pág. ") + (current.position + 1),
                        library.ToggleBookmark, () => library.Current != null, () => library.IsBookmarked));
                    for (int i = 0; i < library.Bookmarks.Count && i < 4; i++)
                    {
                        int position = library.Bookmarks[i];
                        options.Add(new MenuOption("Ir al marcador\npág. " + (position + 1),
                            () => library.GoToBookmark(position)));
                    }
                    int bookPages = Mathf.Max(1, (library.Books.Count + BooksPerPage - 1) / BooksPerPage);
                    _libraryPage = Mathf.Clamp(_libraryPage, 0, bookPages - 1);
                    int last = Mathf.Min(library.Books.Count, (_libraryPage + 1) * BooksPerPage);
                    for (int i = _libraryPage * BooksPerPage; i < last; i++)
                    {
                        BookEntry entry = library.Books[i];
                        string text = _removeId == entry.id ? "¿Quitar? Toca\notra vez"
                            : Shorten(entry.title, 30) + "\n" +
                              (entry.CanOpen ? entry.ProgressText : entry.format.ToUpperInvariant() + " · pronto");
                        options.Add(new MenuOption(text, () => TapBook(entry),
                            marked: () => library.Current != null && library.Current.id == entry.id));
                    }
                    if (bookPages > 1)
                        options.Add(new MenuOption("Más libros\n" + (_libraryPage + 1) + " de " + bookPages,
                            () => { _libraryPage = (_libraryPage + 1) % bookPages; RebuildOptions(); }));
                    if (library.Books.Count == 0) options.Add(new MenuOption("Aún no hay libros:\nimporta el primero"));
                    break;
                case MenuCategory.Entornos:
                {
                    // Once estilos (docs/ENTORNOS.md), en páginas de ocho; el marcado es el activo.
                    const int themesPerPage = 8;
                    var themes = EnvironmentThemes.All;
                    int themePages = (themes.Count + themesPerPage - 1) / themesPerPage;
                    _themePage = Mathf.Clamp(_themePage, 0, themePages - 1);
                    options.Add(new MenuOption("Passthrough", () => _environment.Leave(),
                        () => _environment != null, () => _environment != null && !_environment.IsActive));
                    int lastTheme = Mathf.Min(themes.Count, (_themePage + 1) * themesPerPage);
                    for (int i = _themePage * themesPerPage; i < lastTheme; i++)
                    {
                        int theme = i;
                        options.Add(new MenuOption(TwoLines(themes[i].Name), () => _environment.SetTheme(theme),
                            () => _environment != null,
                            () => _environment != null && _environment.IsActive && _environment.ThemeIndex == theme));
                    }
                    if (themePages > 1)
                        options.Add(new MenuOption("Más entornos\n" + (_themePage + 1) + " de " + themePages,
                            () => { _themePage = (_themePage + 1) % themePages; RebuildOptions(); }));
                    if (_environment != null && _environment.IsActive && _environment.PointNames.Count > 0)
                    {
                        int next = (_pointCursor + 1) % _environment.PointNames.Count;
                        options.Add(new MenuOption("Ir a:\n" + _environment.PointNames[next], () =>
                        {
                            _pointCursor = next;
                            _environment.GoToPoint(next);
                            RebuildOptions();
                        }));
                    }
                    break;
                }
                case MenuCategory.Ajustes:
                    // Una sola casilla por ajuste, coherente con el menú compacto de la base.
                    string[] encodings = { "auto", "UTF-8", "Windows-1252" };
                    int encodingIndex = Array.IndexOf(encodings, PdfPageRenderer.TxtEncoding);
                    if (encodingIndex < 0) encodingIndex = 0;
                    string nextEncoding = encodings[(encodingIndex + 1) % encodings.Length];
                    options.Add(new MenuOption("Codificación TXT:\n" +
                        (encodings[encodingIndex] == "auto" ? "automática" : encodings[encodingIndex]),
                        () => { BookLibrary.Instance.SetTxtEncoding(nextEncoding); RebuildOptions(); },
                        () => BookLibrary.Instance != null && !BookLibrary.Instance.Repaginating));
                    PageThemeMode pageMode = PageTheme.Current;
                    options.Add(new MenuOption("Página:\n" + PageTheme.Name(pageMode),
                        () => { PageTheme.Set((PageThemeMode)(((int)pageMode + 1) % 3)); RebuildOptions(); }));
                    string[] fontNames = { "pequeña", "normal", "grande" };
                    float[] fontScales = { 0.8f, 1f, 1.25f };
                    int font = 1;
                    for (int f = 0; f < fontScales.Length; f++)
                        if (Mathf.Approximately(PdfPageRenderer.EpubFontScale, fontScales[f])) font = f;
                    float nextFont = fontScales[(font + 1) % fontScales.Length];
                    options.Add(new MenuOption("Letra: " + fontNames[font],
                        () => BookLibrary.Instance.SetEpubFontScale(nextFont),
                        () => BookLibrary.Instance != null && !BookLibrary.Instance.Repaginating));
                    if (_book != null)
                    {
                        int steps = Mathf.RoundToInt((FolioBook.MaxRenderScale - FolioBook.MinRenderScale) / FolioBook.RenderScaleStep) + 1;
                        int sharp = Mathf.Clamp(Mathf.RoundToInt((_book.RenderScale - FolioBook.MinRenderScale) / FolioBook.RenderScaleStep), 0, steps - 1);
                        float nextScale = FolioBook.MinRenderScale + ((sharp + 1) % steps) * FolioBook.RenderScaleStep;
                        options.Add(new MenuOption("Nitidez: " + _book.RenderScale.ToString("F1", CultureInfo.InvariantCulture).Replace('.', ','),
                            () => { _book.SetRenderScale(nextScale); RebuildOptions(); }));
                    }
                    options.Add(new MenuOption("Menú: " + (_sphereStyle() ? "esfera" : "simple"),
                        () => _setSphereStyle(!_sphereStyle())));
                    options.Add(new MenuOption("Física: " + (_book != null && _book.RealisticPhysics ? "Realista" : "Flotante"),
                        () => { _book.SetRealisticPhysics(!_book.RealisticPhysics); RebuildOptions(); }, () => _book != null));
                    options.Add(new MenuOption("Acostado: " + (FolioBook.LyingMode ? "sí" : "no"),
                        () => { FolioBook.SetLyingMode(!FolioBook.LyingMode); if (_book != null) _book.PlaceInFrontOfView(); RebuildOptions(); },
                        marked: () => FolioBook.LyingMode));
                    options.Add(new MenuOption("Velocidad: " + (FolioLocomotion.Jog ? "trotar" : "caminar"),
                        () => { FolioLocomotion.SetJog(!FolioLocomotion.Jog); RebuildOptions(); }, marked: () => FolioLocomotion.Jog));
                    options.Add(new MenuOption("Viñeta: " + (FolioLocomotion.VignetteEnabled ? "sí" : "no"),
                        () => { FolioLocomotion.SetVignette(!FolioLocomotion.VignetteEnabled); RebuildOptions(); }));
                    options.Add(new MenuOption("Giro: " + (FolioLocomotion.SmoothTurn ? "suave" : "por pasos"),
                        () => { FolioLocomotion.SetSmoothTurn(!FolioLocomotion.SmoothTurn); RebuildOptions(); }));
                    options.Add(new MenuOption("Tamaño normal", () => _book.ResetBookScale(), () => _book != null));
                    options.Add(new MenuOption("Guardar herramientas", ToolRegistry.StoreAll, () => ToolRegistry.Instance != null));
                    options.Add(new MenuOption(_pendingClear == 1 ? "¿Borrar? Toca\notra vez" : "Borrar trazos\nde esta página",
                        () => ConfirmClear(1), () => InkManager.Instance != null));
                    options.Add(new MenuOption(_pendingClear == 2 ? "¿Borrar? Toca\notra vez" : "Borrar trazos\nde la casa",
                        () => ConfirmClear(2), () => InkManager.HasHouse));
                    options.Add(new MenuOption("Traer libro", () => _book.PlaceInFrontOfView(), () => _book != null));
                    break;
                case MenuCategory.ImportarYNubes:
                    BookLibrary source = BookLibrary.Instance;
                    if (source == null || !source.Available)
                    {
                        options.Add(new MenuOption("Importar solo funciona en el visor"));
                        break;
                    }
                    if (OpdsBrowser.Instance != null && OpdsBrowser.Instance.Browsing)
                    {
                        AddOpdsOptions(options); // Dentro de un catálogo, solo el catálogo.
                        break;
                    }
                    options.Add(new MenuOption("Desde el visor: elegir archivos", source.Import, () => !source.Importing));
                    if (!source.WifiRunning)
                        options.Add(new MenuOption("Desde el PC por Wi-Fi", source.StartWifi));
                    else
                    {
                        options.Add(new MenuOption("En el navegador del PC abre " + source.WifiUrl));
                        options.Add(new MenuOption("Código: " + source.WifiCode + " · recibidos: " + source.WifiReceived));
                        options.Add(new MenuOption("Cerrar la carga por Wi-Fi", source.StopWifi));
                    }
                    options.Add(new MenuOption("Nubes (Drive, Dropbox, MEGA): próximamente"));
                    AddOpdsOptions(options);
                    break;
                case MenuCategory.Hora:
                    foreach (int minutes in new[] { 15, 30, 45, 60 })
                    {
                        int duration = minutes;
                        options.Add(new MenuOption(duration + " min", () => SetTimer(duration),
                            marked: () => TimerMinutes == duration));
                    }
                    options.Add(new MenuOption("Apagar", () => SetTimer(0), marked: () => TimerMinutes == 0));
                    break;
            }
            _options = options.AsReadOnly();
        }
    }
}
