using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CursorJump.App;

/// <summary>
/// GitHub Releases から自動更新を行うサービス。Velopack でインストールされていない
/// 開発実行（dotnet run / 直接 bin\Debug 起動）では <see cref="IsInstalled"/> が false になり、
/// チェックは常に null を返す（例外を呼び出し側に伝播させない設計）。
/// </summary>
public sealed class UpdateService
{
    private const string GitHubRepoUrl = "https://github.com/kochanman87/CursorJump";

    private readonly SettingsService _settingsService;
    private readonly UpdateManager _manager;

    public UpdateService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
        _manager = new UpdateManager(source);
    }

    /// <summary>Velopack インストーラ経由で配置されているかどうか。開発実行時は false。</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>現在のインストール済みバージョン（インストールされていない場合は null）。</summary>
    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <summary>
    /// 新版の有無を確認する。新版があれば <see cref="UpdateInfo"/>、無ければ null。
    /// 例外（オフライン・GitHub 障害・未インストール）は飲み込んで null を返し、
    /// <see cref="DebugLog"/> に記録する。常に <see cref="AppSettings.LastUpdateCheckUtc"/> を更新する。
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            if (!_manager.IsInstalled)
            {
                DebugLog.Write("UpdateService.CheckForUpdatesAsync: not installed (dev run) — skipping");
                return null;
            }

            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            RecordCheckTime();
            if (info == null)
            {
                DebugLog.Write("UpdateService.CheckForUpdatesAsync: up-to-date");
            }
            else
            {
                DebugLog.Write($"UpdateService.CheckForUpdatesAsync: new version {info.TargetFullRelease.Version}");
            }
            return info;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UpdateService.CheckForUpdatesAsync failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 指定された <see cref="UpdateInfo"/> をダウンロードし、適用してアプリを再起動する。
    /// 成功時はメソッドから戻らない（プロセスが置き換わる）。失敗時は false を返す。
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(UpdateInfo info, Action<int>? progress = null)
    {
        try
        {
            if (!_manager.IsInstalled)
            {
                DebugLog.Write("UpdateService.DownloadAndApplyAsync: not installed — abort");
                return false;
            }

            await _manager.DownloadUpdatesAsync(info, progress).ConfigureAwait(false);
            DebugLog.Write($"UpdateService.DownloadAndApplyAsync: downloaded {info.TargetFullRelease.Version}, restarting…");
            _manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"UpdateService.DownloadAndApplyAsync failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>「このバージョンをスキップ」を記録。同バージョンでは起動時通知を抑制する。</summary>
    public void SkipVersion(string version)
    {
        var settings = _settingsService.Current.Clone();
        settings.SkippedVersion = version;
        _settingsService.Save(settings);
    }

    /// <summary>指定バージョンが「スキップ済み」と一致するか。</summary>
    public bool IsSkipped(string version)
    {
        return !string.IsNullOrEmpty(version) &&
               string.Equals(_settingsService.Current.SkippedVersion, version, StringComparison.Ordinal);
    }

    private void RecordCheckTime()
    {
        var settings = _settingsService.Current.Clone();
        settings.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
        _settingsService.Save(settings);
    }
}
