using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KawaiiIsland.Controls;
using KawaiiIsland.Services;
using KawaiiIsland.Services.ClaudeCode;

namespace KawaiiIsland;

/// <summary>
/// Settings: every control writes straight to the config and asks the island to re-apply (debounced),
/// so changes are live. Rows are built in code so all sliders/segments behave identically.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly (string Name, string Hex)[] Accents =
        [("Pink", "#FF8FB1"), ("Lavender", "#BF7BFF"), ("Sky", "#4DA3FF"), ("Mint", "#30D158"), ("Tangerine", "#FF9F0A")];

    private readonly App _app;
    private AppConfig C => _app.Config.Current;
    private IslandWindow Island => _app.Island;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Reload();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    /// <summary>Rebuilds every row from the current config (called after changes made elsewhere, e.g. the menus).</summary>
    public void Reload()
    {
        var w = C.Window;
        bool vertical = Placement.ParseEdge(w.DockEdge) is Edge.Left or Edge.Right;
        HeaderMascot.Source = MascotControl.Art(C.Appearance.Mascot, "happy");

        SizePanel.Children.Clear();
        Heading(SizePanel, "Collapsed");
        SliderRow(SizePanel, "Width", 140, 260, w.CollapsedWidth, v => w.CollapsedWidth = v);
        SliderRow(SizePanel, "Height", 28, 48, w.CollapsedHeight, v => w.CollapsedHeight = v);
        Heading(SizePanel, "Expanded");
        SliderRow(SizePanel, "Width", 440, 640, w.ExpandedWidth, v => w.ExpandedWidth = v);
        SliderRow(SizePanel, "Height", 160, 240, w.ExpandedHeight, v => w.ExpandedHeight = v);
        Heading(SizePanel, "Shape");
        SliderRow(SizePanel, "Radius", 12, 44, w.CornerRadius, v => w.CornerRadius = v);
        SliderRow(SizePanel, "Opacity %", 40, 100, w.Opacity * 100, v => w.Opacity = v / 100);

        PositionPanel.Children.Clear();
        Switch(PositionPanel, "Reserve workspace (AppBar)", "Windows stop below the island instead of sliding under it.",
               w.AppBarEnabled, on => { Island.SetAppBar(on); Later(Reload); });
        Field(PositionPanel, "Edge", Segments([("Top", "Top"), ("Bottom", "Bottom"), ("Left", "Left"), ("Right", "Right")],
               w.DockEdge, v => { w.DockEdge = v; Changed(); Later(Reload); }));
        Field(PositionPanel, "Alignment", Segments(App.AlignmentChoices(vertical), w.Alignment, v => { w.Alignment = v; Changed(); }));
        Field(PositionPanel, "Monitor", MonitorBox());
        Switch(PositionPanel, "Unlock to drag", "Drag the island anywhere; it snaps to edges and the centre line.",
               !w.Locked, on => { Island.SetLocked(!on); Later(Reload); });
        Switch(PositionPanel, "Auto-hide in fullscreen", "Slides away for games, videos and slideshows. Hover the edge and the mascot peeks out.",
               w.AutoHideFullscreen, on => Island.SetAutoHide(on, w.AutoHideAlways));
        Switch(PositionPanel, "Auto-hide always", "Stays tucked away until you hover the edge.",
               w.AutoHideAlways, on => Island.SetAutoHide(w.AutoHideFullscreen, on));

        BehaviorPanel.Children.Clear();
        Field(BehaviorPanel, "Collapse after", Segments(App.CollapseChoices, Island.AutoCollapseSeconds.ToString(),
               v => Island.SetAutoCollapse(int.Parse(v))));
        Switch(BehaviorPanel, "Music", "Shows what's playing in any app that uses Windows media controls: Spotify, Apple Music, browsers, VLC…",
               Island.MusicEnabled, Island.SetMusicEnabled);

        AppearancePanel.Children.Clear();
        Field(AppearancePanel, "Theme", Segments([("Dark", "dark"), ("Light", "light"), ("Auto", "auto")], C.Appearance.Theme,
               v => { C.Appearance.Theme = v; _app.ApplyTheme(); _app.Config.SaveSoon(); }));
        Field(AppearancePanel, "Accent", Swatches());
        Field(AppearancePanel, "Mascot", MascotPicker());

        NotifyPanel.Children.Clear();
        var notify = C.Modules.Notifications;
        Switch(NotifyPanel, "Show notifications",
               Island.NotificationProblem ?? "Mirrors new Windows notifications on the island. They're kept in memory on this PC only.",
               notify.Enabled, async on => { await Island.SetNotificationsEnabled(on); Later(Reload); });
        if (Island.NotificationProblem is not null)
        {
            var open = new Button { Content = "Open Windows notification settings", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
            NotifyPanel.Children.Add(open);
        }
        Switch(NotifyPanel, "Do not disturb", "New notifications still show in the pill but don't open the island.",
               notify.Dnd, Island.SetDnd);
        Field(NotifyPanel, "Muted apps", MutedApps());

        CodePanel.Children.Clear();
        Switch(CodePanel, "Code mode (Claude Code)",
               $"Shows what Claude Code is doing, context used and plan usage left. Adds hooks to {ClaudeSettings.SettingsPath}; turning it off removes them.",
               Island.CodeMode, on => { _app.SetCodeMode(on); Later(Reload); });

        Footer.Text = $"Saved on this PC in {_app.Config.Directory}";
    }

    private void Changed()
    {
        _app.Config.SaveSoon();
        Island.ApplySettingsSoon();
    }

    /// <summary>Rebuilding the panel from inside one of its own control's events is unsafe; defer it.</summary>
    private void Later(Action action) => Dispatcher.BeginInvoke(action);

    // ---------------- row builders ----------------

    private static void Heading(Panel host, string text) =>
        host.Children.Add(Label(text, 12.5, "IslandText", bold: true, margin: new Thickness(0, host.Children.Count == 0 ? 2 : 10, 0, 2)));

    private void SliderRow(Panel host, string label, double min, double max, double value, Action<double> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        var name = Label(label, 12.5, "IslandMuted");
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max),
            IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0),
        };
        AutomationProperties.SetName(slider, label);
        var box = new TextBox { TextAlignment = TextAlignment.Center, Padding = new Thickness(2, 3, 2, 3), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, label + " value");
        box.SetResourceReference(BackgroundProperty, "SettingsBackground");
        box.SetResourceReference(ForegroundProperty, "IslandText");
        box.SetResourceReference(BorderBrushProperty, "SettingsLine");
        box.SetBinding(TextBox.TextProperty, new Binding(nameof(Slider.Value)) { Source = slider, StringFormat = "{0:0}", UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); };
        slider.ValueChanged += (_, e) => { set(e.NewValue); Changed(); };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(box, 2);
        grid.Children.Add(name); grid.Children.Add(slider); grid.Children.Add(box);
        host.Children.Add(grid);
    }

    private static void Field(Panel host, string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        panel.Children.Add(Label(label, 12.5, "IslandText", bold: true, margin: new Thickness(0, 0, 0, 6)));
        panel.Children.Add(control);
        host.Children.Add(panel);
    }

    private void Switch(Panel host, string label, string caption, bool value, Action<bool> set)
    {
        var content = new StackPanel();
        content.Children.Add(Label(label, 13, "IslandText", bold: true));
        var cap = Label(caption, 11.5, "IslandMuted");
        cap.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(cap);
        var toggle = new CheckBox { Content = content, IsChecked = value, Style = (Style)FindResource("Switch") };
        AutomationProperties.SetName(toggle, label);
        toggle.Click += (_, _) => set(toggle.IsChecked == true);
        host.Children.Add(toggle);
    }

    private FrameworkElement Segments((string Label, string Value)[] options, string current, Action<string> pick)
    {
        var group = Guid.NewGuid().ToString("N");
        var row = new WrapPanel();
        foreach (var (label, value) in options)
        {
            var rb = new RadioButton
            {
                Content = label, GroupName = group, Style = (Style)FindResource("Seg"),
                IsChecked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase),
            };
            rb.Checked += (_, _) => pick(value); // attached after IsChecked, so building never fires it
            row.Children.Add(rb);
        }
        var frame = new Border { Child = row, Padding = new Thickness(2), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        frame.SetResourceReference(Border.BackgroundProperty, "SettingsBackground");
        frame.SetResourceReference(Border.BorderBrushProperty, "SettingsLine");
        return frame;
    }

    private ComboBox MonitorBox()
    {
        var box = new ComboBox { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(box, "Monitor");
        var monitors = App.MonitorChoices();
        foreach (var (label, device) in monitors) box.Items.Add(new ComboBoxItem { Content = label, Tag = device });
        int index = Array.FindIndex(monitors, m => m.Value == C.Window.MonitorId);
        box.SelectedIndex = index >= 0 ? index : Math.Max(0, Array.FindIndex(monitors, m => m.Label.Contains("(primary)")));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string device }) { C.Window.MonitorId = device; Changed(); }
        };
        return box;
    }

    /// <summary>One chip per muted app; clicking it unmutes.</summary>
    private FrameworkElement MutedApps()
    {
        var muted = C.Modules.Notifications.Muted;
        if (muted.Count == 0) return Label("None. Use \"Mute\" on a notification in the island to silence an app.", 11.5, "IslandMuted");
        var row = new WrapPanel();
        foreach (var app in muted.ToList())
        {
            var chip = new Button { Content = app + "  ✕", ToolTip = "Unmute " + app, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6) };
            AutomationProperties.SetName(chip, "Unmute " + app);
            chip.Click += (_, _) => { Island.UnmuteApp(app); Later(Reload); };
            row.Children.Add(chip);
        }
        return row;
    }

    private FrameworkElement Swatches()
    {
        var row = new WrapPanel();
        foreach (var (name, hex) in Accents)
        {
            var dot = new Ellipse { Width = 24, Height = 24, Fill = (Brush)new BrushConverter().ConvertFromString(hex)! };
            var rb = new RadioButton
            {
                Content = dot, GroupName = "accent", Style = (Style)FindResource("Pick"), ToolTip = name,
                IsChecked = string.Equals(hex, C.Appearance.Accent, StringComparison.OrdinalIgnoreCase),
            };
            AutomationProperties.SetName(rb, $"Accent {name}");
            rb.Checked += (_, _) => { C.Appearance.Accent = hex; _app.ApplyTheme(); _app.Config.SaveSoon(); };
            row.Children.Add(rb);
        }
        return row;
    }

    private FrameworkElement MascotPicker()
    {
        var row = new WrapPanel();
        foreach (var key in AppConfig.Mascots)
        {
            var rb = new RadioButton
            {
                Content = new Image { Width = 38, Height = 38, Source = MascotControl.Art(key, "idle") },
                GroupName = "mascot", Style = (Style)FindResource("Pick"), ToolTip = App.MascotName(key),
                IsChecked = key == C.Appearance.Mascot,
            };
            AutomationProperties.SetName(rb, App.MascotName(key));
            rb.Checked += (_, _) =>
            {
                C.Appearance.Mascot = key;
                HeaderMascot.Source = MascotControl.Art(key, "happy");
                Changed();
            };
            row.Children.Add(rb);
        }
        return row;
    }

    private static TextBlock Label(string text, double size, string brushKey, bool bold = false, Thickness margin = default)
    {
        var t = new TextBlock { Text = text, FontSize = size, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return t;
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_app.Config.Directory}\"") { UseShellExecute = true });
}
