using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using KawaiiIsland.Controls;
using KawaiiIsland.Services;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

/// <summary>Installed-apps list (Start menu, desktop + Store) with search, instead of browsing for .exe files.</summary>
public partial class AppPickerWindow : Window
{
    /// <summary>A row; the icon is extracted (and cached) only when the row scrolls into view.</summary>
    public sealed class Row(InstalledApp app, string iconDir, bool pinned)
    {
        public InstalledApp App { get; } = app;
        public string Name => App.Name;
        public string Note { get; } = pinned ? "On the island" : "";
        public ImageSource? Icon => ShortcutLauncher.Icon(App.Path, iconDir);
    }

    public List<InstalledApp> ChosenApps { get; } = [];
    public string[] ChosenFiles { get; private set; } = [];

    private bool _closing, _browsing;

    /// <param name="anchor">Island bounds in screen DIPs: the drop-down opens just below, centred on it.</param>
    public AppPickerWindow(string iconDir, IEnumerable<string> pinnedPaths, Rect anchor)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Services.Native.Win32.UseGlassBackdrop(new WindowInteropHelper(this).Handle);
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        Left = Math.Clamp(anchor.Left + (anchor.Width - Width) / 2, screen.Left, screen.Right - Width);
        Top = Math.Clamp(anchor.Bottom + 8, screen.Top, screen.Bottom - Height);
        ContentRendered += (_, _) => Animate(true, null);
        Deactivated += (_, _) => { if (!_browsing) Done(false); }; // click outside closes it, like a menu
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Done(false); };
        CancelButton.Click += (_, _) => Done(false);
        var pinned = pinnedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Loaded += async (_, _) =>
        {
            Search.Focus();
            var apps = await Task.Run(() => StaThread(AppCatalog.All));
            var view = CollectionViewSource.GetDefaultView(apps.Select(a => new Row(a, iconDir, pinned.Contains(a.Path))).ToList());
            view.Filter = o => Search.Text.Length == 0 || ((Row)o).Name.Contains(Search.Text, StringComparison.CurrentCultureIgnoreCase);
            List.ItemsSource = view;
            Loading.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (apps.Count == 0) Loading.Text = "Couldn't read your apps. Use \"Browse for a file…\".";
            Search.TextChanged += (_, _) => view.Refresh();
        };
        Search.KeyDown += (_, e) => { if (e.Key == Key.Down && List.Items.Count > 0) { List.SelectedIndex = 0; ((UIElement)List.ItemContainerGenerator.ContainerFromIndex(0))?.Focus(); } };
        Search.TextChanged += (_, _) => Hint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.SelectionChanged += (_, _) => AddButton.IsEnabled = List.SelectedItems.Count > 0;
        List.MouseDoubleClick += (_, _) => { if (List.SelectedItem is Row) Finish(); };
        AddButton.Click += (_, _) => Finish();
        BrowseButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Pin a file, program or shortcut", Filter = "All files|*.*", Multiselect = true };
            _browsing = true;
            bool ok = dialog.ShowDialog(this) == true;
            _browsing = false;
            if (!ok) { Activate(); return; }
            ChosenFiles = dialog.FileNames;
            Done(true);
        };
    }

    private void Finish()
    {
        ChosenApps.AddRange(List.SelectedItems.Cast<Row>().Select(r => r.App));
        Done(true);
    }

    /// <summary>Fade/slide out, then close with the result.</summary>
    private void Done(bool result)
    {
        if (_closing) return;
        _closing = true;
        Animate(false, () => DialogResult = result);
    }

    /// <summary>Drops down from the island: fade + 10px slide + slight scale, Apple ease-out in, quick ease-in out.</summary>
    private void Animate(bool show, Action? then)
    {
        var ms = Motion.Ms(show ? 280 : 140);
        IEasingFunction ease = show ? Motion.Smooth : new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(show ? 1 : 0, ms) { EasingFunction = ease };
        if (then != null) fade.Completed += (_, _) => then();
        Panel.BeginAnimation(OpacityProperty, fade);
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : -6, ms) { EasingFunction = ease });
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            Scale.BeginAnimation(p, new DoubleAnimation(show ? 1 : 0.97, ms) { EasingFunction = ease });
    }

    /// <summary>Shell.Application is an STA COM object: enumerate it on a dedicated STA thread, off the UI thread.</summary>
    private static List<InstalledApp> StaThread(Func<List<InstalledApp>> work)
    {
        List<InstalledApp> result = [];
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { App.Log.LogError(ex, "Couldn't list installed apps"); } // COM/shell failure: picker falls back to Browse
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }
}
