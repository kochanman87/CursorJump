using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CursorJump.App.Controls;

/// <summary>
/// [−] [値] [+] の数の欄（v1.12.1、スライダーの置き換え。2026-11-27 ユーザー指示）。
/// 値は設定値の単位で持ち、表示は <see cref="Scale"/> 倍・<see cref="Decimals"/> 桁で出す
/// （例: 線が消えるまでの時間は値=ms、表示=秒で Scale=0.001）。範囲外の値は範囲内に収める。
/// </summary>
public partial class StepField : UserControl
{
    private double _value;

    /// <summary>利用者の操作で値が変わった（<see cref="Value"/> を直接設定した時は出さない）。</summary>
    public event EventHandler? ValueChanged;

    public StepField()
    {
        InitializeComponent();
        Refresh();
    }

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    /// <summary>± 1 回分の増減（値の単位）。</summary>
    public double Step { get; set; } = 1;
    /// <summary>表示 = 値 × Scale。</summary>
    public double Scale { get; set; } = 1;
    /// <summary>表示の小数桁数。</summary>
    public int Decimals { get; set; }

    /// <summary>値の後ろに出す単位の文字列リソースキー（言語切替に追従）。空なら出さない。</summary>
    public string UnitKey
    {
        set
        {
            if (string.IsNullOrEmpty(value)) UnitText.Text = "";
            else UnitText.SetResourceReference(TextBlock.TextProperty, value);
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Normalize(value);
            Refresh();
        }
    }

    /// <summary>範囲内に収めるだけ（刻みには揃えない。保存済みの半端な値を読み込みだけで書き換えないため）。</summary>
    private double Normalize(double v)
    {
        if (!double.IsFinite(v)) v = Minimum;
        return Math.Round(Math.Clamp(v, Minimum, Maximum), 6);
    }

    private void Refresh()
    {
        Box.Text = (_value * Scale).ToString("F" + Decimals, CultureInfo.CurrentCulture);
        BtnMinus.IsEnabled = _value > Minimum;
        BtnPlus.IsEnabled = _value < Maximum;
    }

    private void SetByUser(double v)
    {
        double next = Normalize(v);
        bool changed = next != _value;
        _value = next;
        Refresh();
        if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMinusClick(object sender, RoutedEventArgs e) => SetByUser(_value - Step);
    private void OnPlusClick(object sender, RoutedEventArgs e) => SetByUser(_value + Step);

    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        else if (e.Key == Key.Up) { SetByUser(_value + Step); e.Handled = true; }
        else if (e.Key == Key.Down) { SetByUser(_value - Step); e.Handled = true; }
        else if (e.Key == Key.Escape) { Refresh(); } // 入力途中を捨てる（Handled にしないので窓の Esc も効く）
    }

    private void OnBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void Commit()
    {
        var text = Box.Text.Trim().TrimEnd('%');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var shown) && Scale != 0)
            SetByUser(shown / Scale);
        else
            Refresh(); // 読めない入力は元に戻す
    }
}
