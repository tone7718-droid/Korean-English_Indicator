using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HanEngIndicator.Models;
using HanEngIndicator.Native;

namespace HanEngIndicator.Forms;

/// <summary>
/// The small overlay badge ("가" / "A"). It is borderless, top-most, shown
/// without stealing focus, click-through (mouse events pass to the window
/// underneath), and hidden from the taskbar and Alt-Tab.
/// </summary>
public sealed class OverlayForm : Form
{
    private InputMode _mode = InputMode.Unknown;
    private int _badgeSize = 28;
    private float _fontPx = 16f;

    private static readonly Color KoreanBack = Color.FromArgb(0x1F, 0x6F, 0xD6); // blue
    private static readonly Color EnglishBack = Color.FromArgb(0xD9, 0x6A, 0x00); // amber
    private static readonly Color ForeColorText = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(0xFF, 0xFF, 0xFF);

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None; // we manage physical pixels ourselves
        DoubleBuffered = true;
        BackColor = Color.Black;
        Size = new Size(_badgeSize, _badgeSize);
    }

    /// <summary>Do not activate the form when it is shown.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_LAYERED      // alpha / translucency
                        | NativeMethods.WS_EX_TRANSPARENT  // click-through
                        | NativeMethods.WS_EX_NOACTIVATE   // never take focus
                        | NativeMethods.WS_EX_TOOLWINDOW   // hidden from Alt-Tab/taskbar
                        | NativeMethods.WS_EX_TOPMOST;     // always on top
            return cp;
        }
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    protected override void WndProc(ref Message m)
    {
        // Belt-and-suspenders click-through in addition to WS_EX_TRANSPARENT.
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = HTTRANSPARENT;
            return;
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Update the badge content, size, position (physical px) and opacity, then
    /// show it - all without activating or stealing focus.
    /// </summary>
    public void ShowBadge(InputMode mode, Point location, int badgeSize, double opacity)
    {
        _mode = mode;
        _badgeSize = Math.Max(16, badgeSize);
        _fontPx = _badgeSize * 0.56f;

        Opacity = Math.Clamp(opacity, 0.2, 1.0);

        Size newSize = new(_badgeSize, _badgeSize);
        if (Size != newSize)
        {
            Size = newSize;
            UpdateRegion();
        }

        if (Location != location)
        {
            Location = location;
        }

        if (!Visible)
        {
            Show();
        }

        Invalidate();
    }

    public void HideBadge()
    {
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        int radius = Math.Max(4, _badgeSize / 5);
        using GraphicsPath path = RoundedRect(new Rectangle(0, 0, _badgeSize, _badgeSize), radius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var rect = new Rectangle(0, 0, _badgeSize - 1, _badgeSize - 1);
        int radius = Math.Max(4, _badgeSize / 5);

        Color back = _mode == InputMode.Korean ? KoreanBack : EnglishBack;
        string text = _mode == InputMode.Korean ? "가" : "A";

        using (GraphicsPath path = RoundedRect(rect, radius))
        using (var brush = new SolidBrush(back))
        using (var pen = new Pen(BorderColor, Math.Max(1f, _badgeSize / 16f)))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        using var font = CreateBadgeFont(_mode, _fontPx);
        using var textBrush = new SolidBrush(ForeColorText);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, textBrush, new RectangleF(0, 0, _badgeSize, _badgeSize), format);
    }

    private static Font CreateBadgeFont(InputMode mode, float sizePx)
    {
        // Malgun Gothic renders the Hangul glyph crisply; fall back if absent.
        string family = mode == InputMode.Korean ? "Malgun Gothic" : "Segoe UI";
        try
        {
            return new Font(family, sizePx, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, sizePx, FontStyle.Bold, GraphicsUnit.Pixel);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();

        if (d <= 0)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
