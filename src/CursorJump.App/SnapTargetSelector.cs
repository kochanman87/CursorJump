using System;
using System.Collections.Generic;
using System.Drawing;

namespace CursorJump.App;

/// <summary>ジャンプ先の位置補正に使う要素の種類。</summary>
internal enum SnapElementKind
{
    Other,
    RadioButton,
    CheckBox,
}

/// <summary>
/// UI Automation で見つけたクリック可能要素 1 件（座標はすべて物理ピクセル）。
/// <see cref="ClickablePoint"/> は UIA が返したクリック可能点（取れなければ null）。
/// <see cref="Window"/> は所属トップレベルウィンドウ（前面判定用、ロジックでは参照しない）。
/// <see cref="LearnKey"/> は学習データの照合キー（<see cref="SnapElementRules.BuildLearnKey"/>、無ければ空）。
/// <see cref="BaseWeight"/> は学習とは別の固定の重み（1 = 標準、大きいほど選ばれにくい）。
/// </summary>
internal readonly record struct SnapCandidate(
    Rectangle Bounds,
    SnapElementKind Kind,
    Point? ClickablePoint,
    IntPtr Window,
    string LearnKey = "",
    double BaseWeight = 1.0);

/// <summary>
/// 「現在位置 + 移動方向 + 要素一覧 → ジャンプ先候補の順位」を決める純粋関数。
/// 前面判定（他ウィンドウに隠れていないか）は呼出側が順位の上から順に行う。
/// </summary>
internal static class SnapTargetSelector
{

    /// <summary>
    /// 要素の中でカーソルを置く位置。
    /// 1. UIA のクリック可能点が要素内にあればそれ
    /// 2. ラジオ/チェックボックスでラベル込みの横長矩形なら、左端の正方形（丸/四角の部分）の中心
    ///    （WinForms はクリック可能点を返さず、矩形がラベルまで含むため中心だと文字の上に着地する）
    /// 3. それ以外は矩形の中心
    /// </summary>
    public static Point TargetPoint(SnapCandidate c)
    {
        var b = c.Bounds;
        if (c.ClickablePoint is Point cp && b.Contains(cp)) return cp;

        if ((c.Kind == SnapElementKind.RadioButton || c.Kind == SnapElementKind.CheckBox)
            && b.Width > b.Height * 1.5)
        {
            int half = b.Height / 2;
            return new Point(b.Left + half, b.Top + half);
        }

        return new Point(b.Left + b.Width / 2, b.Top + b.Height / 2);
    }

    /// <summary>
    /// <paramref name="origin"/> から (<paramref name="dx"/>, <paramref name="dy"/>) の方向にある候補を、近い順に返す。
    /// 現在カーソルが乗っている要素（矩形が origin を含む）は除外する。
    /// 距離スコアには <see cref="SnapCandidate.BaseWeight"/> と <paramref name="learnedWeight"/>（学習の重み、
    /// 1 = 標準、小さいほど近く扱い）を掛ける。
    /// </summary>
    public static List<SnapCandidate> RankInDirection(
        Point origin, double dx, double dy, IReadOnlyList<SnapCandidate> candidates,
        Func<SnapCandidate, double>? learnedWeight = null,
        double coneHalfAngleDeg = SnapTuning.ConeHalfAngleDeg)
    {
        var result = new List<SnapCandidate>();
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (candidates is null || candidates.Count == 0 || len < 1e-6) return result;

        double ux = dx / len, uy = dy / len;
        double tan = Math.Tan(coneHalfAngleDeg * Math.PI / 180.0);
        var scored = new List<(double Score, SnapCandidate C)>();

        foreach (var c in candidates)
        {
            if (c.Bounds.Contains(origin)) continue;

            var t = TargetPoint(c);
            double vx = t.X - origin.X, vy = t.Y - origin.Y;
            double along = vx * ux + vy * uy;
            if (along <= 0) continue;

            double perp = Math.Abs(vx * uy - vy * ux);
            if (perp > along * tan) continue;

            double weight = c.BaseWeight * (learnedWeight?.Invoke(c) ?? 1.0);
            scored.Add(((along + perp * SnapTuning.PerpendicularWeight) * weight, c));
        }

        scored.Sort((a, b) => a.Score.CompareTo(b.Score));
        foreach (var s in scored) result.Add(s.C);
        return result;
    }
}
