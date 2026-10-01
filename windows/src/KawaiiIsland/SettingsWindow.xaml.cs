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
using KawaiiIsland.Services.Mail;

namespace KawaiiIsland;

/// <summary>
/// Settings: every control writes straight to the config and asks the island to re-apply (debounced),
/// so changes are live. Rows are built in code so all sliders/segments behave identically.
/// </summary>
public partial class SettingsWindow : Window
{
    private static readonly (string Name, string Hex)[] Accents =
        [("Pink", "#FF375F"), ("Blue", "#0A84FF"), ("Purple", "#BF5AF2"), ("Green", "#30D158"), ("Orange", "#FF9F0A"), ("Teal", "#64D2FF")]; // Apple system colours

    private readonly App _app;
    private AppConfig C => _app.Config.Current;
    private IslandWindow Island => _app.Island;

    private static string _lastPage = "General"; // reopening Settings returns to the page you left

    private (RadioButton Nav, StackPanel Page, string Caption)[] Pages => [
        (NavGeneral, PageGeneral, "Startup, keyboard shortcut, collapsing and which modules are on."),
        (NavWidgets, PageWidgets, "Choose what the island shows: time, weather, batteries and your apps."),
        (NavPosition, PagePosition, "Where the island lives and how it gets out of the way."),
        (NavAppearance, PageAppearance, "Theme, accent colour, mascot and the island's size."),
        (NavNotifications, PageNotifications, "Windows notifications mirrored on the island. Kept in memory on this PC only."),
        (NavMail, PageMail, "Unread count and your latest messages. Headers only, never bodies."),
        (NavAgents, PageAgents, "Follow Claude Code, Codex, Gemini CLI, Cursor or any agent that can call a hook."),
        (NavAbout, PageAbout, "Version, where your data lives, and logs."),
    ];

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        foreach (var (nav, page, caption) in Pages)
            nav.Checked += (_, _) => ShowPage(nav, page, caption);
        AboutVersion.Text = $"Kawaii Island {typeof(App).Assembly.GetName().Version?.ToString(3)}";
        Reload();
        (Pages.FirstOrDefault(p => (string)p.Nav.Content == _lastPage).Nav ?? NavGeneral).IsChecked = true;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SourceInitialized += (_, _) => KawaiiIsland.Services.Native.Win32.UseDarkTitleBar(
            new System.Windows.Interop.WindowInteropHelper(this).Handle, ((SolidColorBrush)FindResource("SettingsBackground")).Color.R < 128);
    }

    private void ShowPage(RadioButton nav, StackPanel page, string caption)
    {
        foreach (var (_, p, _) in Pages) p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = _lastPage = (string)nav.Content;
        PageCaption.Text = caption;
        PageScroll.ScrollToTop();
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
        Switch(BehaviorPanel, "Start with Windows", "Opens the island when you sign in.",
               C.Behavior.StartWithWindows, on => { C.Behavior.StartWithWindows = on; _app.Config.SaveSoon(); _app.SyncStartWithWindows(explicitToggle: true); });
        var hotkey = new TextBox { Text = C.Behavior.Hotkey, Padding = new Thickness(6, 4, 6, 4), Width = 160, HorizontalAlignment = HorizontalAlignment.Left,
                                   ToolTip = "e.g. Ctrl+Alt+I. Leave empty for none. Press Enter to apply." };
        StyleBox(hotkey);
        AutomationProperties.SetName(hotkey, "Keyboard shortcut");
        var hotkeyNote = Label(Island.HotkeyProblem ?? "Opens and closes the island from anywhere.", 11.5, "IslandMuted");
        void ApplyHotkey()
        {
            C.Behavior.Hotkey = hotkey.Text.Trim();
            _app.Config.SaveSoon();
            hotkeyNote.Text = Island.ApplyHotkey() ?? "Opens and closes the island from anywhere.";
        }
        hotkey.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHotkey(); };
        hotkey.LostFocus += (_, _) => { if (hotkey.Text.Trim() != C.Behavior.Hotkey) ApplyHotkey(); };
        var hotkeyRow = new StackPanel { Children = { hotkey, hotkeyNote } };
        Field(BehaviorPanel, "Keyboard shortcut", hotkeyRow, below: true);
        Switch(BehaviorPanel, "Music", "Shows what's playing in any app that uses Windows media controls: Spotify, Apple Music, browsers, VLC…",
               Island.MusicEnabled, Island.SetMusicEnabled);

        BuildWidgets();

        AppearancePanel.Children.Clear();
        Field(AppearancePanel, "Theme", Segments([("Dark", "dark"), ("Light", "light"), ("Auto", "auto")], C.Appearance.Theme,
               v => { C.Appearance.Theme = v; _app.ApplyTheme(); _app.Config.SaveSoon(); }));
        Field(AppearancePanel, "Accent", Swatches());
        Field(AppearancePanel, "Shape", Segments([("Pill", "pill"), ("Notch", "notch")], C.Appearance.Shape,
               v => { C.Appearance.Shape = v; Changed(); }));
        Field(AppearancePanel, "Mascot", MascotPicker(), below: true);

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
        Switch(NotifyPanel, "Show message previews", "Off: the island shows only who it's from. People nearby can see your screen.",
               notify.ShowPreview, on => { notify.ShowPreview = on; _app.Config.SaveSoon(); });
        Field(NotifyPanel, "Muted apps", MutedApps(), below: true);

        BuildMail();

        CodePanel.Children.Clear();
        var code = C.Modules.Code;
        Switch(CodePanel, "Coding mode", "Listens on this PC for agent activity, puts the mascot in its hoodie and keeps alerts quiet. Also the </> button on the island.",
               Island.CodeMode, on => { _app.SetCodeMode(on); Later(Reload); });
        Switch(CodePanel, "Claude Code", $"Live status, context and plan usage, Allow / Deny on the island. Adds hooks to {ClaudeSettings.SettingsPath}; off removes them.",
               code.ClaudeHooks, on => { _app.SetClaudeHooks(on); Later(Reload); });
        Switch(CodePanel, "Approve from the island",
               "Permission prompts show Allow / Deny on the island. Unanswered after 2 minutes, they go back to the terminal.",
               code.Approvals, on => { code.Approvals = on; _app.Config.SaveSoon(); });
        string url = $"http://127.0.0.1:{code.Port}/kawaii";
        Field(CodePanel, "Codex CLI · add to ~/.codex/config.toml", Snippet(
            $"notify = [\"curl\", \"-s\", \"-m\", \"2\", \"-H\", \"Content-Type: application/json\", \"{url}/codex\", \"--data-binary\"]"), below: true);
        Field(CodePanel, "Any agent (Gemini CLI, Cursor, Aider, your scripts…) · POST an event from its hooks", Snippet(
            $"curl -s -m 2 {url}/event -H \"Content-Type: application/json\" -d \"{{\\\"agent\\\":\\\"Gemini CLI\\\",\\\"state\\\":\\\"working\\\",\\\"detail\\\":\\\"npm test\\\"}}\"" +
            "\n\nstate: working · tool · needs_input · done · idle · end      optional: session, cwd, project, detail" +
            $"\nAsk for approval: POST the same JSON to {url}/ask; the reply is allow, deny or empty (no answer).\n" +
            "Or with the npm package: kawaii-island event --agent \"Gemini CLI\" --state working --detail \"npm test\""), below: true);

        Footer.Text = $"Saved on this PC in {_app.Config.Directory}";
    }

    /// <summary>Mail (spec §3.1). Field edits apply on "Save & connect"; the password goes to DPAPI, never config.json.</summary>
    private void BuildMail()
    {
        var mail = C.Modules.Mail;
        MailSettingsPanel.Children.Clear();
        Switch(MailSettingsPanel, "Show mail", Island.MailProblem ?? "Unread count on the island and your latest 5 messages (headers only, never bodies).",
               mail.Enabled, async on => { mail.Enabled = on; _app.Config.SaveSoon(); await Island.RestartMailAsync(); Later(Reload); });
        Field(MailSettingsPanel, "Account", Segments([("Demo", "mock"), ("Gmail", "google"), ("Outlook", "microsoft"), ("Other (IMAP)", "imap")], mail.Provider,
               async v => { mail.Provider = v; _app.Config.SaveSoon(); await Island.RestartMailAsync(); Later(Reload); }));
        if (OAuthProvider.For(mail.Provider) is { } oauth) { BuildSignIn(oauth); return; }
        if (mail.Provider != "imap") return;

        var server = TextRow("Server", mail.Server, "imap.gmail.com");
        var port = TextRow("Port", mail.Port.ToString(), "993");
        var user = TextRow("Username", mail.Username, "you@example.com");
        var password = new PasswordBox { Padding = new Thickness(6, 4, 6, 4) };
        StyleBox(password);
        AutomationProperties.SetName(password, "Password");
        Field(MailSettingsPanel, MailSecret.Exists(_app.Config.Directory) ? "Password (saved, type to replace)" : "App password", password);
        var url = TextRow("Open mail at (optional)", mail.OpenUrl, "https://… (blank = guess from server)");
        Switch(MailSettingsPanel, "SSL/TLS", "Port 993 uses SSL. Off = STARTTLS when the server offers it.", mail.Ssl, on => mail.Ssl = on);

        var save = new Button { Content = "Save & connect", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += async (_, _) =>
        {
            mail.Server = server.Text.Trim();
            mail.Port = int.TryParse(port.Text, out var p) ? Math.Clamp(p, 1, 65535) : 993;
            mail.Username = user.Text.Trim();
            string link = url.Text.Trim();
            mail.OpenUrl = link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? link : "";
            if (password.Password.Length > 0) MailSecret.Save(_app.Config.Directory, password.Password);
            _app.Config.SaveSoon();
            save.IsEnabled = false;
            save.Content = "Connecting…";
            await Island.RestartMailAsync();
            Later(Reload);
        };
        MailSettingsPanel.Children.Add(save);
        var note = Label("Gmail/Outlook: use an app password (OAuth isn't supported yet). Everything stays on this PC.", 11.5, "IslandMuted");
        note.TextWrapping = TextWrapping.Wrap;
        MailSettingsPanel.Children.Add(note);

        TextBox TextRow(string label, string value, string hint)
        {
            var box = new TextBox { Text = value, Padding = new Thickness(6, 4, 6, 4), ToolTip = hint };
            StyleBox(box);
            AutomationProperties.SetName(box, label);
            Field(MailSettingsPanel, label, box);
            return box;
        }
    }

    /// <summary>Read-only, selectable code block for copy-paste setup snippets.</summary>
    private static TextBox Snippet(string text)
    {
        var box = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10, 8, 10, 8),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, BorderThickness = new Thickness(1),
        };
        StyleBox(box);
        return box;
    }

    /// <summary>Sign in with Google / Microsoft: one button, the browser does the rest. Nothing to register or paste.</summary>
    private void BuildSignIn(OAuthProvider oauth)
    {
        var mail = C.Modules.Mail;
        string dir = _app.Config.Directory;
        var account = OAuth.Load(dir) is { } a && a.Provider == oauth.Key ? a : null;

        var button = new Button { Padding = new Thickness(16, 6, 16, 6) };
        if (account is null && Island.ClientFor(oauth).Id.Length == 0)
        {
            var na = Label($"Sign in with {oauth.Name} isn't available in this build. Use Other (IMAP) with an app password for now.", 12.5, "IslandMuted");
            na.TextWrapping = TextWrapping.Wrap;
            Row(MailSettingsPanel, na);
            return;
        }
        if (account is null)
        {
            button.Content = $"Sign in with {oauth.Name}";
            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                button.Content = "Finish signing in in your browser…";
                try
                {
                    var (id, secret) = Island.ClientFor(oauth);
                    OAuth.Save(dir, await OAuth.SignInAsync(oauth, id, secret, CancellationToken.None));
                    await Island.RestartMailAsync();
                    await Island.RefreshCalendarAsync(); // same account: Google Calendar / Outlook calendar
                }
                catch (Exception ex) when (ex is OAuthException or System.Net.Http.HttpRequestException or System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(this, ex.Message, $"Sign in with {oauth.Name}", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                Later(Reload);
            };
            Field(MailSettingsPanel, "Account", button);
        }
        else
        {
            button.Content = "Sign out";
            button.Click += async (_, _) => { OAuth.Delete(dir); await Island.RestartMailAsync(); await Island.RefreshCalendarAsync(); Later(Reload); };
            Field(MailSettingsPanel, $"Signed in as {(account.Email.Length > 0 ? account.Email : oauth.Name)}", button);
        }

    }

    /// <summary>Settings → Widgets: what the resting pill and the open island show.</summary>
    private void BuildWidgets()
    {
        var wg = C.Modules.Widgets;
        void Pill(Action set) { set(); Changed(); _ = Island.RefreshWidgetsAsync(); } // pill width changes: re-place
        void Home(Action set) { set(); _app.Config.SaveSoon(); _ = Island.RefreshWidgetsAsync(); }

        PillWidgetsPanel.Children.Clear();
        Switch(PillWidgetsPanel, "Time", "The clock next to the mascot.", wg.PillClock, on => Pill(() => wg.PillClock = on));
        Switch(PillWidgetsPanel, "Weather", "Temperature and sky, e.g. ☀ 21°.", wg.PillWeather, on => Pill(() => wg.PillWeather = on));
        Switch(PillWidgetsPanel, "Battery", "Your laptop's charge.", wg.PillBattery, on => Pill(() => wg.PillBattery = on));

        HomeWidgetsPanel.Children.Clear();
        Switch(HomeWidgetsPanel, "Time and date", "Big clock with today's date.", wg.HomeClock, on => Home(() => wg.HomeClock = on));
        Switch(HomeWidgetsPanel, "Weather", "Temperature, sky and place.", wg.HomeWeather, on => Home(() => wg.HomeWeather = on));
        Switch(HomeWidgetsPanel, "Laptop battery", "Charge and whether it's plugged in.", wg.HomeBattery, on => Home(() => wg.HomeBattery = on));
        Switch(HomeWidgetsPanel, "Bluetooth devices", "Battery of connected headphones, mice and controllers that report it (as in Windows Settings).",
               wg.HomeBluetooth, on => Home(() => wg.HomeBluetooth = on));
        Switch(HomeWidgetsPanel, "Calendar", "Upcoming events from the Google or Outlook account you signed in with (Settings → Mail), plus your own tasks.",
               wg.Calendar, on => Home(() => wg.Calendar = on));
        Switch(HomeWidgetsPanel, "System", "CPU, memory, disk and network, with Lock, Sleep, Restart and Shut down.",
               wg.ShowSystem, on => Home(() => wg.ShowSystem = on));
        Switch(HomeWidgetsPanel, "Pinned apps", "Your apps under the clock. Right-click one on the island to rename or remove it.",
               C.Modules.Shortcuts.Enabled, Island.SetShortcutsEnabled);
        var choose = new Button { Content = "Choose apps…", Padding = new Thickness(14, 5, 14, 5) };
        choose.Click += (_, _) => { Close(); Island.ShowAppPicker(); }; // the list opens inside the island, which Settings hides
        Field(HomeWidgetsPanel, "Apps on the island", choose);
        Field(HomeWidgetsPanel, "Max pinned apps", Segments([("4", "4"), ("6", "6"), ("8", "8"), ("12", "12")], C.Modules.Shortcuts.Max.ToString(),
               v => { C.Modules.Shortcuts.Max = int.Parse(v); _app.Config.SaveSoon(); Island.SetShortcutsEnabled(C.Modules.Shortcuts.Enabled); }));

        LivePanel.Children.Clear();
        void Live(string title, string caption, bool value, Action<bool> set) => Switch(LivePanel, title, caption, value, on => { set(on); _app.Config.SaveSoon(); });
        Live("Volume", "A level bar on the island when the volume changes.", wg.LiveVolume, on => wg.LiveVolume = on);
        Live("Charging and low battery", "Green battery when you plug in; a warning at 20 % and 10 %.", wg.LiveBattery, on => wg.LiveBattery = on);
        Live("Bluetooth devices", "Connected / disconnected, with battery when Windows knows it.", wg.LiveBluetooth, on => wg.LiveBluetooth = on);
        Live("Caps Lock and Num Lock", "Shows On / Off when you press them.", wg.LiveKeys, on => wg.LiveKeys = on);
        Live("Microphone and camera dot", "Orange dot while an app uses the microphone, green while one uses the camera.", wg.LivePrivacy, on => wg.LivePrivacy = on);

        WeatherPanel.Children.Clear();
        var city = new TextBox { Text = wg.City, Padding = new Thickness(6, 4, 6, 4), ToolTip = "Leave empty to use Windows location. Press Enter to apply." };
        StyleBox(city);
        AutomationProperties.SetName(city, "Weather city");
        void ApplyCity() { if (city.Text.Trim() == wg.City) return; wg.City = city.Text.Trim(); _app.Config.SaveSoon(); _ = Island.RefreshWidgetsAsync(); }
        city.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyCity(); };
        city.LostFocus += (_, _) => ApplyCity();
        Field(WeatherPanel, "City", city);
        Field(WeatherPanel, "Units", Segments([("°C", "c"), ("°F", "f")], wg.Fahrenheit ? "f" : "c",
               v => Home(() => wg.Fahrenheit = v == "f")));
        var note = Label("From Open-Meteo (free, no account). Only the city, or your location rounded to about 10 km, leaves this PC.", 11.5, "IslandMuted");
        note.TextWrapping = TextWrapping.Wrap;
        Row(WeatherPanel, note);
    }

    private static void StyleBox(Control box)
    {
        box.SetResourceReference(BackgroundProperty, "SettingsBackground");
        box.SetResourceReference(ForegroundProperty, "IslandText");
        box.SetResourceReference(BorderBrushProperty, "SettingsLine");
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

    /// <summary>Settings row: label on the left, control on the right (or underneath for wide controls).</summary>
    private static void Field(Panel host, string label, FrameworkElement control, bool below = false)
    {
        var name = Label(label, 13, "IslandText", bold: true);
        if (below)
        {
            name.Margin = new Thickness(0, 0, 0, 10);
            Row(host, new StackPanel { Children = { name, control } });
            return;
        }
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        if (control is TextBox or PasswordBox) control.Width = 260;
        DockPanel.SetDock(control, Dock.Right);
        Row(host, new DockPanel { LastChildFill = true, Children = { control, name } });
    }

    /// <summary>Adds a row with even padding and a hairline above it (except the first in a card).</summary>
    private static void Row(Panel host, FrameworkElement row)
    {
        var line = new Border { Padding = new Thickness(0, 12, 0, 12), Child = row, BorderThickness = new Thickness(0, host.Children.Count == 0 ? 0 : 1, 0, 0) };
        line.SetResourceReference(Border.BorderBrushProperty, "SettingsLine");
        host.Children.Add(line);
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
        Row(host, toggle);
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

    /// <summary>One segment per connected display ("1 · primary", "2"…); full names in the tooltip.</summary>
    private FrameworkElement MonitorBox()
    {
        var monitors = App.MonitorChoices();
        string current = monitors.Any(m => m.Value == C.Window.MonitorId) ? C.Window.MonitorId
                       : monitors.FirstOrDefault(m => m.Label.Contains("(primary)")).Value ?? "";
        var options = monitors.Select((m, i) => ($"Display {i + 1}" + (m.Label.Contains("(primary)") ? " · primary" : ""), m.Value)).ToArray();
        var segments = Segments(options, current, v => { C.Window.MonitorId = v; Changed(); });
        segments.ToolTip = string.Join(Environment.NewLine, monitors.Select(m => m.Label));
        return segments;
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
        foreach (var (name, hex) in Accents.Append(("Windows accent (follows your wallpaper)", "auto")))
        {
            var dot = new Ellipse { Width = 24, Height = 24, Fill = (Brush)new BrushConverter().ConvertFromString(hex == "auto" ? App.WindowsAccent() : hex)! };
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
        foreach (var key in (string[])[.. AppConfig.Mascots, AppConfig.NoMascot])
        {
            object face = key == AppConfig.NoMascot ? Label("Off", 12, "IslandMuted", bold: true)
                        : new Image { Width = 38, Height = 38, Source = MascotControl.Art(key, "idle") };
            if (face is TextBlock t) { t.Width = 38; t.Height = 38; t.TextAlignment = TextAlignment.Center; t.Padding = new Thickness(0, 11, 0, 0); }
            var rb = new RadioButton
            {
                Content = face,
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
