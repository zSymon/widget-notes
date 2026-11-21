using System;
using System.Collections.ObjectModel;

namespace WidgetNotes
{
    public class NoteData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>"text" oder "todo".</summary>
        public string Kind { get; set; } = "text";
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public ObservableCollection<TodoItem> Items { get; set; } = new();

        /// <summary>Position der Kachel (ohne Schattenrand) in WPF-Einheiten.</summary>
        public double? X { get; set; }
        public double? Y { get; set; }

        /// <summary>Small, Medium, Large oder Tall.</summary>
        public string Size { get; set; } = "Medium";

        // ---- Aussehen ----
        /// <summary>Preset-Key oder "custom".</summary>
        public string Theme { get; set; } = "graphite";
        public string? CustomColor1 { get; set; }
        public string? CustomColor2 { get; set; }
        public string? CustomAccent { get; set; }
        public double Opacity { get; set; } = 1.0;
        public double Radius { get; set; } = 22;
        public string Font { get; set; } = "default";
        public double FontSize { get; set; } = 14;
        public bool Shadow { get; set; } = true;
        public bool Gloss { get; set; } = true;

        public DateTime Modified { get; set; } = DateTime.Now;

        public void CopyStyleFrom(NoteData o)
        {
            Theme = o.Theme;
            CustomColor1 = o.CustomColor1;
            CustomColor2 = o.CustomColor2;
            CustomAccent = o.CustomAccent;
            Opacity = o.Opacity;
            Radius = o.Radius;
            Font = o.Font;
            FontSize = o.FontSize;
            Shadow = o.Shadow;
            Gloss = o.Gloss;
        }
    }
}
