using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using KawaiiIsland.Services;
using Microsoft.Extensions.Logging;

namespace KawaiiIsland;

/// <summary>
/// "Add apps" inside the island: the open island grows taller into a searchable list of installed apps
/// (Start menu + Store), like an Apple drop-down, instead of opening a separate window.
/// </summary>
public partial class IslandWindow
{
    /// <summary>A row; the icon is extracted (and cached) only when the row scrolls into view.</summary>
    public sealed class PickerRow(InstalledApp app, string iconDir, bool pinned)
    {
        public InstalledApp App { get; } = app;
        public string Name => App.Name;
        public string Note { get; } = pinned ? "On the island" : "";
        public ImageSource? Icon => ShortcutLauncher.Icon(App.Path, iconDir);
    }

    private const double PickerMaxHeight = 400;
    private bool _picking, _browsing;

    private void InitPicker()
    {
        PickerSearch.TextChanged += (_, _) =>
        {
            PickerHint.Visibility = Vis(PickerSearch.Text.Length == 0);
            CollectionViewSource.GetDefaultView(PickerList.ItemsSource)?.Refresh();
        };
        PickerSearch.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Down || PickerList.Items.Count == 0) return;
            PickerList.SelectedIndex = 0;
            (PickerList.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
            e.Handled = true;
        };
        PickerList.SelectionChanged += (_, _) =>
        {
            PickerAdd.IsEnabled = PickerList.SelectedItems.Count > 0;
            PickerAdd.Foreground = (Brush)FindResource(PickerAdd.IsEnabled ? "IslandText" : "IslandMuted");
        };
        PickerList.MouseDoubleClick += (_, _) => { if (PickerList.SelectedItem is PickerRow) FinishPicker(); };
        PickerAdd.Click += (_, _) => FinishPicker();
        PickerCancel.Click += (_, _) => ClosePicker();
        PickerBrowse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Pin a file, program or shortcut", Filter = "All files|*.*", Multiselect = true };
            _browsing = true;
            bool ok = dialog.ShowDialog(this) == true;
            _browsing = false;
            if (!ok) { Activate(); return; }
            ClosePicker();
            PinAll(dialog.FileNames);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (!_picking) return;
            if (e.Key == Key.Escape) { ClosePicker(); e.Handled = true; }
            else if (e.Key == Key.Enter && PickerAdd.IsEnabled) { FinishPicker(); e.Handled = true; }
        };
        Deactivated += (_, _) => { if (_picking && !_browsing) ClosePicker(); }; // click away closes it, like a menu
    }

    /// <summary>Opens (or grows) the island into the installed-apps list; "Browse for a file…" covers anything else.</summary>
    public async void ShowAppPicker()
    {
        if (_picking) return;
        if (!IsVisible) ShowIsland();
        _picking = true;
        PickerSearch.Text = "";
        PickerList.ItemsSource = null;
        PickerLoading.Text = "Loading your apps…";
        PickerLoading.Visibility = Visibility.Visible;
        PickerAdd.IsEnabled = false;
        PickerAdd.Foreground = (Brush)FindResource("IslandMuted");
        RenderExpanded();
        SetExpanded(true); // already open: RenderExpanded's FitExpanded grows it taller in place
        PickerPanel.Opacity = 0;
        Fade(PickerPanel, 1, 220, delayMs: 60);
        Activate(); // the island doesn't take focus by itself; typing goes to the search box
        PickerSearch.Focus();

        var pinned = Apps.Items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var apps = await Task.Run(() => StaThread(AppCatalog.All));
        if (!_picking) return;
        var view = CollectionViewSource.GetDefaultView(apps.Select(a => new PickerRow(a, _config.Directory, pinned.Contains(a.Path))).ToList());
        view.Filter = o => PickerSearch.Text.Length == 0 || ((PickerRow)o).Name.Contains(PickerSearch.Text, StringComparison.CurrentCultureIgnoreCase);
        PickerList.ItemsSource = view;
        PickerList.Opacity = 0;
        Fade(PickerList, 1, 220);
        PickerLoading.Visibility = Vis(apps.Count == 0);
        if (apps.Count == 0) PickerLoading.Text = "Couldn't read your apps. Use \"Browse for a file…\".";
    }

    private void FinishPicker()
    {
        var chosen = PickerList.SelectedItems.Cast<PickerRow>().Select(r => r.App).ToList();
        ClosePicker();
        if (chosen.Count(a => ShortcutLauncher.PinApp(Apps.Items, a, Apps.Max) == PinResult.Added) == 0) return;
        Apps.Enabled = true;
        SaveApps();
    }

    /// <summary>Back to the normal open island: it shrinks to fit its usual content.</summary>
    private void ClosePicker()
    {
        if (!_picking) return;
        _picking = false;
        PickerList.ItemsSource = null;
        RenderExpanded();
        ArmAutoCollapse();
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
