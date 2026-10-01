using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Mail;
using KawaiiIsland.Services.Notifications;

namespace KawaiiIsland;

/// <summary>
/// Mail module (spec §3.1): unread badge in the compact pill, latest 5 headers in the expanded Mail view,
/// clicking opens webmail or the default mail app. Headers live in memory only; the password is DPAPI-encrypted.
/// </summary>
public partial class IslandWindow
{
    private static readonly Color MailTint = Color.FromRgb(0x4D, 0xA3, 0xFF);

    private readonly MailInbox _inbox = new();
    private IMailProvider? _mail;

    private MailConfig MailCfg => _config.Current.Modules.Mail;
    private int Unread => _mail is null ? 0 : _inbox.Current.Unread;

    /// <summary>Why mail can't be read (bad password, no server…), or null.</summary>
    public string? MailProblem { get; private set; }

    private void InitMail()
    {
        TabMail.Click += (_, _) => Pick(View.Mail);
        OpenMailButton.Click += (_, _) => OpenMail();
        if (MailCfg.Enabled) _ = StartMailAsync();
    }

    /// <summary>(Re)starts mail with the current settings; used by Settings after edits.</summary>
    public async Task RestartMailAsync()
    {
        StopMail();
        MailProblem = null;
        if (MailCfg.Enabled) await StartMailAsync();
        RenderMail();
    }

    private async Task StartMailAsync()
    {
        IMailProvider provider;
        if (MailCfg.Provider == "imap")
        {
            if (MailCfg.Server.Length == 0 || MailCfg.Username.Length == 0 || MailSecret.Load(_config.Directory) is not { } password)
            {
                MailProblem = "Add your server, username and password in Settings → Mail.";
                return;
            }
            provider = new ImapMailProvider(MailCfg, password);
        }
        else provider = new MockMailProvider(_config.Directory);

        _mail = provider;
        provider.Changed += snapshot => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_mail, provider)) OnMail(snapshot); });
        string? problem = await provider.StartAsync();
        if (!ReferenceEquals(_mail, provider)) { provider.Dispose(); return; } // restarted while connecting
        MailProblem = problem;
        if (problem is not null) StopMail();
        ((App)Application.Current).RefreshSettings();
    }

    private void StopMail()
    {
        _mail?.Dispose();
        _mail = null;
        RenderMail();
    }

    private void OnMail(MailSnapshot snapshot)
    {
        // Quiet by default: new mail updates the badge and the mascot hops; it never pops the island open.
        if (_inbox.Update(snapshot).Count > 0 && !CodeMode)
            foreach (var m in Mascots) m.Hop();
        RenderMail();
    }

    private void RenderMail()
    {
        var snap = _mail is null ? MailSnapshot.Empty : _inbox.Current;
        MailBadgeCount.Text = snap.Unread > 99 ? "99+" : snap.Unread.ToString();
        MailCount.Text = snap.Unread switch { 0 => "No unread mail", 1 => "1 unread", var n => $"{n} unread" };
        MailList.Children.Clear();
        foreach (var m in snap.Latest.Take(5)) MailList.Children.Add(MailRow(m));
        RenderCompact();
        RenderExpanded();
    }

    /// <summary>"Sender  subject …  5m": the subject only shows when message previews are on (privacy default).</summary>
    private FrameworkElement MailRow(MailHeader m)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var ago = Text(NotificationFeed.Ago(DateTimeOffset.Now - m.Date), 11, "IslandMuted");
        DockPanel.SetDock(ago, Dock.Right);
        ago.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(ago);
        var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
        line.SetResourceReference(TextBlock.FontFamilyProperty, "IslandFont");
        var from = new System.Windows.Documents.Run(m.From) { FontWeight = FontWeights.SemiBold };
        from.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "IslandText");
        line.Inlines.Add(from);
        if (_config.Current.Modules.Notifications.ShowPreview && m.Subject.Length > 0)
        {
            var subject = new System.Windows.Documents.Run("  " + m.Subject);
            subject.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "IslandMuted");
            line.Inlines.Add(subject);
        }
        row.Children.Add(line);
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenMail(); };
        return row;
    }

    /// <summary>Configured URL, else webmail for well-known servers, else the default mail app.
    /// ponytail: opens the inbox, not the exact message; IMAP has no portable deep link.</summary>
    private void OpenMail()
    {
        string server = MailCfg.Server.ToLowerInvariant();
        string target = MailCfg.OpenUrl.Length > 0 ? MailCfg.OpenUrl
            : server.Contains("gmail") || server.Contains("google") ? "https://mail.google.com/"
            : server.Contains("outlook") || server.Contains("office365") || server.Contains("hotmail") ? "https://outlook.live.com/mail/"
            : server.Contains("yahoo") ? "https://mail.yahoo.com/"
            : server.Contains("icloud") || server.Contains("me.com") ? "https://www.icloud.com/mail"
            : "mailto:";
        if (!target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && target != "mailto:") return; // never launch arbitrary commands
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* no browser/mail app registered */ }
        SetExpanded(false);
    }
}
