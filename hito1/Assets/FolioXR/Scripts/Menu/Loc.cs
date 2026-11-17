using System;
using System.Collections.Generic;
using UnityEngine;

namespace FolioXR.Menu
{
    /// <summary>
    /// Idiomas del menú (pedido del usuario, 16 nov 2026). El código escribe en español; al mostrar, cada
    /// texto se busca en Resources/FolioXRIdiomas.txt. Al iniciar se usa el idioma del visor y en
    /// Ajustes → Idioma se puede fijar otro. Lo que no está en la tabla (títulos de libros, números) queda igual.
    /// Faltan hindi, árabe, bengalí y urdu: sus letras se enlazan entre sí y el texto del menú aún no sabe
    /// dibujarlas bien; van a necesitar que el texto lo dibuje Android.
    /// </summary>
    public static class Loc
    {
        public const string PrefsKey = "folioxr.language";
        public static readonly string[] Codes = { "es", "en", "zh", "fr", "pt", "ru", "id", "de", "ja", "tr", "vi" };
        public static readonly string[] Names =
            { "Español", "English", "中文", "Français", "Português", "Русский", "Bahasa Indonesia", "Deutsch", "日本語", "Türkçe", "Tiếng Việt" };
        // Frases que van seguidas de un dato (número de página, minutos…): se traduce solo el comienzo.
        private static readonly string[] Prefixes =
            { "Nombrar marcador pág.", "Quitar marcador pág.", "Marcar pág.", "Pág.", "Avisar en", "Más libros", "Más entornos", "Más entradas", "Descargar", "Mi entorno" };

        public static event Action Changed;
        private static string _current;
        private static Dictionary<string, string> _table;
        private static string _tableFor;

        /// <summary>Idioma elegido en Ajustes; vacío = el del visor.</summary>
        public static string Choice => PlayerPrefs.GetString(PrefsKey, string.Empty);

        public static string Current
        {
            get
            {
                if (_current != null) return _current;
                string choice = Choice;
                _current = Array.IndexOf(Codes, choice) >= 0 ? choice : Detect();
                return _current;
            }
        }

        public static string NameOf(string code)
        {
            int at = Array.IndexOf(Codes, code);
            return at >= 0 ? Names[at] : code;
        }

        public static void Set(string code)
        {
            PlayerPrefs.SetString(PrefsKey, code ?? string.Empty);
            PlayerPrefs.Save();
            _current = null;
            Debug.Log("[FolioXR] Idioma: " + (string.IsNullOrEmpty(code) ? "automático (" + Current + ")" : code));
            Changed?.Invoke();
        }

        private static string Detect()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Vietnamese: return "vi";
                default: return "en";                              // Cualquier otro idioma del visor: inglés.
            }
        }

        private static Dictionary<string, string> Table()
        {
            string language = Current;
            if (_table != null && _tableFor == language) return _table;
            _table = new Dictionary<string, string>();
            _tableFor = language;
            TextAsset asset = Resources.Load<TextAsset>("FolioXRIdiomas");
            if (asset == null) return _table;
            int column = -1;
            foreach (string raw in asset.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cells = line.Split(new[] { " | " }, StringSplitOptions.None);
                if (column < 0)
                {
                    column = Array.IndexOf(cells, language);         // Primer renglón: los códigos.
                    if (column < 0) return _table;
                    continue;
                }
                if (cells.Length > column && cells[column].Length > 0) _table[cells[0]] = cells[column];
            }
            return _table;
        }

        /// <summary>Una frase suelta (sin datos): su traducción o la misma frase.</summary>
        public static string T(string spanish)
        {
            if (string.IsNullOrEmpty(spanish) || Current == "es") return spanish;
            return Table().TryGetValue(spanish.Replace('\n', ' ').Trim(), out string value) ? value : spanish;
        }

        /// <summary>Texto de una opción del menú, que puede traer datos («Marcar pág. 12», «Cuero: negro»).</summary>
        public static string Menu(string text)
        {
            if (string.IsNullOrEmpty(text) || Current == "es") return text;
            Dictionary<string, string> table = Table();
            if (table.Count == 0) return text;
            string flat = text.Replace('\n', ' ').Trim();
            bool lines = text.IndexOf('\n') >= 0;
            if (table.TryGetValue(flat, out string whole)) return lines ? Wrap(whole) : whole;
            int colon = text.IndexOf(':');
            if (colon > 0 && table.TryGetValue(text.Substring(0, colon).Replace('\n', ' ').Trim(), out string head))
            {
                string rest = text.Substring(colon + 1);
                bool below = rest.StartsWith("\n", StringComparison.Ordinal);
                string tail = Piece(rest.Replace('\n', ' ').Trim(), table);
                return tail.Length == 0 ? head + ":" : head + (below ? ":\n" : ": ") + tail;
            }
            string piece = Piece(flat, table);
            return piece == flat ? text : lines ? Wrap(piece) : piece;
        }

        private static string Piece(string flat, Dictionary<string, string> table)
        {
            if (table.TryGetValue(flat, out string value)) return value;
            foreach (string prefix in Prefixes)
            {
                if (!flat.StartsWith(prefix + " ", StringComparison.Ordinal) || !table.TryGetValue(prefix, out string start)) continue;
                return start + " " + Count(flat.Substring(prefix.Length + 1));
            }
            return Count(flat);
        }

        // «2 de 5» → «2 / 5».
        private static string Count(string text)
        {
            int of = text.IndexOf(" de ", StringComparison.Ordinal);
            if (of <= 0 || of + 4 >= text.Length || !char.IsDigit(text[of - 1]) || !char.IsDigit(text[of + 4])) return text;
            return text.Substring(0, of) + " / " + text.Substring(of + 4);
        }

        /// <summary>Parte una frase en dos renglones parejos (en un espacio; sin espacios, por la mitad).</summary>
        private static string Wrap(string text)
        {
            if (text.Length <= 10) return text;
            int best = -1;
            for (int i = 1; i < text.Length - 1; i++)
                if (text[i] == ' ' && (best < 0 || Mathf.Abs(i - text.Length / 2) < Mathf.Abs(best - text.Length / 2))) best = i;
            if (best > 0) return text.Substring(0, best) + "\n" + text.Substring(best + 1);
            if (text.Length <= 12) return text;
            int half = (text.Length + 1) / 2;                       // Chino y japonés: sin espacios.
            return text.Substring(0, half) + "\n" + text.Substring(half);
        }
    }
}
