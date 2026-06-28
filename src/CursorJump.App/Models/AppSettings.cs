using System;
using System.Collections.Generic;

namespace CursorJump.App.Models;

[Flags]
public enum TriggerType
{
    None            = 0,
    Mouse           = 1,
    Keyboard        = 2,
    /// <summary>修飾キーの連打ジェスチャ（v1.9.0+）。Pro 機能 3 種専用。<see cref="ModifierGesture"/> で具体的なジェスチャを指定する。</summary>
    ModifierSequence = 4,
}

/// <summary>
/// 修飾キーの連打ジェスチャのプリセット（v1.9.0+）。観測専用でキーは消費しない。
/// 順次タップ（前のキーを離してから次を押す）で成立し、同時押し（チャード）では成立しない。
/// </summary>
public enum ModifierGesture
{
    None,
    /// <summary>Ctrl を素早く 2 回タップ。</summary>
    CtrlDoubleTap,
    /// <summary>Shift を素早く 2 回タップ。</summary>
    ShiftDoubleTap,
    /// <summary>Alt を素早く 2 回タップ。単独タップで一部アプリのメニューバーが一瞬反応する点に注意。</summary>
    AltDoubleTap,
}

public sealed class ActionShortcut
{
    /// <summary>有効なトリガーの組み合わせ。Mouse | Keyboard | ModifierSequence のように複数指定可能。</summary>
    public TriggerType EnabledTriggers { get; set; } = TriggerType.Mouse;
    public ModifierKeyFlags Modifiers { get; set; } = ModifierKeyFlags.Control | ModifierKeyFlags.Windows;
    public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
    /// <summary>キーボードトリガーの仮想キーコード（例: VK_F13=0x7C）。Keyboard フラグが有効な時に使用。</summary>
    public int VirtualKeyCode { get; set; } = 0;
    /// <summary>修飾キー連打ジェスチャ（v1.9.0+）。ModifierSequence フラグが有効な時に使用。</summary>
    public ModifierGesture ModifierGesture { get; set; } = ModifierGesture.None;
}

public sealed class AppSettings
{
    public ActionShortcut SaveShortcut { get; set; } = new()
    {
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Left
    };

    public ActionShortcut NavigateShortcut { get; set; } = new()
    {
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Right
    };

    public ActionShortcut DisplayDeleteShortcut { get; set; } = new()
    {
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Middle
    };

    public ActionShortcut NavigateCurrentMonitorShortcut { get; set; } = new()
    {
        EnabledTriggers = TriggerType.None,
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows | ModifierKeyFlags.Shift,
        MouseButton = MouseButtonType.Right,
        VirtualKeyCode = 0
    };

    /// <summary>第2座標セット（Set B）の座標保存ショートカット。デフォルト Win+Shift+左。
    /// 旧デフォルト Win+Alt+左 は Xbox Game Bar（Win+Alt プレフィックス）との干渉で
    /// GetAsyncKeyState(VK_LMENU) が 0 を返す問題があるため Win+Shift に変更。</summary>
    public ActionShortcut SaveShortcutB { get; set; } = new()
    {
        EnabledTriggers = TriggerType.Mouse,
        Modifiers = ModifierKeyFlags.Shift | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Left,
        VirtualKeyCode = 0
    };

    /// <summary>第2座標セット（Set B）の座標移動ショートカット。デフォルト Win+Shift+右。</summary>
    public ActionShortcut NavigateShortcutB { get; set; } = new()
    {
        EnabledTriggers = TriggerType.Mouse,
        Modifiers = ModifierKeyFlags.Shift | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Right,
        VirtualKeyCode = 0
    };

    // ── Pro 追加機能（v1.9.0+） 3 種。いずれも既定無効（利用者が各機能ごとに ON）。 ──

    /// <summary>ジャンプ循環リセット（Set A/B 両方の循環インデックスを先頭前へ戻す）。Pro 限定。既定無効。
    /// 既定プリセット = Ctrl ダブルタップ（ON にすると即使える想定値）。</summary>
    public ActionShortcut ResetCycleShortcut { get; set; } = new()
    {
        EnabledTriggers = TriggerType.None,
        Modifiers = ModifierKeyFlags.Control | ModifierKeyFlags.Windows | ModifierKeyFlags.Shift,
        MouseButton = MouseButtonType.Middle,
        VirtualKeyCode = 0,
        ModifierGesture = ModifierGesture.CtrlDoubleTap
    };

    /// <summary>フォアグラウンドウィンドウ中央へジャンプ。Pro 限定。既定無効。
    /// 既定プリセット = Shift ダブルタップ。</summary>
    public ActionShortcut ActiveWindowJumpShortcut { get; set; } = new()
    {
        EnabledTriggers = TriggerType.None,
        Modifiers = ModifierKeyFlags.Alt | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Middle,
        VirtualKeyCode = 0,
        ModifierGesture = ModifierGesture.ShiftDoubleTap
    };

    /// <summary>通常左クリック履歴を 1 つ戻る（Cursor の Ctrl+Z 風）。Pro 限定。既定無効。
    /// 既定プリセット = Alt ダブルタップ。</summary>
    public ActionShortcut ClickHistoryBackShortcut { get; set; } = new()
    {
        EnabledTriggers = TriggerType.None,
        Modifiers = ModifierKeyFlags.Alt | ModifierKeyFlags.Windows,
        MouseButton = MouseButtonType.Left,
        VirtualKeyCode = 0,
        ModifierGesture = ModifierGesture.AltDoubleTap
    };

    /// <summary>左クリック履歴の巡回に含める最近クリック数（1..10）。戻るショートカットを押すたびに最近 N 件を循環する。
    /// 既定 2＝最近 2 点を行き来。循環式なので 1 は実質意味がなく（その場でスキップされ動かない）、最小実用値は 2。</summary>
    public int ClickHistoryDepth { get; set; } = 2;

    // 初期色は Set A=青 / Set B=ピンク で 3 効果（保存円・軌跡・マーカー）を統一する。
    // デフォルト変更は新規インストール（settings.json 不在）時のみ反映。既存環境は保存値を維持する。
    public string SaveCircleColor { get; set; } = "#5BA8F0";
    public string SaveCircleColorB { get; set; } = "#FF7FA8";
    public string TrailColor { get; set; } = "#5BA8F0";
    public string TrailColorB { get; set; } = "#FF7FA8";
    public string MarkerColor { get; set; } = "#5BA8F0";
    public string MarkerColorB { get; set; } = "#FF7FA8";

    /// <summary>座標保存時の収縮円エフェクトを表示するか。false なら視覚効果のみスキップ（保存自体は動作）。</summary>
    public bool SaveEffectEnabled { get; set; } = true;
    /// <summary>ナビゲーション時の軌跡ラインを表示するか。false ならカーソル移動は通常通り。</summary>
    public bool TrailEffectEnabled { get; set; } = true;
    /// <summary>座標表示モード時の保存座標マーカーを描画するか。false なら表示モード中でも視覚マーカーなし。</summary>
    public bool MarkerEffectEnabled { get; set; } = true;
    /// <summary>座標表示/削除モード中にヘルプパネルを表示するか。false でも全削除確認バナーは表示される。旧 settings.json では不在 → true 扱い。</summary>
    public bool ShowDeleteModeHelp { get; set; } = true;

    // ── 軌跡エフェクト詳細設定 ──
    /// <summary>軌跡ラインの太さ（dp）。デフォルト 3.0。範囲: 1.0–20.0。</summary>
    public double TrailThickness { get; set; } = 3.0;
    /// <summary>軌跡フェードアウトの総時間（ms）。デフォルト 500。範囲: 100–3000。</summary>
    public int TrailDurationMs { get; set; } = 500;
    /// <summary>軌跡のピーク不透明度。デフォルト 1.0。範囲: 0.1–1.0。</summary>
    public double TrailOpacity { get; set; } = 1.0;

    // ── 永続化された座標 ──
    /// <summary>Set A の保存座標。アプリ終了後も保持。</summary>
    public List<SavedCoordinate> SavedCoordinatesA { get; set; } = new();
    /// <summary>Set B の保存座標。アプリ終了後も保持。</summary>
    public List<SavedCoordinate> SavedCoordinatesB { get; set; } = new();

    /// <summary>UI テーマ（Light / Dark）。デフォルトは Dark。</summary>
    public UiTheme UiTheme { get; set; } = UiTheme.Dark;

    /// <summary>UI 言語。Auto は OS の UI 言語から自動判定する。</summary>
    public UiLanguage UiLanguage { get; set; } = UiLanguage.Auto;

    // ── 自動更新設定 ──
    /// <summary>起動時に GitHub Releases へ更新確認を行うか。旧 settings.json では未定義 → true 扱い。</summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    /// <summary>最後に更新確認を行った UTC 時刻（ISO 8601）。空文字は未確認。デバッグ・UI 表示用。</summary>
    public string LastUpdateCheckUtc { get; set; } = "";
    /// <summary>「このバージョンをスキップ」で記録された対象バージョン文字列。一致する版は通知しない。</summary>
    public string SkippedVersion { get; set; } = "";

    // ── 診断ログ ──
    /// <summary>ジャンプ時の before/after 座標などの詳細ログを debug.log に出力するか。
    /// マルチモニタでのジャンプ位置ずれ調査用。常用するとフックコールバック内 I/O が増えるため通常は false。</summary>
    public bool VerboseLogging { get; set; } = false;

    // ── ジャンプ後アクション ──
    /// <summary>座標ジャンプ後、カーソル直下にウィンドウがあればそれを前面化（フォーカス）するか。
    /// 既定 true。旧 settings.json では未定義 → C# 初期化値 true（オプトアウト）。</summary>
    public bool ActivateWindowUnderCursorOnJump { get; set; } = true;

    // ── 自動起動 ──
    /// <summary>Windows サインイン時に CursorJump を自動起動するか。
    /// レジストリ HKCU\Software\Microsoft\Windows\CurrentVersion\Run に値名 "CursorJump" で exe パスを登録する。
    /// 既定 false（オプトイン）。旧 settings.json では未定義 → false。</summary>
    public bool AutoStartEnabled { get; set; } = false;

    // ── カーソルジャンプ方式 ──
    /// <summary>カーソルジャンプの実装戦略。v1.5.1 で導入。
    /// 既定 = DpiContext (SetThreadDpiAwarenessContext + SetCursorPos)。
    /// 旧 settings.json (v1.5.1 より前) では未定義 → JumpStrategy = DpiContext (新既定) として扱われる。</summary>
    public JumpStrategy JumpStrategy { get; set; } = JumpStrategy.DpiContext;

    /// <summary>v1.5.0 で導入した非推奨フラグ。後方互換のため読み込みのみ受け付ける。
    /// 実際の経路選択は <see cref="JumpStrategy"/> が優先される。新規書き込みでは default(false) のまま放置。</summary>
    public bool UseSendInputForJump { get; set; } = false;

    // ── ライセンス ──
    /// <summary>Pro 版ライセンスキー（ユーザー入力）。空文字なら Free。SHA256 ハッシュを LicenseService 内の埋め込みハッシュと比較して Pro 化する。</summary>
    public string LicenseKey { get; set; } = "";

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.SavedCoordinatesA = new List<SavedCoordinate>(SavedCoordinatesA);
        c.SavedCoordinatesB = new List<SavedCoordinate>(SavedCoordinatesB);
        return c;
    }
}

public enum UiTheme
{
    Light,
    Dark
}

/// <summary>
/// カーソルジャンプの実装戦略。v1.5.1 で導入。
/// </summary>
public enum JumpStrategy
{
    /// <summary>SetThreadDpiAwarenessContext(PER_MONITOR_AWARE_V2) で対象モニタのコンテキストを明示してから SetCursorPos。
    /// PerMonitorV2 + マルチ DPI 環境で OS の DPI 仮想化キャッシュが期待通りの位置にカーソルを置けるよう促す。v1.5.1 既定。</summary>
    DpiContext,

    /// <summary>SendInput(MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK) で 0..65535 正規化座標経由。
    /// v1.5.0 既定。Dynabook + マルチ DPI で効かなかったが、他環境向け退避路として残す。</summary>
    SendInputVirtualDesk,

    /// <summary>素の SetCursorPos のみ。v1.4.x までの挙動。デバッグ・最終退避路用。</summary>
    LegacySetCursorPos,
}

public enum UiLanguage
{
    Auto,
    Japanese,
    English
}

public enum MouseButtonType
{
    Left,
    Right,
    Middle,
    XButton1,           // 戻るボタン（サイドボタン1）
    XButton2,           // 進むボタン（サイドボタン2）
    MiddleLeftChord,    // ホイール押下 + 左クリック
    MiddleRightChord,   // ホイール押下 + 右クリック
    MiddleDoubleClick,  // ホイール2連打
    MiddleTripleClick,  // ホイール3連打
    WheelUp,            // ホイール上スクロール (v1.5.2、UI からは廃止。settings.json 後方互換のため enum 値は維持)
    WheelDown,          // ホイール下スクロール (v1.5.2、UI からは廃止。settings.json 後方互換のため enum 値は維持)
    MouseWheel          // ホイール上下を方向中立で受ける統合トリガー (v1.6.1+)。Navigate 系のみ上=GetPrev / 下=GetNext で動作
}

[Flags]
public enum ModifierKeyFlags
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Windows = 8
}
