using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace WidgetNotes
{
    public sealed class Theme
    {
        public string Key { get; }
        public string Name { get; }
        public Color[] Stops { get; }
        public Color Foreground { get; }
        public Color Secondary { get; }
        public Color Accent { get; }
        public bool IsDark { get; }

        public Theme(string key, string name, Color[] stops, Color fg, Color secondary, Color accent, bool isDark)
        {
            Key = key;
            Name = name;
            Stops = stops;
            Foreground = fg;
            Secondary = secondary;
            Accent = accent;
            IsDark = isDark;
        }

        public Theme(string key, string name, string[] stops, string fg, string secondary, string accent, bool isDark)
            : this(key, name, stops.Select(Themes.Parse).ToArray(), Themes.Parse(fg), Themes.Parse(secondary), Themes.Parse(accent), isDark)
        {
        }

        public Brush CreateBackground(double opacity = 1)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), Opacity = opacity };
            for (int i = 0; i < Stops.Length; i++)
                brush.GradientStops.Add(new GradientStop(Stops[i], Stops.Length == 1 ? 0 : (double)i / (Stops.Length - 1)));
            brush.Freeze();
            return brush;
        }
    }

    public static class Themes
    {
        public const string DefaultKey = "graphite";
        public const string CustomKey = "custom";

        public static readonly Theme[] All =
        {
            new("graphite", "Graphit",     new[] { "#3A3A40", "#1C1C20" },            "#F5F5F7", "#80FFFFFF", "#FFD60A", true),
            new("midnight", "Mitternacht", new[] { "#2A3B68", "#0D1427" },            "#F2F6FF", "#80DCE6FF", "#64D2FF", true),
            new("aurora",   "Aurora",      new[] { "#5B2A86", "#B4446C", "#F08A5D" }, "#FFFFFF", "#B0FFFFFF", "#FFE29A", true),
            new("ocean",    "Ozean",       new[] { "#13A89E", "#0B4F6C" },            "#FFFFFF", "#B0FFFFFF", "#B5FFF4", true),
            new("forest",   "Wald",        new[] { "#4A7C55", "#1B3024" },            "#F3FFF5", "#99F3FFF5", "#B8F28C", true),
            new("sunset",   "Sonne",       new[] { "#FFC86B", "#FF7A59" },            "#2E1300", "#992E1300", "#7A2400", false),
            new("lemon",    "Zitrone",     new[] { "#FFF4A8", "#FFDB45" },            "#2B2500", "#992B2500", "#946000", false),
            new("paper",    "Papier",      new[] { "#FFFFFF", "#ECECF1" },            "#1D1D1F", "#8E8E93", "#FF9F0A", false),
        };

        public static Theme Get(string? key) =>
            All.FirstOrDefault(t => t.Key == key) ?? All[0];

        /// <summary>Theme einer Notiz, inkl. eigener Farben.</summary>
        public static Theme Resolve(NoteData n)
        {
            if (n.Theme == CustomKey && n.CustomColor1 != null && n.CustomColor2 != null && n.CustomAccent != null)
                return FromColors(Parse(n.CustomColor1), Parse(n.CustomColor2), Parse(n.CustomAccent));
            return Get(n.Theme);
        }

        public static Theme FromColors(Color c1, Color c2, Color accent)
        {
            bool dark = (Luminance(c1) + Luminance(c2)) / 2 < 0.6;
            return new Theme(CustomKey, "Eigene", new[] { c1, c2 },
                dark ? Parse("#F5F5F7") : Parse("#1D1D1F"),
                dark ? Parse("#99FFFFFF") : Parse("#8A000000"),
                accent, dark);
        }

        public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        public static Color Parse(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch (FormatException) { return Colors.Gray; }
        }

        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    public static class NoteFonts
    {
        public static readonly (string Key, string Name, string Display, string Text)[] All =
        {
            ("default", "Standard", "Segoe UI Variable Display, Segoe UI", "Segoe UI Variable Text, Segoe UI"),
            ("modern",  "Modern",   "Bahnschrift",                         "Bahnschrift"),
            ("serif",   "Serif",    "Georgia",                             "Georgia"),
            ("mono",    "Mono",     "Cascadia Mono, Consolas",             "Cascadia Mono, Consolas"),
            ("hand",    "Hand",     "Ink Free, Segoe Print",               "Ink Free, Segoe Print"),
        };

        public static (FontFamily Display, FontFamily Text) Get(string? key)
        {
            var f = All.FirstOrDefault(x => x.Key == key);
            if (f.Key == null) f = All[0];
            return (new FontFamily(f.Display), new FontFamily(f.Text));
        }
    }
}
