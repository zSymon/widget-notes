using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WidgetNotes
{
    /// <summary>Optionsmenü und „Anpassen“-Seite.</summary>
    public partial class NoteWindow
    {
        private bool _syncingMenu;
        private int _colorTarget;

        private void New_Click(object sender, RoutedEventArgs e) => App.CreateNote(this);

        private void More_Click(object sender, RoutedEventArgs e)
        {
            SyncMenu();
            MainPage.Visibility = Visibility.Visible;
            CustomizePage.Visibility = Visibility.Collapsed;
            MenuPopup.IsOpen = true;
        }

        private void SyncMenu()
        {
            _syncingMenu = true;

            KindText.IsChecked = Note.Kind != "todo";
            KindTodo.IsChecked = Note.Kind == "todo";
            SizeSmall.IsChecked = Note.Size == "Small";
            SizeMedium.IsChecked = Note.Size == "Medium";
            SizeLarge.IsChecked = Note.Size == "Large";
            SizeTall.IsChecked = Note.Size == "Tall";
            foreach (var child in SwatchPanel.Children)
                if (child is RadioButton rb) rb.IsChecked = (string)rb.Tag == Note.Theme;
            ClearDoneRow.Visibility = Note.Kind == "todo" ? Visibility.Visible : Visibility.Collapsed;

            OpacitySlider.Value = Note.Opacity;
            RadiusSlider.Value = Note.Radius;
            FontSizeSlider.Value = Note.FontSize;
            foreach (var child in FontPanel.Children)
                if (child is RadioButton rb) rb.IsChecked = (string)rb.Tag == Note.Font;
            AutostartSwitch.IsChecked = App.IsAutostartEnabled();
            VersionLabel.Text = $"Version {Updater.Current}";
            if (App.AvailableUpdate is UpdateInfo update)
            {
                UpdateText.Text = $"Update installieren (v{update.Version})";
                UpdateRow.Foreground = (Brush)Resources["MenuAccent"];
            }
            else
            {
                UpdateText.Text = "Nach Updates suchen";
                UpdateRow.ClearValue(ForegroundProperty);
            }
            ShadowSwitch.IsChecked = Note.Shadow;
            GlossSwitch.IsChecked = Note.Gloss;
            UpdateValueLabels();

            _syncingMenu = false;
        }

        private void UpdateValueLabels()
        {
            OpacityValue.Text = $"{Math.Round(Note.Opacity * 100)} %";
            RadiusValue.Text = $"{Math.Round(Note.Radius)}";
            FontSizeValue.Text = $"{Note.FontSize:0} pt";
        }

        private void Kind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string kind })
            {
                SwitchKind(kind);
                ClearDoneRow.Visibility = Note.Kind == "todo" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Size_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string size } || size == Note.Size) return;
            var current = TileRect.TopLeft;
            Note.Size = size;
            ApplySizeDimensions();
            PlaceAt(current, animate: true);
        }

        private void BuildSwatches()
        {
            foreach (var theme in Themes.All)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("SwatchOption"),
                    GroupName = "Theme",
                    Tag = theme.Key,
                    Background = theme.CreateBackground(),
                    ToolTip = theme.Name,
                };
                rb.Click += (_, _) =>
                {
                    Note.Theme = theme.Key;
                    ApplyStyle();
                    NoteStore.SaveSoon();
                };
                SwatchPanel.Children.Add(rb);
            }
        }

        private void BuildFontOptions()
        {
            foreach (var f in NoteFonts.All)
            {
                var rb = new RadioButton
                {
                    Style = (Style)FindResource("SegmentOption"),
                    GroupName = "Font",
                    Tag = f.Key,
                    Padding = new Thickness(9, 5, 9, 5),
                    Content = new TextBlock { Text = f.Name, FontFamily = new FontFamily(f.Text), FontSize = 13 },
                };
                rb.Click += (_, _) =>
                {
                    Note.Font = f.Key;
                    ApplyStyle();
                    NoteStore.SaveSoon();
                };
                FontPanel.Children.Add(rb);
            }
        }

        private void MenuNew_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            App.CreateNote(this);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            bool empty = string.IsNullOrWhiteSpace(Note.Title) && string.IsNullOrWhiteSpace(Note.Text) && Note.Items.Count == 0;
            if (empty) App.DeleteNote(this);
            else ShowConfirm();
        }

        private void ShowConfirm()
        {
            ConfirmOverlay.Visibility = Visibility.Visible;
            ConfirmOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
        }

        private void HideConfirm()
        {
            var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            anim.Completed += (_, _) => ConfirmOverlay.Visibility = Visibility.Collapsed;
            ConfirmOverlay.BeginAnimation(OpacityProperty, anim);
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e) => HideConfirm();

        private void ConfirmDelete_Click(object sender, RoutedEventArgs e) => App.DeleteNote(this);

        // ---------- Anpassen ----------

        private void Customize_Click(object sender, RoutedEventArgs e)
        {
            // Eigene Farben starten mit dem aktuellen Look.
            if (Note.Theme != Themes.CustomKey || Note.CustomColor1 == null)
            {
                var t = Themes.Resolve(Note);
                Note.CustomColor1 = Themes.ToHex(t.Stops[0]);
                Note.CustomColor2 = Themes.ToHex(t.Stops[^1]);
                Note.CustomAccent = Themes.ToHex(t.Accent);
            }
            _colorTarget = 0;
            Target0.IsChecked = true;
            UpdateTargetSwatches();
            Picker.SetColor(TargetColor(_colorTarget));
            SyncMenu();

            MainPage.Visibility = Visibility.Collapsed;
            CustomizePage.Visibility = Visibility.Visible;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            SyncMenu();
            CustomizePage.Visibility = Visibility.Collapsed;
            MainPage.Visibility = Visibility.Visible;
        }

        private Color TargetColor(int target) => Themes.Parse(target switch
        {
            0 => Note.CustomColor1 ?? "#3A3A40",
            1 => Note.CustomColor2 ?? "#1C1C20",
            _ => Note.CustomAccent ?? "#FFD60A",
        });

        private void UpdateTargetSwatches()
        {
            Target0.Background = new SolidColorBrush(TargetColor(0));
            Target1.Background = new SolidColorBrush(TargetColor(1));
            Target2.Background = new SolidColorBrush(TargetColor(2));
        }

        private void Target_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string tag })
            {
                _colorTarget = int.Parse(tag);
                Picker.SetColor(TargetColor(_colorTarget));
            }
        }

        private void Picker_ColorChanged(object? sender, Color c)
        {
            var hex = Themes.ToHex(c);
            switch (_colorTarget)
            {
                case 0: Note.CustomColor1 = hex; break;
                case 1: Note.CustomColor2 = hex; break;
                default: Note.CustomAccent = hex; break;
            }
            Note.Theme = Themes.CustomKey;
            UpdateTargetSwatches();
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || _syncingMenu) return;
            Note.Opacity = Math.Round(e.NewValue, 2);
            UpdateValueLabels();
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void Radius_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || _syncingMenu) return;
            Note.Radius = Math.Round(e.NewValue);
            UpdateValueLabels();
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void FontSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || _syncingMenu) return;
            Note.FontSize = Math.Round(e.NewValue);
            UpdateValueLabels();
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void Shadow_Click(object sender, RoutedEventArgs e)
        {
            Note.Shadow = ShadowSwitch.IsChecked == true;
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void Gloss_Click(object sender, RoutedEventArgs e)
        {
            Note.Gloss = GlossSwitch.IsChecked == true;
            ApplyStyle();
            NoteStore.SaveSoon();
        }

        private void Autostart_Click(object sender, RoutedEventArgs e)
        {
            App.SetAutostart(AutostartSwitch.IsChecked == true);
            AutostartSwitch.IsChecked = App.IsAutostartEnabled();
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            MenuPopup.IsOpen = false;
            if (App.AvailableUpdate != null) App.InstallUpdate();
            else App.CheckForUpdates(manual: true);
        }

        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            App.ApplyStyleToAll(Note);
            MenuPopup.IsOpen = false;
        }
    }
}
