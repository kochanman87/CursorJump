using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace CursorJump.App;

/// <summary>
/// 要素へジャンプ（お試し機能、v1.11.0-beta+、Pro）の裏方。
///
/// - 表示中の全トップレベルウィンドウのクリック可能要素を UI Automation で取得して保持する
///   （ジャンプ時に毎回取得すると 1 秒前後かかるため。前面ウィンドウは 0.5 秒、他は 3 秒ごとに取り直す）
/// - 普段の左クリックを観測し、クリックした要素を学習する（<see cref="SnapLearningStore"/>）
///
/// 機能 OFF・Free のときは待機するだけで、他アプリへの問い合わせを一切行わない
/// （Chrome 等は外部から問い合わせを受けるとアクセシビリティ機能を有効化するため、不要な負荷をかけない）。
/// UIA 呼び出しは応答しないアプリ（実測: Steam）で止まることがあるため、必ず別スレッド + 時間制限で行う。
/// </summary>
internal sealed class ElementSnapService : IDisposable
{
    private const int LoopMs = 250;
    private const int InactiveLoopMs = 500;
    private const int ForegroundRefreshMs = 500;
    private const int OtherRefreshMs = 3000;
    private const int LearnTimeoutMs = 1000;
    private const int SaveIntervalMs = 5000;
    /// <summary>クリック位置から何段まで親を辿ってクリック可能要素を探すか（実測では最大 1 段）。</summary>
    private const int MaxParentDepth = 6;

    private static readonly ControlType[] ClickableTypes =
    {
        ControlType.Button, ControlType.Hyperlink, ControlType.CheckBox, ControlType.RadioButton,
        ControlType.MenuItem, ControlType.TabItem, ControlType.ListItem, ControlType.ComboBox,
        ControlType.TreeItem, ControlType.SplitButton,
        // ControlType.Edit は初期版では除外（エクスプローラーで一覧の各列が大量に返るため。2026-10-13 実測）
    };
    private static readonly HashSet<int> ClickableIds = ClickableTypes.Select(t => t.Id).ToHashSet();

    private sealed class WindowEntry
    {
        public List<SnapCandidate> Items = new();
        public long ScannedAt;
        public int Failures;
        public long RetryAt;
        public string Process = "";
        public string Domain = "";
        public List<Rectangle> WebAreas = new();
    }

    private sealed record ScanResult(List<SnapCandidate> Items, string Domain, List<Rectangle> WebAreas);

    private readonly SettingsService _settingsService;
    private readonly LicenseService _licenseService;
    private readonly string _learningPath;
    private readonly Dictionary<IntPtr, WindowEntry> _entries = new();
    private readonly object _lock = new();
    private readonly object _saveLock = new();
    private readonly BlockingCollection<Point> _clickQueue = new(64);
    private readonly CancellationTokenSource _cts = new();
    private readonly int _selfPid = Environment.ProcessId;
    private volatile IReadOnlyList<SnapCandidate> _snapshot = Array.Empty<SnapCandidate>();
    private volatile bool _dirty;
    private bool _wasActive;

    public SnapLearningStore Store { get; }

    public static string DefaultLearningPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CursorJump", "snap-learning.json");

    public ElementSnapService(SettingsService settingsService, LicenseService licenseService, string? learningPath = null)
    {
        _settingsService = settingsService;
        _licenseService = licenseService;
        _learningPath = learningPath ?? DefaultLearningPath;
        SnapLearningStore store;
        try { store = SnapLearningStore.LoadFile(_learningPath); }
        catch (Exception ex)
        {
            DebugLog.Write($"ElementSnap: learning data load failed: {ex.GetType().Name}: {ex.Message}");
            store = new SnapLearningStore();
        }
        Store = store;
        DebugLog.Write($"ElementSnap: learning entries loaded = {Store.Count}");

        StartThread(CacheLoop, "ElementSnap.Cache");
        StartThread(LearnLoop, "ElementSnap.Learn");
    }

    /// <summary>機能が動作すべき状態か（設定 ON かつ Pro）。</summary>
    public bool IsActive => _settingsService.Current.ElementSnapEnabled && _licenseService.IsPro;

    /// <summary>現在保持しているジャンプ先候補（全ウィンドウ分）。</summary>
    public IReadOnlyList<SnapCandidate> Snapshot => _snapshot;

    /// <summary>左クリック位置を学習キューに積む（フック由来の UI スレッドから呼ぶ。満杯なら捨てる）。</summary>
    public void ObserveClick(int x, int y)
    {
        if (!IsActive || !_settingsService.Current.ElementSnapLearningEnabled) return;
        _clickQueue.TryAdd(new Point(x, y));
    }

    /// <summary>学習データを消去して保存する（設定画面のボタンから）。</summary>
    public void ClearLearning()
    {
        Store.Clear();
        SaveLearning();
        DebugLog.Write("ElementSnap: learning data cleared by user");
    }

    private static void StartThread(Action body, string name)
    {
        var t = new Thread(() => body()) { IsBackground = true, Name = name };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
    }

    // ───────────── 要素一覧の保持 ─────────────

    private void CacheLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            bool active = IsActive;
            if (active != _wasActive)
            {
                _wasActive = active;
                DebugLog.Write($"ElementSnap: {(active ? "activated" : "deactivated")}");
                if (!active)
                {
                    lock (_lock) _entries.Clear();
                    _snapshot = Array.Empty<SnapCandidate>();
                }
            }

            if (active)
            {
                try { Tick(); }
                catch (Exception ex) { DebugLog.Write($"ElementSnap: cache tick failed: {ex.GetType().Name}: {ex.Message}"); }
            }

            if (_cts.Token.WaitHandle.WaitOne(active ? LoopMs : InactiveLoopMs)) break;
        }
    }

    private void Tick()
    {
        var windows = VisibleTopLevelWindows();
        var fg = NativeMethods.GetForegroundWindow();
        long now = Environment.TickCount64;
        bool changed = false;

        lock (_lock)
        {
            foreach (var dead in _entries.Keys.Where(h => !windows.Contains(h)).ToList())
            {
                _entries.Remove(dead);
                changed = true;
            }
        }

        foreach (var h in windows)
        {
            if (_cts.IsCancellationRequested || !IsActive) return;

            WindowEntry? e;
            lock (_lock) _entries.TryGetValue(h, out e);
            if (e != null)
            {
                if (now < e.RetryAt) continue;
                int interval = h == fg ? ForegroundRefreshMs : OtherRefreshMs;
                if (e.Failures == 0 && now - e.ScannedAt < interval) continue;
            }
            string proc = e?.Process is { Length: > 0 } p ? p : ProcessName(h);

            var (result, timedOut, ms) = RunWithTimeout(() => Scan(h, proc), SnapTuning.ScanTimeoutMs);
            lock (_lock)
            {
                if (e == null)
                {
                    e = new WindowEntry { Process = proc };
                    _entries[h] = e;
                }
                if (timedOut)
                {
                    // 一度の遅延で永久除外しない（試作で Claude アプリが一時的に遅れて以後ジャンプ不能になった）。
                    // 待ち時間を倍々に延ばして再挑戦し、その間は前回の結果を使う。
                    e.Failures++;
                    long wait = Math.Min(SnapTuning.ScanBackoffMaxMs,
                        SnapTuning.ScanBackoffMinMs * (1L << Math.Min(e.Failures - 1, 6)));
                    e.RetryAt = Environment.TickCount64 + wait;
                    DebugLog.Write($"ElementSnap: scan timeout {proc} (#{e.Failures}) → retry in {wait / 1000}s");
                    continue;
                }
                if (e.Failures > 0) DebugLog.Write($"ElementSnap: scan recovered {proc} ({ms} ms)");
                e.Failures = 0;
                e.RetryAt = 0;
                e.Items = result?.Items ?? new List<SnapCandidate>();
                e.Domain = result?.Domain ?? "";
                e.WebAreas = result?.WebAreas ?? new List<Rectangle>();
                e.ScannedAt = Environment.TickCount64;
                changed = true;
            }
        }

        if (changed)
        {
            lock (_lock) _snapshot = _entries.Values.SelectMany(x => x.Items).ToList();
        }
    }

    private ScanResult Scan(IntPtr hwnd, string proc)
    {
        bool browser = SnapElementRules.IsBrowser(proc);
        var conds = ClickableTypes
            .Select(t => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, t))
            .ToList();
        if (browser)
        {
            // Web ページ領域（Document）とアドレスバーも同じ 1 回の走査で拾う
            conds.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            conds.Add(new PropertyCondition(AutomationElement.ClassNameProperty, SnapElementRules.OmniboxClassName));
        }

        var cache = new CacheRequest();
        cache.Add(AutomationElement.BoundingRectangleProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(AutomationElement.IsOffscreenProperty);
        cache.Add(AutomationElement.ClickablePointProperty);
        cache.Add(AutomationElement.ClassNameProperty);
        cache.Add(AutomationElement.AutomationIdProperty);
        cache.Add(AutomationElement.NameProperty);
        if (browser) cache.Add(ValuePattern.ValueProperty);
        cache.AutomationElementMode = AutomationElementMode.None;

        var root = AutomationElement.FromHandle(hwnd);
        AutomationElementCollection found;
        using (cache.Activate())
            found = root.FindAll(TreeScope.Descendants, new OrCondition(conds.ToArray()));

        string domain = "";
        var webAreas = new List<Rectangle>();
        var raw = new List<(AutomationElement E, Rectangle R, ControlType Ct, string Cls)>();
        foreach (AutomationElement e in found)
        {
            var ct = (ControlType)e.GetCachedPropertyValue(AutomationElement.ControlTypeProperty);
            var cls = (string)e.GetCachedPropertyValue(AutomationElement.ClassNameProperty);
            var rr = (System.Windows.Rect)e.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty);
            if (rr.IsEmpty) continue;
            var rect = new Rectangle((int)rr.X, (int)rr.Y, (int)rr.Width, (int)rr.Height);
            if (cls == SnapElementRules.OmniboxClassName)
            {
                if (e.GetCachedPropertyValue(ValuePattern.ValueProperty) is string url)
                    domain = SnapElementRules.HostFromUrl(url);
                continue;
            }
            if (ct == ControlType.Document) { webAreas.Add(rect); continue; }
            if ((bool)e.GetCachedPropertyValue(AutomationElement.IsOffscreenProperty)) continue;
            if (rect.Width < 4 || rect.Height < 4) continue;
            raw.Add((e, rect, ct, cls));
        }

        bool excludeBookmarks = _settingsService.Current.ElementSnapExcludeBookmarkBar;
        var list = new List<SnapCandidate>(raw.Count);
        foreach (var (e, rect, ct, cls) in raw)
        {
            if (excludeBookmarks && SnapElementRules.IsBookmarkBar(cls)) continue;
            var kind = ct == ControlType.RadioButton ? SnapElementKind.RadioButton
                     : ct == ControlType.CheckBox ? SnapElementKind.CheckBox
                     : SnapElementKind.Other;
            Point? cp = null;
            if (e.GetCachedPropertyValue(AutomationElement.ClickablePointProperty) is System.Windows.Point p
                && double.IsFinite(p.X) && double.IsFinite(p.Y))
                cp = new Point((int)p.X, (int)p.Y);
            var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            bool isWeb = browser && webAreas.Any(a => a.Contains(center));
            var key = SnapElementRules.BuildLearnKey(proc, ct.Id, cls,
                (string)e.GetCachedPropertyValue(AutomationElement.AutomationIdProperty),
                (string)e.GetCachedPropertyValue(AutomationElement.NameProperty),
                isWeb, domain) ?? "";
            list.Add(new SnapCandidate(rect, kind, cp, hwnd, key, SnapElementRules.BaseWeight(cls)));
        }
        return new ScanResult(list, domain, webAreas);
    }

    // ───────────── クリック学習 ─────────────

    private void LearnLoop()
    {
        long lastSave = Environment.TickCount64;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_clickQueue.TryTake(out var p, 1000, _cts.Token))
                {
                    var (log, timedOut, _) = RunWithTimeout(() => Learn(p), LearnTimeoutMs);
                    if (_settingsService.Current.VerboseLogging)
                        DebugLog.Write(timedOut ? "ElementSnap: learn timeout (skipped)" : $"ElementSnap: {log}");
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { DebugLog.Write($"ElementSnap: learn failed: {ex.GetType().Name}: {ex.Message}"); }

            if (_dirty && Environment.TickCount64 - lastSave > SaveIntervalMs)
            {
                lastSave = Environment.TickCount64;
                SaveLearning();
            }
        }
    }

    /// <summary>クリック位置の要素を特定して学習する。戻り値は診断ログ用（Web の要素名は含めない）。</summary>
    private string Learn(Point p)
    {
        var el = AutomationElement.FromPoint(new System.Windows.Point(p.X, p.Y));
        int depth = 0;
        while (el != null && depth <= MaxParentDepth && !ClickableIds.Contains(el.Current.ControlType.Id))
        {
            el = TreeWalker.ControlViewWalker.GetParent(el);
            depth++;
        }
        if (el == null || depth > MaxParentDepth) return "learn: not a clickable element";

        var hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = p.X, Y = p.Y }), NativeMethods.GA_ROOT);
        string proc = ProcessName(hwnd);
        var cur = el.Current;
        bool isWeb = false;
        string domain = "";
        if (SnapElementRules.IsBrowser(proc))
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(hwnd, out var e))
                {
                    isWeb = e.WebAreas.Any(r => r.Contains(p));
                    domain = e.Domain;
                }
            }
        }
        var key = SnapElementRules.BuildLearnKey(proc, cur.ControlType.Id, cur.ClassName, cur.AutomationId, cur.Name, isWeb, domain);
        if (key == null) return $"learn: {proc} unidentifiable element";

        var now = DateTime.UtcNow;
        Store.Record(key, now);
        _dirty = true;
        string type = cur.ControlType.ProgrammaticName.Replace("ControlType.", "");
        string what = isWeb ? $"web {domain} {type}"
                    : $"{type} {(SnapElementRules.IsUiClassName(cur.ClassName) ? cur.ClassName : "")}";
        return $"learn: {proc} {what} score={Store.Score(key, now):F1}";
    }

    private void SaveLearning()
    {
        _dirty = false;
        // 学習スレッドの定期保存と、消去ボタン・終了時の保存が同じ一時ファイルを奪い合わないよう直列化する
        lock (_saveLock)
        try { Store.SaveFile(_learningPath); }
        catch (Exception ex) { DebugLog.Write($"ElementSnap: learning data save failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ───────────── 共通 ─────────────

    /// <summary>
    /// UIA 呼び出しを別スレッドで実行し、時間内に終わらなければ諦める。
    /// 時間切れのスレッドは中断できないため放置される（バックグラウンドなので終了は妨げない）。
    /// </summary>
    private static (T? Result, bool TimedOut, long Ms) RunWithTimeout<T>(Func<T> body, int timeoutMs) where T : class
    {
        T? result = null;
        var sw = Stopwatch.StartNew();
        var worker = new Thread(() =>
        {
            try { result = body(); }
            catch (ElementNotAvailableException) { result = null; } // 走査中にウィンドウが閉じた
            catch (Exception ex) { DebugLog.Write($"ElementSnap: UIA call failed: {ex.GetType().Name}: {ex.Message}"); }
        })
        { IsBackground = true, Name = "ElementSnap.Worker" };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        bool ok = worker.Join(timeoutMs);
        return (result, !ok, sw.ElapsedMilliseconds);
    }

    private HashSet<IntPtr> VisibleTopLevelWindows()
    {
        var set = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) return true;
            if ((NativeMethods.GetWindowLong(h, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            NativeMethods.DwmGetWindowAttribute(h, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int));
            if (cloaked != 0) return true;
            NativeMethods.GetWindowThreadProcessId(h, out uint pid);
            if (pid == _selfPid) return true;
            NativeMethods.GetWindowRect(h, out var r);
            if (r.Right - r.Left < 100 || r.Bottom - r.Top < 100) return true;
            set.Add(h);
            return true;
        }, IntPtr.Zero);
        return set;
    }

    private static string ProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return "?"; }
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        if (_dirty) SaveLearning();
    }
}
