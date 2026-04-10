using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var outputPath = args.Length > 0 ? args[0] : "icon.ico";

var sizes = new[] { 16, 32, 48, 256 };
var bitmaps = new List<Bitmap>();

foreach (var size in sizes)
    bitmaps.Add(CreateIcon(size));

WriteIco(bitmaps, outputPath);

foreach (var bmp in bitmaps)
    bmp.Dispose();

Console.WriteLine($"Icon saved to {outputPath}");

// ────────────────────────────────────────────────────────
// 角丸正方形の背景を描画（青→紫グラデーション）
// ────────────────────────────────────────────────────────
static GraphicsPath RoundedRect(RectangleF rect, float radius)
{
    var path = new GraphicsPath();
    float d = radius * 2;
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
    path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
    path.CloseFigure();
    return path;
}

// ────────────────────────────────────────────────────────
// カーソル形状の頂点
// ────────────────────────────────────────────────────────
static PointF[] GetCursorPoints(float s, float ox, float oy) =>
[
    new( 0   * s + ox,  0   * s + oy),
    new( 0   * s + ox, 16   * s + oy),
    new( 3.5f* s + ox, 12.5f* s + oy),
    new( 5.8f* s + ox, 17   * s + oy),
    new( 8   * s + ox, 15.8f* s + oy),
    new( 5.8f* s + ox, 10.8f* s + oy),
    new(10.5f* s + ox, 10.8f* s + oy),
];

// ────────────────────────────────────────────────────────
// 回転付きカーソル描画
// ────────────────────────────────────────────────────────
static void DrawCursor(Graphics g, float s, float ox, float oy,
                       int alpha, Color fillColor, Color outlineColor,
                       float rotation = 0f, float glowRadius = 0f)
{
    var state = g.Save();

    if (rotation != 0f)
    {
        // 回転の中心 = カーソルの重心付近
        float cx = ox + 4f * s;
        float cy = oy + 8f * s;
        g.TranslateTransform(cx, cy);
        g.RotateTransform(rotation);
        g.TranslateTransform(-cx, -cy);
    }

    var pts = GetCursorPoints(s, ox, oy);
    using var path = new GraphicsPath();
    path.AddPolygon(pts);

    // グロー（発光）エフェクト
    if (glowRadius > 0)
    {
        for (int i = 3; i >= 1; i--)
        {
            float expand = glowRadius * i * 0.5f;
            int glowAlpha = (int)(alpha * 0.08f);
            using var glowPen = new Pen(Color.FromArgb(glowAlpha, fillColor), expand);
            g.DrawPath(glowPen, path);
        }
    }

    // 塗り
    using var fill = new SolidBrush(Color.FromArgb(alpha, fillColor));
    g.FillPath(fill, path);

    // 縁取り
    float pw = Math.Max(0.6f, s * 0.9f);
    using var outline = new Pen(Color.FromArgb(alpha, outlineColor), pw);
    outline.LineJoin = LineJoin.Round;
    g.DrawPath(outline, path);

    g.Restore(state);
}

// ────────────────────────────────────────────────────────
// 軌跡弧を描画（分身間のジャンプ感）
// ────────────────────────────────────────────────────────
static void DrawTrailArc(Graphics g, float size, float x1, float y1, float x2, float y2, int alpha)
{
    float s = size / 256f;
    using var pen = new Pen(Color.FromArgb(alpha, 180, 220, 255), 2.5f * s);
    pen.DashStyle = DashStyle.Dot;
    pen.StartCap = LineCap.Round;
    pen.EndCap = LineCap.Round;

    // ベジェ曲線で弧を描く
    float midX = (x1 + x2) / 2f;
    float midY = Math.Min(y1, y2) - 18f * s;
    g.DrawBezier(pen, x1, y1, midX - 10f * s, midY, midX + 10f * s, midY, x2, y2);
}

// ────────────────────────────────────────────────────────
// メイン描画
// ────────────────────────────────────────────────────────
static Bitmap CreateIcon(int size)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.Transparent);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

    float pad = size * 0.04f;
    var bgRect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
    float cornerRadius = size * 0.2f;

    // ── 背景: 青→紫グラデーション ──
    using var bgPath = RoundedRect(bgRect, cornerRadius);
    using var bgBrush = new LinearGradientBrush(
        bgRect,
        Color.FromArgb(255, 40, 100, 220),   // 青
        Color.FromArgb(255, 130, 60, 200),    // 紫
        LinearGradientMode.ForwardDiagonal);
    g.FillPath(bgBrush, bgPath);

    // 背景に微妙な光沢（上部）
    var shineRect = new RectangleF(bgRect.X, bgRect.Y, bgRect.Width, bgRect.Height * 0.5f);
    using var shineBrush = new LinearGradientBrush(
        shineRect,
        Color.FromArgb(50, 255, 255, 255),
        Color.FromArgb(0, 255, 255, 255),
        LinearGradientMode.Vertical);
    g.FillRectangle(shineBrush, shineRect);

    // ── カーソル描画スケール ──
    float cs = size / 28f; // カーソルスケール

    // カーソル配置位置
    float c1x = size * 0.52f, c1y = size * 0.50f;   // 最遠分身（右下）
    float c2x = size * 0.36f, c2y = size * 0.35f;   // 中間分身
    float c3x = size * 0.15f, c3y = size * 0.15f;   // 本体（左上）

    // ── 軌跡弧 ──
    DrawTrailArc(g, size, c1x + 4 * cs, c1y + 4 * cs,
                          c2x + 4 * cs, c2y + 4 * cs, 40);
    DrawTrailArc(g, size, c2x + 4 * cs, c2y + 4 * cs,
                          c3x + 4 * cs, c3y + 4 * cs, 70);

    // ── 影分身（最遠）──
    DrawCursor(g, cs, c1x, c1y,
               alpha: 70,
               fillColor: Color.FromArgb(200, 220, 255),
               outlineColor: Color.FromArgb(60, 80, 160),
               rotation: 8f);

    // ── 影分身（中間）──
    DrawCursor(g, cs, c2x, c2y,
               alpha: 140,
               fillColor: Color.FromArgb(220, 235, 255),
               outlineColor: Color.FromArgb(50, 70, 150),
               rotation: 4f);

    // ── 本体カーソル ──
    DrawCursor(g, cs, c3x, c3y,
               alpha: 255,
               fillColor: Color.White,
               outlineColor: Color.FromArgb(30, 50, 120),
               rotation: 0f,
               glowRadius: 4f * cs);

    return bmp;
}

// ────────────────────────────────────────────────────────
// ICO ファイル書き出し
// ────────────────────────────────────────────────────────
static void WriteIco(List<Bitmap> bitmaps, string outputPath)
{
    var pngList = new List<byte[]>();
    foreach (var bmp in bitmaps)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        pngList.Add(ms.ToArray());
    }

    int count = bitmaps.Count;
    int headerSize = 6;
    int dirEntrySize = 16;
    int dataOffset = headerSize + dirEntrySize * count;

    using var writer = new BinaryWriter(File.Create(outputPath));

    writer.Write((short)0);
    writer.Write((short)1);
    writer.Write((short)count);

    for (int i = 0; i < count; i++)
    {
        int w = bitmaps[i].Width;
        int h = bitmaps[i].Height;
        writer.Write((byte)(w >= 256 ? 0 : w));
        writer.Write((byte)(h >= 256 ? 0 : h));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(pngList[i].Length);
        writer.Write(dataOffset);
        dataOffset += pngList[i].Length;
    }

    foreach (var png in pngList)
        writer.Write(png);
}
