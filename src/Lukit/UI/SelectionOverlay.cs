using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Lukit.Display;

namespace Lukit.UI;

/// <summary>
/// A full-monitor, borderless overlay that shows the already-captured (and already
/// tone-mapped) screenshot frozen, dims it, and lets the user drag out a rectangle.
/// Selecting on the frozen, final-looking image is WYSIWYG and keeps the selection UI
/// itself out of the shot. Returns the selection in source-bitmap pixel coordinates.
/// </summary>
public sealed class SelectionOverlay : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_SHOWWINDOW = 0x0040;

    private readonly BitmapSource _image;
    private readonly Monitors.RECT _bounds;
    private readonly Canvas _canvas;
    private readonly Path _dim;
    private readonly Rectangle _border;

    private Point _start;
    private bool _dragging;

    /// <summary>The selected region in <see cref="_image"/> pixel coordinates (valid when DialogResult==true).</summary>
    public Int32Rect SelectedPixelRect { get; private set; }

    public SelectionOverlay(BitmapSource image, Monitors.RECT bounds)
    {
        _image = image;
        _bounds = bounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;

        // Root Grid stretches its children to the window; a Canvas does NOT (it would
        // render the bitmap at its natural pixel size, appearing zoomed on scaled displays).
        var preview = new Image { Source = image, Stretch = Stretch.Fill };

        // Transparent background makes the whole canvas hit-testable for the drag.
        _canvas = new Canvas { Background = Brushes.Transparent };

        _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) };
        _canvas.Children.Add(_dim);

        _border = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(255, 40, 170, 255)),
            StrokeThickness = 1.5,
            Visibility = Visibility.Collapsed,
        };
        _canvas.Children.Add(_border);

        var root = new Grid();
        root.Children.Add(preview);
        root.Children.Add(_canvas);
        Content = root;

        Loaded += (_, _) => UpdateDim();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Position exactly over the target monitor using physical pixels (DPI-independent).
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height, SWP_SHOWWINDOW);
        Activate();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(_canvas);
        _border.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Rect r = MakeRect(_start, e.GetPosition(_canvas));
        Canvas.SetLeft(_border, r.X);
        Canvas.SetTop(_border, r.Y);
        _border.Width = r.Width;
        _border.Height = r.Height;
        UpdateDim(r);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        Rect r = MakeRect(_start, e.GetPosition(_canvas));
        if (r.Width < 3 || r.Height < 3)
        {
            DialogResult = false;
            Close();
            return;
        }

        // Map from canvas (DIP) space to source-bitmap pixels.
        double sx = _image.PixelWidth / _canvas.ActualWidth;
        double sy = _image.PixelHeight / _canvas.ActualHeight;
        int px = (int)Math.Round(r.X * sx);
        int py = (int)Math.Round(r.Y * sy);
        int pw = (int)Math.Round(r.Width * sx);
        int ph = (int)Math.Round(r.Height * sy);

        px = Math.Clamp(px, 0, _image.PixelWidth - 1);
        py = Math.Clamp(py, 0, _image.PixelHeight - 1);
        pw = Math.Clamp(pw, 1, _image.PixelWidth - px);
        ph = Math.Clamp(ph, 1, _image.PixelHeight - py);

        SelectedPixelRect = new Int32Rect(px, py, pw, ph);
        DialogResult = true;
        Close();
    }

    private static Rect MakeRect(Point a, Point b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private void UpdateDim() => UpdateDim(Rect.Empty);

    private void UpdateDim(Rect hole)
    {
        double w = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : _bounds.Width;
        double h = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : _bounds.Height;

        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
        if (!hole.IsEmpty && hole.Width > 0 && hole.Height > 0)
            geometry.Children.Add(new RectangleGeometry(hole));
        _dim.Data = geometry;
    }
}
