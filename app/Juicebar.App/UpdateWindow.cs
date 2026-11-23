using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Juicebar.Core;

namespace Juicebar.App;

// "Update available": the release notes (what's new) and the choices: update in one click,
// open the release page, remind me tomorrow or skip this version.
public sealed class UpdateWindow : FlyoutWindow
{
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 6, IsVisible = false };
    readonly TextBlock status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    readonly WrapPanel buttons = new() { Orientation = Orientation.Horizontal };
    readonly Ink ink;
    readonly bool light;

    public event Action? InstallRequested, LaterRequested, SkipRequested;

    public UpdateWindow(Updater.Release release, bool lightTheme, bool canInstall)
    {
        light = lightTheme;
        ink = Ink.For(light);
        Title = Loc.T("upd_title");
        RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
        status.Foreground = Draw.B(ink.Sub);

        var close = new IconButton(IconButton.Shape.Close) { Color = ink.Sub, Light = light, Width = 28, Height = 28 };
        close.Click += Hide;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = Loc.T("upd_title"), FontSize = 17.3, FontWeight = FontWeight.SemiBold, Foreground = Draw.B(ink.Text), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        string notes = CleanNotes(release.Notes);
        var notesBox = new Border
        {
            Background = Draw.B(ink.CardBg), BorderBrush = Draw.B(ink.CardLine), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10),
            Child = new ScrollViewer
            {
                MaxHeight = 240,
                Content = new SelectableTextBlock { Text = notes.Length > 0 ? notes : Loc.T("upd_no_notes"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Draw.B(ink.Text) },
            },
        };

        var install = Button(Loc.T("upd_install"), () => InstallRequested?.Invoke(), accent: true);
        install.IsEnabled = canInstall;
        buttons.Children.Add(install);
        buttons.Children.Add(Button(Loc.T("upd_page"), () => Shell.Open(release.Url)));
        buttons.Children.Add(Button(Loc.T("upd_later"), () => { LaterRequested?.Invoke(); Hide(); }));
        buttons.Children.Add(Button(Loc.T("upd_skip"), () => { SkipRequested?.Invoke(); Hide(); }));

        status.Text = canInstall ? Loc.T("upd_safe_note") : Loc.T("upd_manual");
        SetPanel(new Border
        {
            Width = 540,
            Background = Draw.B(ink.Bg), BorderBrush = Draw.B(ink.Border), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 14, 20, 18),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    header,
                    new TextBlock { Text = Loc.F("upd_version", release.Tag.TrimStart('v', 'V'), Updater.CurrentText), FontSize = 13.3, Foreground = Draw.B(ink.Text), TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = Loc.T("upd_whats_new"), FontSize = 13.3, FontWeight = FontWeight.SemiBold, Foreground = Draw.B(ink.Text), Margin = new Thickness(0, 4, 0, 0) },
                    notesBox,
                    bar,
                    status,
                    buttons,
                },
            },
        });
    }

    Button Button(string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        if (accent) b.Classes.Add("accent");
        b.Click += (s, e) => click();
        return b;
    }

    public void ShowCentered()
    {
        PlaceCenter();
        Show();
        Activate();
        PlaceCenter();
    }

    public void SetProgress(string text, double? fraction)
    {
        bar.IsVisible = true;
        bar.IsIndeterminate = fraction == null;
        if (fraction is double f) bar.Value = f * 100;
        status.Text = text;
        status.Foreground = Draw.B(ink.Sub);
        buttons.IsEnabled = false;
    }

    public void SetError(string text)
    {
        bar.IsVisible = false;
        status.Text = text;
        status.Foreground = Draw.B(Palette.Low(light));
        buttons.IsEnabled = true;
    }

    // GitHub release notes are Markdown: keep the text readable without rendering it.
    static string CleanNotes(string md)
    {
        var sb = new StringBuilder();
        foreach (string raw in md.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            line = Regex.Replace(line, @"^\s{0,3}#{1,6}\s*", "");                 // headings
            line = Regex.Replace(line, @"^\s*[-*+]\s+", "• ");                     // bullets
            line = Regex.Replace(line, @"\[([^\]]+)\]\((https?://[^)]+)\)", "$1");  // links -> text
            line = line.Replace("**", "").Replace("__", "").Replace("`", "");
            sb.AppendLine(line);
        }
        string text = Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim();
        return text.Length > 6000 ? text[..6000] + "..." : text;
    }
}
