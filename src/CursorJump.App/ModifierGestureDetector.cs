using System;
using System.Collections.Generic;
using CursorJump.App.Models;

namespace CursorJump.App;

/// <summary>
/// 修飾キーの「連打ジェスチャ」（Ctrl/Shift/Alt のダブルタップ）を検出する純粋ロジック（v1.9.0+）。
///
/// 設計方針:
/// - <b>観測専用</b>: このクラスはキーイベントを消費しない。呼び出し側（KeyboardHookService）は
///   Feed の戻り値に関わらずキーを必ず素通しする。よって通常の修飾キー操作は一切壊さない。
/// - <b>順次タップ判定</b>: 各修飾キーが「前のキーを離してから次を押す」清浄なタップ（down→up で、
///   保持時間が短く、間に他キーが挟まらない）として成立した場合のみシーケンスを伸ばす。
///   Ctrl+Shift+キー のような同時押し（チャード）や、修飾キー＋文字キーの通常ショートカットでは
///   タップ列が崩れるため誤発火しない。
/// - <b>発火タイミング</b>: 2 回目のタップの UP（同一キーのダブルタップ成立）で 1 度だけ発火し、バッファをクリアする。
///
/// LL キーボードフックのコールバックは単一スレッドで呼ばれるため、内部状態はロックなしで扱う。
/// </summary>
internal sealed class ModifierGestureDetector
{
    /// <summary>タップとみなす最大保持時間（ms）。これより長い押下はタップでなく「ホールド」扱い。</summary>
    private const long TapMaxHoldMs = 300;
    /// <summary>連続タップ間に許容する最大間隔（ms）。これを超えるとシーケンスが切れる。</summary>
    private const long TapGapMaxMs = 400;
    /// <summary>追跡する最大タップ数（ダブルタップ判定のみなので 2）。</summary>
    private const int MaxTrackedTaps = 2;

    private enum ModKind { Ctrl, Shift, Alt, Win }

    // 現在物理的に押下中の修飾キー vk 集合（auto-repeat と同時押し検出に使用）。
    private readonly HashSet<int> _downModVks = new();
    // 直近に成立した清浄タップの種別列（末尾が最新）。
    private readonly List<ModKind> _taps = new(MaxTrackedTaps + 1);
    private long _lastTapTime;

    // 進行中タップの追跡。
    private ModKind _pendingKind;
    private long _pendingDownTime;
    private bool _pendingValid;

    /// <summary>
    /// キーイベントを 1 件投入する。完了したジェスチャがあればそれを返す（なければ null）。
    /// 修飾キー・非修飾キーの down/up すべてを投入すること（非修飾キーでシーケンスがリセットされる）。
    /// </summary>
    public ModifierGesture? Feed(int vkCode, bool isDown, long nowMs)
    {
        bool isMod = TryClassify(vkCode, out ModKind kind);

        if (isDown)
        {
            if (!isMod)
            {
                // 非修飾キーが押された → ジェスチャ進行を破棄（通常のショートカット/文字入力と区別）。
                _taps.Clear();
                _pendingValid = false;
                return null;
            }

            // 同一 vk の再 DOWN は auto-repeat。状態を変えない。
            if (_downModVks.Contains(vkCode))
                return null;

            // 既に別の修飾キーが押下中 → 同時押し（チャード）。タップ列を崩す。
            if (_downModVks.Count > 0)
            {
                _pendingValid = false;
                _taps.Clear();
            }

            _downModVks.Add(vkCode);

            if (_downModVks.Count == 1)
            {
                // 単独押下開始 = 清浄タップ候補。
                _pendingKind = kind;
                _pendingDownTime = nowMs;
                _pendingValid = true;
            }
            else
            {
                _pendingValid = false;
            }
            return null;
        }

        // UP
        if (!isMod)
            return null;

        _downModVks.Remove(vkCode);

        // 進行中の清浄タップでなければシーケンスは伸びない。
        if (!_pendingValid || _pendingKind != kind || _downModVks.Count != 0)
        {
            if (_downModVks.Count == 0)
                _pendingValid = false;
            return null;
        }

        _pendingValid = false;

        // 保持時間が長すぎる（ホールド）ならタップとして認めない。
        if (nowMs - _pendingDownTime > TapMaxHoldMs)
            return null;

        // 直前タップから時間が空きすぎていればシーケンスをリセット。
        if (_taps.Count > 0 && nowMs - _lastTapTime > TapGapMaxMs)
            _taps.Clear();

        _taps.Add(kind);
        _lastTapTime = nowMs;
        if (_taps.Count > MaxTrackedTaps)
            _taps.RemoveAt(0);

        var gesture = MatchTail();
        if (gesture is not null)
        {
            _taps.Clear();
            return gesture;
        }
        return null;
    }

    /// <summary>外部状態（削除モード遷移・サスペンド等）が変わったとき、進行中の検出をリセットする。</summary>
    public void Reset()
    {
        _downModVks.Clear();
        _taps.Clear();
        _pendingValid = false;
    }

    private ModifierGesture? MatchTail()
    {
        int n = _taps.Count;
        if (n < 2) return null;
        ModKind x = _taps[n - 2], y = _taps[n - 1];
        if (x != y) return null;
        return x switch
        {
            ModKind.Ctrl => ModifierGesture.CtrlDoubleTap,
            ModKind.Shift => ModifierGesture.ShiftDoubleTap,
            ModKind.Alt => ModifierGesture.AltDoubleTap,
            _ => null,
        };
    }

    private static bool TryClassify(int vk, out ModKind kind)
    {
        switch (vk)
        {
            case 0xA2: // VK_LCONTROL
            case 0xA3: // VK_RCONTROL
            case 0x11: // VK_CONTROL
                kind = ModKind.Ctrl; return true;
            case 0xA0: // VK_LSHIFT
            case 0xA1: // VK_RSHIFT
            case 0x10: // VK_SHIFT
                kind = ModKind.Shift; return true;
            case 0xA4: // VK_LMENU
            case 0xA5: // VK_RMENU
            case 0x12: // VK_MENU
                kind = ModKind.Alt; return true;
            case 0x5B: // VK_LWIN
            case 0x5C: // VK_RWIN
                kind = ModKind.Win; return true;
            default:
                kind = default; return false;
        }
    }
}
