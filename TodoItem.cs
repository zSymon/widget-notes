using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WidgetNotes
{
    public class TodoItem : INotifyPropertyChanged
    {
        private static readonly string[] WeekDays = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

        private string _text = "";
        private bool _done;
        private DateTime? _due;
        private bool _hasTime;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; On(); } }
        }

        public bool Done
        {
            get => _done;
            set { if (_done != value) { _done = value; On(); On(nameof(DueState)); } }
        }

        public DateTime? Due
        {
            get => _due;
            set { _due = value; On(); RaiseDue(); }
        }

        public bool HasTime
        {
            get => _hasTime;
            set { _hasTime = value; On(); RaiseDue(); }
        }

        /// <summary>Wurde für den aktuellen Termin schon eine Erinnerung gezeigt?</summary>
        public bool Notified { get; set; }

        public void SetDue(DateTime? due, bool hasTime)
        {
            _due = due;
            _hasTime = due.HasValue && hasTime;
            Notified = false;
            On(nameof(Due));
            On(nameof(HasTime));
            RaiseDue();
        }

        [JsonIgnore] public bool HasDue => _due.HasValue;

        [JsonIgnore]
        public DateTime? ReminderTime => _due is DateTime d ? (_hasTime ? d : d.Date.AddHours(9)) : null;

        [JsonIgnore]
        public bool IsOverdue =>
            _due is DateTime d && !_done && (_hasTime ? d < DateTime.Now : d.Date < DateTime.Today);

        /// <summary>none, done, overdue, today, future</summary>
        [JsonIgnore]
        public string DueState =>
            _due is not DateTime d ? "none" :
            _done ? "done" :
            IsOverdue ? "overdue" :
            d.Date == DateTime.Today ? "today" : "future";

        [JsonIgnore]
        public string DueLabel
        {
            get
            {
                if (_due is not DateTime d) return "";
                var today = DateTime.Today;
                int diff = (d.Date - today).Days;
                string day =
                    diff == 0 ? "Heute" :
                    diff == 1 ? "Morgen" :
                    diff == -1 ? "Gestern" :
                    diff > 1 && diff < 7 ? WeekDays[(int)d.DayOfWeek] :
                    d.Year == today.Year ? $"{d.Day}.{d.Month}." : $"{d.Day}.{d.Month}.{d.Year % 100:00}";
                return _hasTime ? $"{day} {d:HH:mm}" : day;
            }
        }

        /// <summary>Zeitabhängige Anzeigen neu berechnen (z. B. jede Minute).</summary>
        public void Refresh() => RaiseDue();

        private void RaiseDue()
        {
            On(nameof(HasDue));
            On(nameof(DueLabel));
            On(nameof(DueState));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void On([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
