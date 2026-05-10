using System.Windows;

namespace CursorJump.App;

public enum UpgradeReason
{
    /// <summary>Set B 機能の利用要求。</summary>
    SetB,
    /// <summary>Free 版の保存上限に到達。</summary>
    SaveLimit,
}

public partial class UpgradeDialog : Window
{
    /// <summary>OnOpenLicenseTabClick が押された場合に true。呼び出し側がライセンスタブを開く判断に使う。</summary>
    public bool OpenLicenseTabRequested { get; private set; }

    public UpgradeDialog(UpgradeReason reason)
    {
        InitializeComponent();
        BodyText.Text = reason switch
        {
            UpgradeReason.SetB =>
                Loc.Get("Str.Upgrade.SetBBody"),
            UpgradeReason.SaveLimit =>
                string.Format(Loc.Get("Str.Upgrade.SaveLimitBodyFormat"), LicenseService.FreeMaxCoordinates),
            _ => Loc.Get("Str.Upgrade.SetBBody"),
        };
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        OpenLicenseTabRequested = false;
        DialogResult = false;
        Close();
    }

    private void OnOpenLicenseTabClick(object sender, RoutedEventArgs e)
    {
        OpenLicenseTabRequested = true;
        DialogResult = true;
        Close();
    }
}
