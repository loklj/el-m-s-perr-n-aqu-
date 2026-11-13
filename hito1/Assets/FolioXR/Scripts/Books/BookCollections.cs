using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace FolioXR.Books
{
    /// <summary>
    /// Colecciones (carpetas), orden y filtro de la biblioteca (encargo 18, parte 2; Claude, 12 nov 2026).
    /// Todo vive en PlayerPrefs: no toca la base SQLite de FolioLibrary.java. El menú y la estantería 3D
    /// leen la misma vista con <see cref="View"/>.
    /// </summary>
    public static class BookCollections
    {
        public enum SortMode { Recent, Title, Author, Manual }
        public enum FormatFilter { All, Pdf, Comics, Ebooks, Documents }

        private const string SortKey = "folioxr.lib.orden", FilterKey = "folioxr.lib.formato",
                             CollectionKey = "folioxr.lib.coleccion", DataKey = "folioxr.lib.colecciones";
        public static event Action Changed;

        public static readonly string[] SortNames = { "recientes", "título", "autor", "a mano" };
        public static readonly string[] FilterNames = { "todos", "PDF", "cómics", "libros", "documentos" };

        [Serializable] private sealed class Folder { public string name; public List<string> ids = new List<string>(); }
        [Serializable] private sealed class Store { public List<Folder> folders = new List<Folder>(); public List<string> order = new List<string>(); }

        private static Store _store;

        public static SortMode Sort => (SortMode)Mathf.Clamp(PlayerPrefs.GetInt(SortKey, 0), 0, 3);
        public static FormatFilter Filter => (FormatFilter)Mathf.Clamp(PlayerPrefs.GetInt(FilterKey, 0), 0, 4);

        /// <summary>Colección visible; vacío = todas.</summary>
        public static string Active
        {
            get
            {
                string name = PlayerPrefs.GetString(CollectionKey, string.Empty);
                return string.IsNullOrEmpty(name) || Data.folders.Any(f => f.name == name) ? name : string.Empty;
            }
        }

        public static IReadOnlyList<string> Names => Data.folders.Select(f => f.name).ToList();

        public static void SetSort(SortMode mode) { PlayerPrefs.SetInt(SortKey, (int)mode); Save(); }
        public static void SetFilter(FormatFilter filter) { PlayerPrefs.SetInt(FilterKey, (int)filter); Save(); }
        public static void SetActive(string name) { PlayerPrefs.SetString(CollectionKey, name ?? string.Empty); Save(); }

        /// <summary>Siguiente colección para el botón que las recorre: todas → cada una → todas.</summary>
        public static string NextActive()
        {
            var names = Names;
            if (names.Count == 0) return string.Empty;
            int index = -1;
            for (int i = 0; i < names.Count; i++) if (names[i] == Active) index = i;
            return index + 1 < names.Count ? names[index + 1] : string.Empty;
        }

        public static bool Create(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 40 || Data.folders.Any(f => string.Equals(f.name, name, StringComparison.OrdinalIgnoreCase)))
                return false;
            Data.folders.Add(new Folder { name = name });
            Save();
            return true;
        }

        public static void Rename(string oldName, string newName)
        {
            newName = (newName ?? string.Empty).Trim();
            Folder folder = Data.folders.FirstOrDefault(f => f.name == oldName);
            if (folder == null || newName.Length == 0 || Data.folders.Any(f => f != folder && f.name == newName)) return;
            bool active = Active == oldName;
            folder.name = newName;
            if (active) PlayerPrefs.SetString(CollectionKey, newName);
            Save();
        }

        public static void Delete(string name)
        {
            Data.folders.RemoveAll(f => f.name == name);
            if (Active == name) PlayerPrefs.SetString(CollectionKey, string.Empty);
            Save();
        }

        /// <summary>Colección del libro (un libro está en una colección o en ninguna).</summary>
        public static string CollectionOf(string id) => Data.folders.FirstOrDefault(f => f.ids.Contains(id))?.name ?? string.Empty;

        public static void Assign(string id, string name)
        {
            foreach (Folder f in Data.folders) f.ids.Remove(id);
            Folder target = Data.folders.FirstOrDefault(f => f.name == name);
            if (target != null) target.ids.Add(id);
            Save();
        }

        /// <summary>Siguiente colección para un libro: ninguna → cada una → ninguna.</summary>
        public static string NextFor(string id)
        {
            var names = Names;
            if (names.Count == 0) return string.Empty;
            string current = CollectionOf(id);
            int index = -1;
            for (int i = 0; i < names.Count; i++) if (names[i] == current) index = i;
            return index + 1 < names.Count ? names[index + 1] : string.Empty;
        }

        /// <summary>Libros visibles: colección y formato elegidos, en el orden elegido.</summary>
        public static List<BookEntry> View(IReadOnlyList<BookEntry> books)
        {
            var result = new List<BookEntry>();
            if (books == null) return result;
            string active = Active;
            HashSet<string> members = string.IsNullOrEmpty(active) ? null
                : new HashSet<string>(Data.folders.First(f => f.name == active).ids);
            FormatFilter filter = Filter;
            foreach (BookEntry b in books)
            {
                if (b == null) continue;
                if (members != null && !members.Contains(b.id)) continue;
                if (!Matches(b.format, filter)) continue;
                result.Add(b);
            }
            CompareInfo compare = Comparer;
            const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            switch (Sort)
            {
                case SortMode.Title:
                    result.Sort((a, b) => compare.Compare(a.title ?? "", b.title ?? "", options));
                    break;
                case SortMode.Author:
                    // Sin autor al final; a igual autor, por título.
                    result.Sort((a, b) =>
                    {
                        bool ea = string.IsNullOrWhiteSpace(a.author), eb = string.IsNullOrWhiteSpace(b.author);
                        if (ea != eb) return ea ? 1 : -1;
                        int c = compare.Compare(a.author ?? "", b.author ?? "", options);
                        return c != 0 ? c : compare.Compare(a.title ?? "", b.title ?? "", options);
                    });
                    break;
                case SortMode.Manual:
                {
                    // Orden que la persona armó en la estantería; los libros nuevos van al final.
                    var rank = new Dictionary<string, int>();
                    for (int i = 0; i < Data.order.Count; i++) if (!rank.ContainsKey(Data.order[i])) rank[Data.order[i]] = i;
                    result.Sort((a, b) =>
                    {
                        bool ka = rank.TryGetValue(a.id ?? "", out int ra), kb = rank.TryGetValue(b.id ?? "", out int rb);
                        if (ka && kb) return ra.CompareTo(rb);
                        if (ka != kb) return ka ? -1 : 1;
                        return b.lastRead.CompareTo(a.lastRead);
                    });
                    break;
                }
                default:
                    result.Sort((a, b) => b.lastRead.CompareTo(a.lastRead));
                    break;
            }
            return result;
        }

        private static CompareInfo _comparer;
        private static CompareInfo Comparer
        {
            get
            {
                if (_comparer != null) return _comparer;
                try { _comparer = CultureInfo.GetCultureInfo("es-ES").CompareInfo; }
                catch (Exception) { _comparer = CultureInfo.InvariantCulture.CompareInfo; } // Sin datos de cultura.
                return _comparer;
            }
        }

        public static bool Matches(string format, FormatFilter filter)
        {
            format = (format ?? string.Empty).ToLowerInvariant();
            switch (filter)
            {
                case FormatFilter.Pdf: return format == "pdf";
                case FormatFilter.Comics: return format == "cbz" || format == "zip" || format == "cbr";
                case FormatFilter.Ebooks: return format == "epub" || format == "mobi" || format == "azw" || format == "azw3" || format == "txt";
                case FormatFilter.Documents: return format == "docx" || format == "odt" || format == "rtf" || format == "doc";
                default: return true;
            }
        }

        /// <summary>
        /// Coloca un libro en la estantería antes de otro (o al final si before es null) y pasa a «a mano».
        /// view es lo que la estantería muestra ahora: desde ahí se arma el orden, así nada salta de lugar.
        /// </summary>
        public static void Place(IReadOnlyList<BookEntry> view, string id, string before)
        {
            if (string.IsNullOrEmpty(id) || view == null) return;
            var ids = new List<string>();
            foreach (BookEntry b in view) if (b != null && b.id != id) ids.Add(b.id);
            int at = before != null ? ids.IndexOf(before) : -1;
            if (at < 0) ids.Add(id); else ids.Insert(at, id);
            // Los libros ocultos por un filtro o una colección conservan su lugar relativo, detrás.
            foreach (string old in Data.order) if (!ids.Contains(old)) ids.Add(old);
            Data.order = ids;
            PlayerPrefs.SetInt(SortKey, (int)SortMode.Manual);
            Save();
        }

        /// <summary>Al quitar un libro de la biblioteca también sale de su colección.</summary>
        public static void Forget(string id)
        {
            bool changed = false;
            foreach (Folder f in Data.folders) changed |= f.ids.Remove(id);
            changed |= Data.order.Remove(id);
            if (changed) Save();
        }

        private static Store Data
        {
            get
            {
                if (_store != null) return _store;
                try { _store = JsonUtility.FromJson<Store>(PlayerPrefs.GetString(DataKey, "{}")) ?? new Store(); }
                catch (Exception e)
                {
                    Debug.LogWarning("[FolioXR] Colecciones ilegibles, se empieza de cero: " + e.Message);
                    _store = new Store();
                }
                if (_store.folders == null) _store.folders = new List<Folder>();
                if (_store.order == null) _store.order = new List<string>();
                return _store;
            }
        }

        private static void Save()
        {
            PlayerPrefs.SetString(DataKey, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
