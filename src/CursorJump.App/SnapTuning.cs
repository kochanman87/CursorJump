namespace CursorJump.App;

/// <summary>
/// 要素スナップ機能（修飾キー + 移動でクリック可能要素へジャンプ）の調整値を 1 か所に集める。
/// 仮置きの値も含むため、値を変えたら日付と理由をコメントに残すこと。
/// </summary>
internal static class SnapTuning
{
    /// <summary>
    /// 修飾キー押下中にこれだけ（物理px）動かしたら 1 回ジャンプする。
    /// 2026-10-13 試作: 40px で「軽く動かすだけで飛ぶ」体感が得られたため仮採用。
    /// </summary>
    public const int MoveThresholdPx = 40;

    /// <summary>移動方向から何度までずれた要素を候補にするか（片側）。2026-10-13 試作で仮採用。</summary>
    public const double ConeHalfAngleDeg = 45;

    /// <summary>方向に対する横ずれの重み。大きいほど「まっすぐ先」を優先する。2026-10-13 試作で仮採用。</summary>
    public const double PerpendicularWeight = 2.0;

    /// <summary>
    /// 拡張機能アイコン（Chromium のツールバー）の重み。1 より大きい = 遠く扱い = 選ばれにくい。
    /// 2026-10-14: 普段あまり押さない要素への吸着を減らすため仮置き。
    /// </summary>
    public const double ExtensionButtonWeight = 1.5;

    /// <summary>
    /// 学習スコアの半減期（日）。押さなくなった要素の効きを徐々に弱める。
    /// 2026-10-14 仮置き: 1 か月使わなければ効きが半分。
    /// </summary>
    public const double LearningHalfLifeDays = 30;

    /// <summary>
    /// 学習の効きの強さ。重み = 1 / (1 + Strength × log2(1 + スコア))。
    /// 2026-10-14 仮置き: 7 回押した要素が距離 0.4 倍扱い、20 回で約 0.3 倍。
    /// </summary>
    public const double LearningStrength = 0.5;

    /// <summary>学習データの最大件数。超えたらスコアの低いものから捨てる。2026-10-14 仮置き。</summary>
    public const int LearningMaxEntries = 5000;

    /// <summary>学習キーに使う要素名の最大文字数（長いリンク文などを丸める）。</summary>
    public const int MaxNameLength = 100;

    /// <summary>
    /// 1 ウィンドウの要素一覧取得の時間制限（ms）。
    /// 2026-10-14: 2000ms だと Claude アプリが一時的に超えて除外されたため 3000ms に延長。
    /// </summary>
    public const int ScanTimeoutMs = 3000;

    /// <summary>
    /// 時間制限を超えたウィンドウを次に再挑戦するまでの待ち（ms）。超えるたびに倍、上限あり。
    /// 2026-10-14: 一度の遅延で永久除外していた試作の不具合（Claude 画面でジャンプ不能）の対策。
    /// </summary>
    public const int ScanBackoffMinMs = 5000;
    public const int ScanBackoffMaxMs = 60000;
}
