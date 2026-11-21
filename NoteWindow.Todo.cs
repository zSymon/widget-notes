using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WidgetNotes
{
    /// <summary>Listen-Modus: Aufgaben, Haken, Termine.</summary>
    public partial class NoteWindow
    {
        private TodoItem? _dueItem;
        private DateTime _calendarMonth;

        // ---------- Modus ----------

        private void ApplyKind()
        {
            bool todo = Note.Kind == "todo";
            TextContent.Visibility = todo ? Visibility.Collapsed : Visibility.Visible;
            ListContent.Visibility = todo ? Visibility.Visible : Visibility.Collapsed;
            HeaderIcon.Text = todo ? "" : "";
            UpdateHeader();
        }

        private void SwitchKind(string kind)
        {
            if (kind == Note.Kind) return;
            if (kind == "todo" && Note.Items.Count == 0 && !string.IsNullOrWhiteSpace(Note.Text))
            {
                // Vorhandene Zeilen als Aufgaben übernehmen.
                foreach (var raw in Note.Text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    bool done = false;
                    var m = Regex.Match(line, @"^(?:[-*•]\s*)?\[( |x|X)\]\s*");
                    if (m.Success)
                    {
                        done = m.Groups[1].Value is "x" or "X";
                        line = line[m.Length..];
                    }
                    else line = Regex.Replace(line, @"^[-*•]\s+", "");
                    Note.Items.Add(new TodoItem { Text = line, Done = done });
                }
            }
            Note.Kind = kind;
            ApplyKind();
            NoteStore.SaveSoon();
        }

        // ---------- Änderungen beobachten ----------

        private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (TodoItem i in e.NewItems) i.PropertyChanged += Item_PropertyChanged;
            if (e.OldItems != null)
                foreach (TodoItem i in e.OldItems) i.PropertyChanged -= Item_PropertyChanged;
            if (!_loading) Touch();
            ScheduleGrowCheck();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TodoItem.DueLabel) or nameof(TodoItem.DueState) or nameof(TodoItem.HasDue))
            {
                UpdateHeader();
                return;
            }
            if (!_loading) Touch();
            if (e.PropertyName == nameof(TodoItem.Text)) ScheduleGrowCheck();
        }

        // ---------- Tastatur ----------

        private void AddBox_TextChanged(object sender, TextChangedEventArgs e) =>
            AddPlaceholder.Visibility = AddBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void AddBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var text = AddBox.Text.Trim();
                if (text.Length > 0)
                {
                    Note.Items.Add(new TodoItem { Text = text });
                    AddBox.Clear();
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => AddBox.BringIntoView());
                }
                e.Handled = true;
            }
            else if ((e.Key == Key.Back && AddBox.Text.Length == 0) || e.Key == Key.Up)
            {
                if (Note.Items.Count > 0)
                {
                    FocusItem(Note.Items[^1], atEnd: true);
                    e.Handled = true;
                }
            }
        }

        private void TodoText_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb || tb.DataContext is not TodoItem item) return;
            int index = Note.Items.IndexOf(item);

            switch (e.Key)
            {
                case Key.Enter:
                    {
                        // Text ab Cursor in eine neue Aufgabe darunter verschieben.
                        var rest = tb.Text[tb.CaretIndex..];
                        item.Text = tb.Text[..tb.CaretIndex];
                        var next = new TodoItem { Text = rest };
                        Note.Items.Insert(index + 1, next);
                        FocusItem(next, atEnd: false);
                        e.Handled = true;
                        break;
                    }
                case Key.Back when tb.CaretIndex == 0 && tb.SelectionLength == 0:
                    {
                        if (tb.Text.Length == 0)
                        {
                            Note.Items.RemoveAt(index);
                            if (index > 0) FocusItem(Note.Items[index - 1], atEnd: true);
                            else FocusAddBox();
                            e.Handled = true;
                        }
                        break;
                    }
                case Key.Up when tb.GetLineIndexFromCharacterIndex(tb.CaretIndex) <= 0:
                    {
                        if (index > 0) FocusItem(Note.Items[index - 1], atEnd: true);
                        else { TitleBox.Focus(); TitleBox.CaretIndex = TitleBox.Text.Length; }
                        e.Handled = true;
                        break;
                    }
                case Key.Down when tb.GetLineIndexFromCharacterIndex(tb.CaretIndex) >= tb.LineCount - 1:
                    {
                        if (index < Note.Items.Count - 1) FocusItem(Note.Items[index + 1], atEnd: false);
                        else FocusAddBox();
                        e.Handled = true;
                        break;
                    }
            }
        }

        private void FocusTodoStart()
        {
            if (Note.Items.Count > 0) FocusItem(Note.Items[0], atEnd: false);
            else FocusAddBox();
        }

        private void FocusAddBox()
        {
            AddBox.Focus();
            AddBox.BringIntoView();
        }

        private void FocusItem(TodoItem item, bool atEnd)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (TodoList.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container) return;
                var tb = FindChild<TextBox>(container);
                if (tb == null) return;
                tb.Focus();
                tb.CaretIndex = atEnd ? tb.Text.Length : 0;
                tb.BringIntoView();
            });
        }

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match) return match;
                var deeper = FindChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void ClearDone_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            foreach (var done in Note.Items.Where(i => i.Done).ToList())
                Note.Items.Remove(done);
        }

        // ---------- Termin-Auswahl ----------

        private void DueButton_Click(object sender, RoutedEventArgs e) => OpenDuePicker((FrameworkElement)sender);

        private void DueChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OpenDuePicker((FrameworkElement)sender);
            e.Handled = true;
        }

        private void OpenDuePicker(FrameworkElement anchor)
        {
            if (anchor.DataContext is not TodoItem item) return;
            _dueItem = item;
            var d = item.Due ?? DateTime.Today;
            _calendarMonth = new DateTime(d.Year, d.Month, 1);
            TimeBox.Text = item.HasTime && item.Due is DateTime due ? due.ToString("HH:mm") : "";
            BuildCalendar();
            DuePopup.PlacementTarget = anchor;
            DuePopup.IsOpen = true;
        }

        private void BuildCalendar()
        {
            MonthLabel.Text = _calendarMonth.ToString("MMMM yyyy", German);
            DayGrid.Children.Clear();

            int offset = ((int)_calendarMonth.DayOfWeek + 6) % 7; // Woche beginnt Montag
            var start = _calendarMonth.AddDays(-offset);
            var accent = (Brush)Resources["MenuAccent"];
            var onAccent = (Brush)Resources["OnMenuAccent"];
            var dayStyle = (Style)FindResource("DayButton");

            for (int i = 0; i < 42; i++)
            {
                var day = start.AddDays(i);
                var b = new Button { Style = dayStyle, Content = day.Day.ToString(), Tag = day };
                if (day.Month != _calendarMonth.Month) b.Opacity = 0.35;
                if (_dueItem?.Due?.Date == day)
                {
                    b.Background = accent;
                    b.Foreground = onAccent;
                    b.FontWeight = FontWeights.SemiBold;
                }
                else if (day == DateTime.Today)
                {
                    b.BorderBrush = accent;
                }
                b.Click += (_, _) => ApplyDue((DateTime)b.Tag);
                DayGrid.Children.Add(b);
            }
        }

        private void ApplyDue(DateTime date)
        {
            if (_dueItem == null) return;
            var time = ParseTime(TimeBox.Text);
            _dueItem.SetDue(time is TimeSpan t ? date.Date + t : date.Date, time.HasValue);
            if (date.Month != _calendarMonth.Month || date.Year != _calendarMonth.Year)
                _calendarMonth = new DateTime(date.Year, date.Month, 1);
            BuildCalendar();
        }

        private static TimeSpan? ParseTime(string text)
        {
            var m = Regex.Match(text.Trim(), @"^(\d{1,2})(?:[:.\s]?(\d{2}))?\s*(?:uhr)?$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            int h = int.Parse(m.Groups[1].Value);
            int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (h > 23 || min > 59) return null;
            return new TimeSpan(h, min, 0);
        }

        private void QuickDue_Click(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;
            int toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            DateTime date = ((FrameworkElement)sender).Tag switch
            {
                "tomorrow" => today.AddDays(1),
                "nextweek" => today.AddDays(toMonday == 0 ? 7 : toMonday),
                _ => today,
            };
            ApplyDue(date);
            DuePopup.IsOpen = false;
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _calendarMonth = _calendarMonth.AddMonths(-1);
            BuildCalendar();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _calendarMonth = _calendarMonth.AddMonths(1);
            BuildCalendar();
        }

        private void TimeBox_TextChanged(object sender, TextChangedEventArgs e) =>
            TimePlaceholder.Visibility = TimeBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void TimeBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) DueDone_Click(sender, e);
        }

        private void TimeChip_Click(object sender, RoutedEventArgs e)
        {
            TimeBox.Text = (string)((FrameworkElement)sender).Tag;
            ApplyDue(_dueItem?.Due?.Date ?? DateTime.Today);
        }

        private void DueDone_Click(object sender, RoutedEventArgs e)
        {
            if (_dueItem != null && (_dueItem.HasDue || ParseTime(TimeBox.Text).HasValue))
                ApplyDue(_dueItem.Due?.Date ?? DateTime.Today);
            DuePopup.IsOpen = false;
        }

        private void RemoveDue_Click(object sender, RoutedEventArgs e)
        {
            _dueItem?.SetDue(null, false);
            DuePopup.IsOpen = false;
        }
    }
}
