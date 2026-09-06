using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var branding = Path.GetFullPath(args.Length > 0 ? args[0] : "Assets/Branding");
var platePath = File.Exists(Path.Combine(branding, "_original-256.png"))
    ? Path.Combine(branding, "_original-256.png")
    : Path.Combine(branding, "MgaSonicAnvil.png");
var pngOut = Path.Combine(branding, "MgaSonicAnvil.png");
var icoOut = Path.Combine(branding, "MgaSonicAnvil.ico");

using var plate = LoadUnlocked(platePath);
using var master = RenderMark(plate);
master.Save(pngOut, ImageFormat.Png);

var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
var pngs = new List<byte[]>();
foreach (var size in sizes)
{
    using var frame = size == 256 ? (Bitmap)master.Clone() : Resize(master, size);
    pngs.Add(ToPng(frame));
}

WriteIco(icoOut, sizes, pngs);
Console.WriteLine($"Wrote {icoOut} ({new FileInfo(icoOut).Length} bytes)");
Console.WriteLine($"Wrote {pngOut} ({new FileInfo(pngOut).Length} bytes)");

static Bitmap LoadUnlocked(string path)
{
    using var src = new Bitmap(path);
    return new Bitmap(src);
}

static Bitmap RenderMark(Bitmap plate)
{
    var dest = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
    var top = Color.FromArgb(255, 3, 138, 254);
    var bottom = Color.FromArgb(255, 1, 92, 252);
    using (var g = Graphics.FromImage(dest))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        using var plateBrush = new LinearGradientBrush(
            new Rectangle(0, 0, 256, 256),
            top,
            bottom,
            LinearGradientMode.Vertical);
        using var platePath = PlatePath(plate);
        g.FillPath(plateBrush, platePath);

        using var white = new SolidBrush(Color.White);
        using var anvil = AnvilPath();
        using var bolt = BoltPath();
        CenterOnCanvas(anvil, bolt);
        using var mark = new Region(anvil);
        mark.Exclude(bolt);
        g.FillRegion(white, mark);
        ApplyPlateAlpha(dest, plate);
    }

    return dest;
}

static void ApplyPlateAlpha(Bitmap dest, Bitmap plate)
{
    for (var y = 0; y < 256; y++)
    {
        for (var x = 0; x < 256; x++)
        {
            var p = dest.GetPixel(x, y);
            var a = plate.GetPixel(x, y).A;
            dest.SetPixel(x, y, Color.FromArgb(Math.Min(p.A, a), p));
        }
    }
}

static GraphicsPath PlatePath(Bitmap plate)
{
    var path = new GraphicsPath();
    for (var y = 0; y < 256; y++)
    {
        var runStart = -1;
        for (var x = 0; x <= 256; x++)
        {
            var inside = x < 256 && plate.GetPixel(x, y).A >= 8;
            if (inside && runStart < 0)
            {
                runStart = x;
            }
            else if (!inside && runStart >= 0)
            {
                path.AddRectangle(new Rectangle(runStart, y, x - runStart, 1));
                runStart = -1;
            }
        }
    }

    return path;
}

static void CenterOnCanvas(params GraphicsPath[] paths)
{
    var bounds = paths[0].GetBounds();
    using var matrix = new Matrix();
    matrix.Translate(
        128f - (bounds.X + bounds.Width / 2f),
        128f - (bounds.Y + bounds.Height / 2f));
    foreach (var path in paths)
    {
        path.Transform(matrix);
    }
}

static GraphicsPath AnvilPath()
{
    var p = new GraphicsPath();
    p.StartFigure();
    p.AddLine(24f, 118f, 62f, 86f);
    p.AddLine(62f, 86f, 80f, 72f);
    p.AddLine(80f, 72f, 170f, 72f);
    p.AddLine(170f, 72f, 186f, 86f);
    p.AddLine(186f, 86f, 232f, 86f);
    p.AddLine(232f, 86f, 232f, 124f);
    p.AddLine(232f, 124f, 184f, 124f);
    p.AddBezier(184f, 124f, 176f, 142f, 172f, 160f, 170f, 176f);
    p.AddLine(170f, 176f, 200f, 196f);
    p.AddLine(200f, 196f, 200f, 220f);
    p.AddLine(200f, 220f, 54f, 220f);
    p.AddLine(54f, 220f, 54f, 196f);
    p.AddLine(54f, 196f, 84f, 176f);
    p.AddBezier(84f, 176f, 82f, 160f, 78f, 142f, 70f, 124f);
    p.AddLine(70f, 124f, 28f, 136f);
    p.CloseFigure();
    return p;
}

static GraphicsPath BoltPath()
{
    // Heroicons / Material の稲妻。金床の輪郭の内側に収める。
    var src = new[]
    {
        new PointF(3.75f, 13.5f),
        new PointF(14.25f, 2.25f),
        new PointF(12f, 10.5f),
        new PointF(20.25f, 10.5f),
        new PointF(9.75f, 21.75f),
        new PointF(12f, 13.5f),
    };
    const float x0 = 3.75f;
    const float y0 = 2.25f;
    const float sw = 16.5f;
    const float sh = 19.5f;
    const float left = 92f;
    const float top = 78f;
    const float tw = 74f;
    const float th = 122f;
    var dest = new PointF[src.Length];
    for (var i = 0; i < src.Length; i++)
    {
        dest[i] = new PointF(
            left + (src[i].X - x0) * (tw / sw),
            top + (src[i].Y - y0) * (th / sh));
    }

    var p = new GraphicsPath();
    p.AddPolygon(dest);
    return p;
}

static Bitmap Resize(Bitmap src, int size)
{
    var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dest);
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.Clear(Color.Transparent);
    g.DrawImage(src, 0, 0, size, size);
    return dest;
}

static byte[] ToPng(Bitmap bmp)
{
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

static void WriteIco(string path, IReadOnlyList<int> sizes, IReadOnlyList<byte[]> pngs)
{
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)sizes.Count);
    var offset = 6 + 16 * sizes.Count;
    for (var i = 0; i < sizes.Count; i++)
    {
        var size = sizes[i];
        var w = size >= 256 ? 0 : size;
        bw.Write((byte)w);
        bw.Write((byte)w);
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(pngs[i].Length);
        bw.Write(offset);
        offset += pngs[i].Length;
    }

    foreach (var png in pngs)
    {
        bw.Write(png);
    }
}
