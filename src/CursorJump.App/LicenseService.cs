using System;
using System.Security.Cryptography;
using System.Text;

namespace CursorJump.App;

public enum LicenseStatus
{
    NotEntered,
    Valid,
    Invalid
}

/// <summary>
/// Pro 版ライセンスキーの検証と状態管理。
/// 平文キーはソースに置かず、SHA256 ハッシュのみを埋め込む（OSS 公開リポでも安全）。
/// 検証は SHA256(UTF8(input)) → 小文字 hex を ProKeyHash と比較するのみ。
/// </summary>
public sealed class LicenseService
{
    /// <summary>"<REDACTED-OLD-KEY>" の SHA256（小文字 hex）。</summary>
    private const string ProKeyHash =
        "69d6a90f54687b014099998699092c1ec9a8c746d31d83bab5eddb9c2be7be26";

    /// <summary>Free 版の保存座標上限（Set A 専用、Set B は Free では機能無効）。</summary>
    public const int FreeMaxCoordinates = 3;

    private readonly SettingsService _settingsService;

    public LicenseService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Refresh();
    }

    public LicenseStatus Status { get; private set; } = LicenseStatus.NotEntered;

    public bool IsPro => Status == LicenseStatus.Valid;

    /// <summary>ライセンス状態が変化した（NotEntered/Valid/Invalid 遷移）ときに発火。</summary>
    public event Action? StatusChanged;

    /// <summary>設定の現在値からステータスを再計算する。Apply 経由ではなく外部から状態を変えた場合に呼ぶ。</summary>
    public void Refresh()
    {
        Status = Evaluate(_settingsService.Current.LicenseKey);
    }

    /// <summary>
    /// ユーザー入力キーを検証して結果を返す。Valid なら settings.json に保存し、StatusChanged を発火。
    /// Invalid/NotEntered の場合は保存しない（誤入力で既存ライセンスを潰さないため）。
    /// </summary>
    public LicenseStatus Apply(string key)
    {
        var newStatus = Evaluate(key);

        if (newStatus == LicenseStatus.Valid)
        {
            var snap = _settingsService.Current.Clone();
            snap.LicenseKey = key;
            if (!_settingsService.Save(snap))
            {
                DebugLog.Write("LicenseService.Apply: failed to persist LicenseKey to settings.json");
            }
        }

        if (newStatus != Status)
        {
            Status = newStatus;
            StatusChanged?.Invoke();
        }
        else
        {
            // 状態は同じでも呼出側が表示更新したい場合があるので明示反映
            Status = newStatus;
        }
        return newStatus;
    }

    /// <summary>ライセンスキーをクリアして Free 状態に戻す（テスト/サポート用、現状 UI からは未使用）。</summary>
    public void Clear()
    {
        var snap = _settingsService.Current.Clone();
        snap.LicenseKey = "";
        _settingsService.Save(snap);
        if (Status != LicenseStatus.NotEntered)
        {
            Status = LicenseStatus.NotEntered;
            StatusChanged?.Invoke();
        }
    }

    private static LicenseStatus Evaluate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return LicenseStatus.NotEntered;
        return ComputeHash(key.Trim()) == ProKeyHash ? LicenseStatus.Valid : LicenseStatus.Invalid;
    }

    private static string ComputeHash(string input)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
