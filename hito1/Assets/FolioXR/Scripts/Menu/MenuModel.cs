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
            RebuildOptions();
        }

        private void OnLibraryChanged()
        {
            if (SelectedCategory == MenuCategory.Biblioteca) RebuildOptions();
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
                    if (library.Importing)
                    {
                        options.Add(new MenuOption(library.ImportLine));
                        options.Add(new MenuOption("Cancelar\nimportación", library.CancelImport));
                    }
                    else
                    {
                        options.Add(new MenuOption("Importar libros", library.Import));
                        options.Add(new MenuOption(_removing ? "Elige el libro\na quitar" : "Quitar de la\nbiblioteca",
                            () => { _removing = !_removing; _removeId = null; RebuildOptions(); },
                            () => library.Books.Count > 0, () => _removing));
                    }
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
                    options.Add(new MenuOption("Passthrough", () => _environment.Leave(),
                        () => _environment != null, () => _environment != null && !_environment.IsActive));
                    options.Add(new MenuOption("Casa moderna\ncálida", () => _environment.Enter(),
                        () => _environment != null, () => _environment != null && _environment.IsActive));
                    if (_environment != null && _environment.IsActive)
                        for (int i = 0; i < _environment.PointNames.Count; i++)
                        {
                            int point = i;
                            options.Add(new MenuOption(_environment.PointNames[i], () => _environment.GoToPoint(point)));
                        }
                    break;
                case MenuCategory.Ajustes:
                    for (int i = 0; i < 5; i++)
                    {
                        float scale = FolioBook.MinRenderScale + i * FolioBook.RenderScaleStep;
                        options.Add(new MenuOption("Nitidez " + scale.ToString("F1", CultureInfo.InvariantCulture).Replace('.', ','),
                            () => _book.SetRenderScale(scale), () => _book != null,
                            () => _book != null && Mathf.Approximately(_book.RenderScale, scale)));
                    }
                    options.Add(new MenuOption("Menú:\nEsfera", () => _setSphereStyle(true), marked: () => _sphereStyle()));
                    options.Add(new MenuOption("Menú:\nSimple", () => _setSphereStyle(false), marked: () => !_sphereStyle()));
                    options.Add(new MenuOption("Física: " + (_book != null && _book.RealisticPhysics ? "Realista" : "Flotante"),
                        () => { _book.SetRealisticPhysics(!_book.RealisticPhysics); RebuildOptions(); }, () => _book != null));
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
                    options.Add(new MenuOption("Próximamente"));
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
