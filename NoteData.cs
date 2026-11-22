using System;
using System.Collections.ObjectModel;

namespace WidgetNotes
{
    public class NoteData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>"text", "todo" oder "spotify".</summary>
        public string Kind { get; set; } = "text";
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public ObservableCollection<TodoItem> Items { get; set; } = new();

        /// <summary>Letzte vom Nutzer gewählte Position (ohne Schattenrand) in WPF-Einheiten.</summary>
        public double? X { get; set; }
        public double? Y { get; set; }

        /// <summary>„Zuhause“ der Kachel: Bildschirm (Pixel-Bounds + Gerätename) …</summary>
        public string? ScreenBounds { get; set; }
        public string? ScreenDevice { get; set; }

        /// <summary>… und Abstand zur linken oberen Ecke des Arbeitsbereichs. Klebt die Kachel am rechten/unteren
        /// Rand, bleibt sie dort auch bei anderer Auflösung.</summary>
        public double? OffsetX { get; set; }
        public double? OffsetY { get; set; }
        public bool AnchorRight { get; set; }
        public bool AnchorBottom { get; set; }

        /// <summary>Ältere Versionen (1.1–1.3): Rasterzelle. Wird beim Start in Offset umgerechnet.</summary>
        public int? GridX { get; set; }
        public int? GridY { get; set; }

        /// <summary>Vorgabegröße (Small, Medium, Large, Tall) – gilt nur, solange W/H nicht gesetzt sind.</summary>
        public string Size { get; set; } = "Medium";

        /// <summary>Freie Größe in Rastereinheiten.</summary>
        public int? W { get; set; }
        public int? H { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public (int W, int H) Units => (W ?? GridLayout.Units(Size).W, H ?? GridLayout.Units(Size).H);

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

        /// <summary>Spotify-Player: weichgezeichnetes Cover und Farben aus dem Cover (Betterfy-Stil).</summary>
        public bool CoverBackdrop { get; set; } = true;

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
