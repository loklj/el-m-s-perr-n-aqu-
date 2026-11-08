using System;
using System.Collections.Generic;
using System.Globalization;
using FolioXR.Environments;
using FolioXR.Reading;
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
        void ShowNotice(string message, Transform head);
    }

    public sealed class MenuOption
    {
        public string Text { get; }
        public Func<bool> Enabled { get; }
        public Func<bool> Marked { get; }
        public Func<bool> Visible { get; }
        public Action Action { get; }

        public MenuOption(string text, Action action = null, Func<bool> enabled = null, Func<bool> marked = null, Func<bool> visible = null)
        {
            Text = text;
            Action = action;
            Enabled = enabled ?? (() => action != null);
            Marked = marked ?? (() => false);
            Visible = visible ?? (() => true);
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
        private readonly Action _useSimple;
        private IReadOnlyList<MenuOption> _options;
        private bool _lastEnvironmentActive;
        private float _lastRenderScale;
        private double _timerEnd;

        public MenuModel(FolioBook book, FolioEnvironmentController environment, Action useSimple)
        {
            _book = book;
            _environment = environment;
            _useSimple = useSimple;
            RebuildOptions();
        }

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
            if (!option.Enabled() || !option.Visible()) return;
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
                Changed?.Invoke();
            }
            if (!Mathf.Approximately(scale, _lastRenderScale))
            {
                _lastRenderScale = scale;
                Changed?.Invoke();
            }
            if (TimerMinutes > 0 && Time.realtimeSinceStartupAsDouble >= _timerEnd)
            {
                TimerMinutes = 0;
                Changed?.Invoke();
                TimerElapsed?.Invoke();
            }
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
                    // El Hito 1 solo abre uno de estos dos PDF; no existe biblioteca todavía.
                    string name = System.IO.File.Exists(System.IO.Path.Combine(Application.persistentDataPath,
                        FolioBook.UserPdfName)) ? FolioBook.UserPdfName : FolioBook.BundledPdfName;
                    options.Add(new MenuOption("Libro abierto:\n" + name));
                    options.Add(new MenuOption("Próximamente:\ntu biblioteca"));
                    break;
                case MenuCategory.Entornos:
                    options.Add(new MenuOption("Passthrough", () => _environment.Leave(),
                        () => _environment != null, () => _environment != null && !_environment.IsActive));
                    options.Add(new MenuOption("Casa moderna\ncálida", () => _environment.Enter(),
                        () => _environment != null, () => _environment != null && _environment.IsActive));
                    if (_environment != null)
                        for (int i = 0; i < _environment.PointNames.Count; i++)
                        {
                            int point = i;
                            options.Add(new MenuOption(_environment.PointNames[i], () => _environment.GoToPoint(point),
                                () => _environment.IsActive, visible: () => _environment.IsActive));
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
                    options.Add(new MenuOption("Estilo de menú:\nSimple", _useSimple, marked: () => true));
                    options.Add(new MenuOption("Esfera\n(próximamente)"));
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
