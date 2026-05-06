using System;
using System.Globalization;
using System.Windows;
using Velopack;

namespace CursorJump.App;

public partial class UpdateDialog : Window
{
    private readonly UpdateService _updateService;
    private readonly UpdateInfo _info;
    private readonly string _newVersion;

    public UpdateDialog(UpdateService updateService, UpdateInfo info)
    {
        InitializeComponent();
        _updateService = updateService;
        _info = info;
        _newVersion = info.TargetFullRelease.Version.ToString();

        string current = updateService.CurrentVersion ?? "-";
        CurrentVersionText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.Update.CurrentVersion"), current);
        NewVersionText.Text = string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.Update.NewVersion"), _newVersion);

        string notes = info.TargetFullRelease.NotesMarkdown;
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(notes) ? "-" : notes;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        SetButtonsEnabled(false);
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = "0%";

        var progress = new Progress<int>(p =>
        {
            DownloadProgress.Value = p;
            ProgressText.Text = $"{p}%";
        });

        bool ok = await _updateService.DownloadAndApplyAsync(_info, p => ((IProgress<int>)progress).Report(p));
        // 成功時は ApplyUpdatesAndRestart 内でプロセス終了するためここに到達しない。
        if (!ok)
        {
            MessageBox.Show(
                string.Format(CultureInfo.CurrentCulture, Loc.Get("Str.Update.CheckFailed"), "download/apply"),
                Loc.Get("Str.AppName"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetButtonsEnabled(true);
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        _updateService.SkipVersion(_newVersion);
        DialogResult = false;
        Close();
    }

    private void SetButtonsEnabled(bool enabled)
    {
        UpdateButton.IsEnabled = enabled;
        LaterButton.IsEnabled = enabled;
        SkipButton.IsEnabled = enabled;
    }
}
