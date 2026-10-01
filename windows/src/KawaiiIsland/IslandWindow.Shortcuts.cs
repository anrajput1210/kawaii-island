using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KawaiiIsland.Services;

using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

/// <summary>
/// App shortcuts (spec §3.4): pinned apps in the Home and Apps views. Click launches; right-click: run as
/// administrator, rename, remove; drag a tile onto another to reorder; "+" opens a file picker.
/// Dropping files on the island turns the mascot into a box ("Feed me!" → "Nom!") and pins them.
/// </summary>
public partial class IslandWindow
{
    private const string TileFormat = "KawaiiIsland.Shortcut";

    private readonly DispatcherTimer _dropTimer = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    private string? _dropText; // "Feed me!" / "Nom!" shown in place of the clock while a file is dragged over
    private Point _tileDown;

    private ShortcutsConfig Apps => _config.Current.Modules.Shortcuts;
    private bool AppsOn => Apps.Enabled;

    private void InitShortcuts()
    {
        TabApps.Click += (_, _) => Pick(View.Apps);
        Pill.DragEnter += OnFileDragEnter;
        Pill.DragOver += (_, e) => { e.Effects = IsFileDrag(e) ? DragDropEffects.Copy : e.Effects; e.Handled = IsFileDrag(e); };
        Pill.DragLeave += (_, _) => { if (_dropText == "Feed me!") EndDrop(); };
        Pill.Drop += OnFileDrop;
        _dropTimer.Tick += (_, _) => EndDrop();
        RenderApps();
    }

    public void SetShortcutsEnabled(bool on)
    {
        Apps.Enabled = on;
        _config.SaveSoon();
        RenderApps();
    }

    private void RenderApps()
    {
        AppsPanel.Children.Clear();
        foreach (var (item, index) in Apps.Items.Select((it, i) => (it, i)))
            AppsPanel.Children.Add(Tile(item, index));
        if (Apps.Items.Count < Apps.Max) AppsPanel.Children.Add(AddTile());
        RenderExpanded();
    }

    private Button Tile(ShortcutItem item, int index)
    {
        FrameworkElement icon = ShortcutLauncher.Icon(item.Path, _config.Directory) is { } source
            ? new Image { Width = 28, Height = 28, Source = source }
            : new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 20, // missing app: generic icon
                              Width = 28, Height = 28, TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 4, 0, 0) };
        var tile = new Button { Style = (Style)FindResource("Tile"), Content = TileContent(icon, item.Label), Tag = item, AllowDrop = true, ToolTip = item.Path };
        AutomationProperties(tile, item.Label);
        tile.Click += (_, _) => Launch(item, asAdmin: false);

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Open", () => Launch(item, asAdmin: false)));
        menu.Items.Add(MenuItem("Run as administrator", () => Launch(item, asAdmin: true)));
        menu.Items.Add(MenuItem("Rename…", () => Rename(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Remove", () => { Apps.Items.Remove(item); SaveApps(); }));
        menu.Closed += (_, _) => HideSoon();
        tile.ContextMenu = menu;

        // Drag a tile onto another tile to reorder.
        tile.PreviewMouseLeftButtonDown += (_, e) => _tileDown = e.GetPosition(this);
        tile.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(this) - _tileDown).Length < 6) return;
            DragDrop.DoDragDrop(tile, new DataObject(TileFormat, index), DragDropEffects.Move);
        };
        tile.Drop += (_, e) =>
        {
            if (e.Data.GetData(TileFormat) is not int from) return;
            e.Handled = true;
            ShortcutLauncher.Move(Apps.Items, from, index);
            SaveApps();
        };
        return tile;
    }

    private Button AddTile()
    {
        var plus = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 18,
                                   Width = 28, Height = 28, TextAlignment = TextAlignment.Center, Padding = new Thickness(0, 5, 0, 0) };
        var tile = new Button { Style = (Style)FindResource("Tile"), Content = TileContent(plus, "Add"), ToolTip = "Pin an app (or drop a file on the island)" };
        AutomationProperties(tile, "Add an app");
        tile.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Pin an app", Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*", Multiselect = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) + @"\Programs",
            };
            if (dialog.ShowDialog() == true) PinAll(dialog.FileNames);
        };
        return tile;
    }

    private static StackPanel TileContent(FrameworkElement icon, string label)
    {
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        var panel = new StackPanel();
        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center });
        return panel;
    }

    private static void AutomationProperties(UIElement element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    private static MenuItem MenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void Launch(ShortcutItem item, bool asAdmin)
    {
        if (ShortcutLauncher.Launch(item, asAdmin) is { } error)
        {
            App.Log.LogWarning("Shortcut: {Error}", error);
            MessageBox.Show(error, "Kawaii Island", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else SetExpanded(false);
    }

    private void Rename(ShortcutItem item)
    {
        if (TextPrompt.Show("Rename shortcut", item.Label) is { Length: > 0 } label) { item.Label = label; SaveApps(); }
    }

    private void SaveApps()
    {
        _config.SaveSoon();
        RenderApps();
    }

    /// <returns>How many were pinned.</returns>
    private int PinAll(IEnumerable<string> paths)
    {
        int added = paths.Count(p => ShortcutLauncher.Pin(Apps.Items, p, Apps.Max) == PinResult.Added);
        if (added > 0) { Apps.Enabled = true; SaveApps(); }
        return added;
    }

    // ---------------- file drop: the mascot becomes a box ----------------

    private static bool IsFileDrag(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(TileFormat);

    private void OnFileDragEnter(object sender, DragEventArgs e)
    {
        if (!IsFileDrag(e)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        _dropTimer.Stop();
        ShowDrop("Feed me!", "box.open");
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (!IsFileDrag(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        bool full = Apps.Items.Count >= Apps.Max;
        int added = PinAll(files);
        ShowDrop(added > 0 ? "Nom!" : full ? "I'm full!" : "Got it already", added > 0 ? "box.closed" : "annoyed");
        _dropTimer.Start();
    }

    private void ShowDrop(string text, string face)
    {
        _dropText = text;
        _moodTimer.Stop();
        SetMood(face);
        Clock.Text = text;
        RenderCompact();
    }

    private void EndDrop()
    {
        _dropTimer.Stop();
        _dropText = null;
        SetMood(Pill.IsMouseOver ? "wow" : null);
        UpdateClock();
        RenderCompact();
    }
}

/// <summary>Tiny modal text prompt (WPF has no InputBox). Activatable, unlike the island itself.</summary>
internal static class TextPrompt
{
    public static string? Show(string title, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(6, 4, 6, 4) };
        var ok = new Button { Content = "Save", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } };
        var window = new Window
        {
            Title = title, Width = 340, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = false,
            Content = new StackPanel { Margin = new Thickness(16), Children = { box, buttons } },
        };
        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return window.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
