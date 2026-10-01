using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

    public AppPickerWindow(string iconDir, IEnumerable<string> pinnedPaths)
    {
        InitializeComponent();
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
        List.SelectionChanged += (_, _) => AddButton.IsEnabled = List.SelectedItems.Count > 0;
        List.MouseDoubleClick += (_, _) => { if (List.SelectedItem is Row) Finish(); };
        AddButton.Click += (_, _) => Finish();
        BrowseButton.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Pin a file, program or shortcut", Filter = "All files|*.*", Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            ChosenFiles = dialog.FileNames;
            DialogResult = true;
        };
    }

    private void Finish()
    {
        ChosenApps.AddRange(List.SelectedItems.Cast<Row>().Select(r => r.App));
        DialogResult = true;
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
