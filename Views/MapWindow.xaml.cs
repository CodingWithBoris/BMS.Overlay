using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using BMS.Overlay.Services;
using BMS.Overlay.ViewModels;
using BMS.Shared.Models;
using Microsoft.Win32;

namespace BMS.Overlay.Views;

public partial class MapWindow : Window
{
    private readonly MapDataService _mapDataService;
    private readonly ApiService _apiService;
    private readonly MainViewModel _viewModel;
    private readonly SignalRService? _signalRService;

    // Stable property key for tagging Ink strokes with their server-assigned ID.
    // Stroke.AddPropertyData supports string values and survives Clone().
    private static readonly Guid StrokeIdProperty = Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

    // Stores both icon type and server ID on each icon FrameworkElement's Tag
    private record IconData(string IconType, string? ServerId = null);

    // Auth state (session only — never persisted)
    private bool _isDrawingAuthorized = false;

    // Drawing state
    private Color _currentColor = Color.FromRgb(0xCC, 0x22, 0x22);
    private double _currentSize = 3.0;
    private bool _isEraserMode = false;

    // Draw Mode — when ON, left-click draws; when OFF, canvas area is click-through
    private bool _drawModeActive = false;

    // Left-click drawing (manual stroke building — works in AllowsTransparency windows)
    private bool _isLeftDrawing = false;
    private Stroke? _activeLeftStroke = null;
    private StrokeCollection? _leftDrawSnapshot = null;

    // Undo / Redo (max 10 steps)
    private readonly Stack<StrokeCollection> _undoStack = new();
    private readonly Stack<StrokeCollection> _redoStack = new();
    private bool _suppressStrokeEvents = false;

    // Icon placement
    private string? _pendingIconType = null;

    // Icon dragging (right-click drag)
    private FrameworkElement? _draggingIcon = null;
    private Point _dragStartPoint;
    private double _dragStartLeft;
    private double _dragStartTop;

    // Zoom
    private double _zoomScale = 1.0;

    // Pan (middle-click drag)
    private bool _isPanning = false;
    private Point _panStart;
    private double _panOriginX;
    private double _panOriginY;

    // Custom map path
    private string? _customMapPath = null;

    // Current order id for persistence
    private string? _currentOrderId = null;

    public MapWindow(MapDataService mapDataService, ApiService apiService, MainViewModel viewModel,
        SignalRService? signalRService = null)
    {
        InitializeComponent();
        _mapDataService = mapDataService;
        _apiService = apiService;
        _viewModel = viewModel;
        _signalRService = signalRService;

        // Keep drawing layers sized to the visible area
        DrawingHost.SizeChanged += OnDrawingHostSizeChanged;

        // Reload map data when the current order changes
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentOrder))
                Dispatcher.Invoke(async () => await LoadMapDataAsync());
        };

        // Real-time map updates from other clients
        if (_signalRService != null)
            _signalRService.OnMapUpdated += HandleMapUpdated;

        Loaded += async (_, _) =>
        {
            UpdateCanvasSizes();
            await LoadMapDataAsync();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Prevent the overlay from stealing focus from the game
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

        // Partial click-through: canvas area passes clicks to game when Draw Mode is OFF.
        // The toolbar area is always interactive.
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    // Minimum toolbar height used as a safe fallback before WPF layout completes.
    // The toolbar Border has Padding="6,4" and buttons Height="22", so ~32 px at minimum.
    private const int ToolbarMinHeight = 40;
    private const int WM_NCHITTEST  = 0x0084;
    private const int HTTRANSPARENT = -1;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && !_drawModeActive)
        {
            // Screen coords are packed into lParam as LOWORD=x, HIWORD=y.
            var lo = (int)(lParam.ToInt64() & 0xFFFF);
            var hi = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
            var screenX = (lo > 32767) ? lo - 65536 : lo;   // sign-extend 16-bit
            var screenY = (hi > 32767) ? hi - 65536 : hi;

            var clientPoint = PointFromScreen(new Point(screenX, screenY));

            // Use whichever is larger: the actual rendered toolbar height or the safe minimum.
            // This prevents the toolbar from becoming transparent when ActualHeight is 0
            // (e.g., before the first layout pass completes).
            var toolbarH = Math.Max(ToolbarBorder.ActualHeight, ToolbarMinHeight);

            if (clientPoint.Y > toolbarH)
            {
                handled = true;
                return new IntPtr(HTTRANSPARENT);
            }
        }
        return IntPtr.Zero;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Canvas sizing (keeps all layers flush with the visible area)
    // ═══════════════════════════════════════════════════════════════

    private void OnDrawingHostSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateCanvasSizes();

    private void UpdateCanvasSizes()
    {
        var w = DrawingHost.ActualWidth;
        var h = DrawingHost.ActualHeight;
        if (w <= 0 || h <= 0) return;
        MapImage.Width = w;
        MapImage.Height = h;
        DrawingCanvas.Width = w;
        DrawingCanvas.Height = h;
        IconsCanvas.Width = w;
        IconsCanvas.Height = h;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Coordinate helpers — image-space ↔ canvas-space
    //  All coordinates sent to/from the server are in image-space
    //  (intrinsic pixels of the map image), independent of window size or zoom.
    // ═══════════════════════════════════════════════════════════════

    private (double scale, double ox, double oy) GetImageRenderParams()
    {
        if (MapImage.Source is not BitmapSource bmp || bmp.PixelWidth == 0)
            return (1, 0, 0);
        double s = Math.Min(DrawingHost.ActualWidth / bmp.PixelWidth,
                            DrawingHost.ActualHeight / bmp.PixelHeight);
        double rW = bmp.PixelWidth * s, rH = bmp.PixelHeight * s;
        return (s, (DrawingHost.ActualWidth - rW) / 2, (DrawingHost.ActualHeight - rH) / 2);
    }

    private Point CanvasToImage(Point p)
    {
        var (s, ox, oy) = GetImageRenderParams();
        return new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    private Point ImageToCanvas(Point p)
    {
        var (s, ox, oy) = GetImageRenderParams();
        return new Point(p.X * s + ox, p.Y * s + oy);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Persistence — load / save
    // ═══════════════════════════════════════════════════════════════

    private async Task LoadMapDataAsync()
    {
        var order = _viewModel.CurrentOrder;
        var orderId = order?.Id;

        // Clear everything when no order is selected
        if (string.IsNullOrEmpty(orderId))
        {
            ClearCanvasInternal();
            _currentOrderId = null;
            return;
        }

        if (orderId == _currentOrderId) return; // same order — skip reload
        _currentOrderId = orderId;

        // 1. Load map image: prefer shared URL on the order, fall back to local custom path
        if (!string.IsNullOrEmpty(order?.MapImageUrl))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(order.MapImageUrl);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                MapImage.Source = bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MapWindow] Failed to load map URL: {ex.Message}");
                MapImage.Source = null;
            }
        }
        else
        {
            // Fall back to locally loaded custom map
            if (!string.IsNullOrEmpty(_customMapPath) && File.Exists(_customMapPath))
            {
                try { MapImage.Source = new BitmapImage(new Uri(_customMapPath)); }
                catch { MapImage.Source = null; _customMapPath = null; }
            }
            else
            {
                MapImage.Source = null;
            }
        }

        // 2. Load map state — try API first, fall back to local JSON
        var factionId = _viewModel.SelectedFactionId;
        var apiState = !string.IsNullOrEmpty(factionId)
            ? await _apiService.GetMapStateAsync(factionId, orderId)
            : null;

        _suppressStrokeEvents = true;
        DrawingCanvas.Strokes.Clear();
        IconsCanvas.Children.Clear();

        if (apiState != null)
        {
            // Render from server state (image-space → canvas-space)
            foreach (var ms in apiState.Strokes)
            {
                try
                {
                    var pts = ms.Points
                        .Select(p => ImageToCanvas(new Point(p.X, p.Y)))
                        .Select(cp => new StylusPoint(cp.X, cp.Y));
                    var sc = new StylusPointCollection(pts);
                    if (sc.Count == 0) continue;
                    var color = (Color)ColorConverter.ConvertFromString(ms.Color);
                    var da = new DrawingAttributes { Color = color, Width = ms.Size, Height = ms.Size, FitToCurve = true };
                    var stroke = new Stroke(sc, da);
                    stroke.AddPropertyData(StrokeIdProperty, ms.Id);
                    DrawingCanvas.Strokes.Add(stroke);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MapWindow] Stroke restore error: {ex.Message}");
                }
            }

            foreach (var icon in apiState.Icons)
            {
                var cp = ImageToCanvas(new Point(icon.X, icon.Y));
                PlaceIconAt(icon.IconType, cp.X, cp.Y, icon.Id);
            }
        }
        else
        {
            // Fall back to local JSON (canvas-space coordinates, resolution-dependent)
            var mapData = await _mapDataService.LoadAsync(orderId);
            _customMapPath = mapData.CustomMapPath;

            foreach (var ms in mapData.Strokes)
            {
                try
                {
                    var pts = new StylusPointCollection(ms.Points.Select(p => new StylusPoint(p.X, p.Y)));
                    if (pts.Count == 0) continue;
                    var color = (Color)ColorConverter.ConvertFromString(ms.Color);
                    var da = new DrawingAttributes { Color = color, Width = ms.StrokeSize, Height = ms.StrokeSize, FitToCurve = true };
                    DrawingCanvas.Strokes.Add(new Stroke(pts, da));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MapWindow] Stroke restore error: {ex.Message}");
                }
            }

            foreach (var icon in mapData.Icons)
                PlaceIconAt(icon.IconType, icon.X, icon.Y);
        }

        _suppressStrokeEvents = false;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private async Task SaveMapDataAsync()
    {
        if (string.IsNullOrEmpty(_currentOrderId)) return;

        var strokes = DrawingCanvas.Strokes
            .Select(s => new MapStroke
            {
                Color = s.DrawingAttributes.Color.ToString(),
                StrokeSize = s.DrawingAttributes.Width,
                Points = s.StylusPoints.Select(p => new MapPoint { X = p.X, Y = p.Y }).ToList()
            })
            .ToList();

        var icons = IconsCanvas.Children.OfType<FrameworkElement>()
            .Select(el => new MapIcon
            {
                IconType = (el.Tag as IconData)?.IconType ?? el.Tag?.ToString() ?? "objective",
                X = Canvas.GetLeft(el),
                Y = Canvas.GetTop(el)
            })
            .ToList();

        await _mapDataService.SaveAsync(new MapData
        {
            OrderId = _currentOrderId,
            CustomMapPath = _customMapPath,
            Strokes = strokes,
            Icons = icons
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Window lifecycle
    // ═══════════════════════════════════════════════════════════════

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_signalRService != null)
            _signalRService.OnMapUpdated -= HandleMapUpdated;
        await SaveMapDataAsync();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Roblox visibility (same as NotepadWindow — hides with main overlay)
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    //  Auth / Unlock drawing
    // ═══════════════════════════════════════════════════════════════

    private async void OnUnlock_Click(object sender, RoutedEventArgs e)
    {
        if (_isDrawingAuthorized)
        {
            LockDrawing();
            return;
        }

        var factionId = _viewModel.SelectedFactionId;
        if (string.IsNullOrEmpty(factionId))
        {
            MessageBox.Show("Please select a faction first.", "No Faction",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var prompt = new PasswordPromptWindow();
        if (prompt.ShowDialog() != true || string.IsNullOrEmpty(prompt.Password))
            return;

        var ok = await _apiService.VerifyOfficerPasswordAsync(factionId, prompt.Password);
        if (!ok)
        {
            MessageBox.Show(
                "Incorrect password. Enter the faction's ControlPanel (officer) password.",
                "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UnlockDrawing();
    }

    private void UnlockDrawing()
    {
        _isDrawingAuthorized = true;
        DrawingCanvas.IsEnabled = true;
        SetToolbarEnabled(true);
        ApplyCurrentDrawingAttributes();
        UnlockBtn.Content = "🔓 Lock Drawing";
        UnlockBtn.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xAA, 0x44));
        UnlockBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xAA, 0x44));
    }

    private void LockDrawing()
    {
        _isDrawingAuthorized = false;

        // Return canvas to click-through mode (WM_NCHITTEST reads _drawModeActive)
        if (_drawModeActive)
        {
            _drawModeActive = false;
            DrawModeBtn.Content = "🖊 Draw Mode";
            DrawModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            DrawModeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            DebugStatus.Text = "● Click-through";
            DebugStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }

        CancelActiveStroke();
        DrawingCanvas.IsEnabled = false;
        SetToolbarEnabled(false);
        UnlockBtn.Content = "🔒 Unlock Drawing";
        UnlockBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
        UnlockBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
        _pendingIconType = null;
        DrawingHost.Cursor = Cursors.Arrow;
    }

    private void CancelActiveStroke()
    {
        if (!_isLeftDrawing) return;
        _isLeftDrawing = false;
        _activeLeftStroke = null;
        _leftDrawSnapshot = null;
        DrawingHost.ReleaseMouseCapture();
    }

    private void SetToolbarEnabled(bool enabled)
    {
        // DrawModeBtn is intentionally excluded — it must stay clickable at all times.
        ColorRed.IsEnabled = enabled;
        ColorBlue.IsEnabled = enabled;
        ColorGreen.IsEnabled = enabled;
        ColorYellow.IsEnabled = enabled;
        ColorWhite.IsEnabled = enabled;
        SizeSmall.IsEnabled = enabled;
        SizeMedium.IsEnabled = enabled;
        SizeLarge.IsEnabled = enabled;
        EraserBtn.IsEnabled = enabled;
        UndoBtn.IsEnabled = enabled;
        RedoBtn.IsEnabled = enabled;
        IconsBtn.IsEnabled = enabled;
        ClearBtn.IsEnabled = enabled;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Draw Mode toggle — switches between click-through and interactive
    //  Called from the in-window button AND from the main overlay's 🗺● button
    // ═══════════════════════════════════════════════════════════════

    private void OnDrawMode_Click(object sender, RoutedEventArgs e) => ToggleDrawMode();

    /// <summary>
    /// Toggles Draw Mode: OFF = canvas area HTTRANSPARENT (clicks pass to game),
    /// toolbar always stays interactive. ON = entire window captures input for drawing.
    /// Called by both the toolbar button and the main overlay's map button.
    /// </summary>
    public void ToggleDrawMode()
    {
        _drawModeActive = !_drawModeActive;
        // WndProc reads _drawModeActive — no explicit Win32 call needed.

        if (_drawModeActive)
        {
            DrawModeBtn.Content = "✏ Drawing";
            DrawModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x55, 0xCC));
            DrawModeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x55, 0xCC));
            DebugStatus.Text = "● INTERACTIVE";
            DebugStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xCC, 0x55));
        }
        else
        {
            DrawModeBtn.Content = "🖊 Draw Mode";
            DrawModeBtn.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            DrawModeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            _pendingIconType = null;
            DrawingHost.Cursor = Cursors.Arrow;
            CancelActiveStroke();
            DebugStatus.Text = "● Click-through";
            DebugStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Color selection
    // ═══════════════════════════════════════════════════════════════

    private void OnColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string hex) return;

        _currentColor = (Color)ColorConverter.ConvertFromString(hex);
        _isEraserMode = false;
        EraserBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

        // Highlight selected color swatch
        foreach (var cb in new[] { ColorRed, ColorBlue, ColorGreen, ColorYellow, ColorWhite })
            cb.BorderBrush = new SolidColorBrush(cb == btn ? Colors.White : Colors.Transparent);

        ApplyCurrentDrawingAttributes();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Stroke size
    // ═══════════════════════════════════════════════════════════════

    private void OnSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || !double.TryParse(btn.Tag?.ToString(), out var size)) return;

        _currentSize = size;
        _isEraserMode = false;
        EraserBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        ApplyCurrentDrawingAttributes();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Eraser
    // ═══════════════════════════════════════════════════════════════

    private void OnEraser_Click(object sender, RoutedEventArgs e)
    {
        _isEraserMode = !_isEraserMode;
        EraserBtn.BorderBrush = _isEraserMode
            ? new SolidColorBrush(Colors.White)
            : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

        if (!_isEraserMode)
            ApplyCurrentDrawingAttributes();
    }

    private void ApplyCurrentDrawingAttributes()
    {
        DrawingCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _currentColor,
            Width = _currentSize,
            Height = _currentSize,
            FitToCurve = true
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Undo / Redo
    // ═══════════════════════════════════════════════════════════════

    private void OnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        _redoStack.Push(DrawingCanvas.Strokes.Clone());

        var target = _undoStack.Pop();
        _suppressStrokeEvents = true;
        DrawingCanvas.Strokes.Clear();
        foreach (var s in target)
            DrawingCanvas.Strokes.Add(s.Clone());
        _suppressStrokeEvents = false;
    }

    private void OnRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        _undoStack.Push(DrawingCanvas.Strokes.Clone());

        var target = _redoStack.Pop();
        _suppressStrokeEvents = true;
        DrawingCanvas.Strokes.Clear();
        foreach (var s in target)
            DrawingCanvas.Strokes.Add(s.Clone());
        _suppressStrokeEvents = false;
    }

    private void TrimUndoStack()
    {
        if (_undoStack.Count <= 10) return;
        var items = _undoStack.ToArray().Take(10).Reverse().ToArray();
        _undoStack.Clear();
        foreach (var item in items)
            _undoStack.Push(item);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Zoom (scroll-wheel, zooms toward cursor)
    // ═══════════════════════════════════════════════════════════════

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(DrawingHost);
        var oldZoom = _zoomScale;
        _zoomScale = Math.Clamp(_zoomScale + (e.Delta > 0 ? 0.1 : -0.1), 0.5, 4.0);

        if (Math.Abs(oldZoom - _zoomScale) < 0.001)
        {
            e.Handled = true;
            return;
        }

        // Keep the point under the cursor fixed:
        // newPan = mousePos - (mousePos - oldPan) * (newScale / oldScale)
        var ratio = _zoomScale / oldZoom;
        PanTransform.X = mousePos.X - (mousePos.X - PanTransform.X) * ratio;
        PanTransform.Y = mousePos.Y - (mousePos.Y - PanTransform.Y) * ratio;

        ZoomTransform.ScaleX = _zoomScale;
        ZoomTransform.ScaleY = _zoomScale;
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Pan (middle-click drag)
    // ═══════════════════════════════════════════════════════════════

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panStart = e.GetPosition(DrawingHost);
            _panOriginX = PanTransform.X;
            _panOriginY = PanTransform.Y;
            DrawingHost.CaptureMouse();
            DrawingHost.Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            if (!_isLeftDrawing)
                DrawingHost.ReleaseMouseCapture();
            DrawingHost.Cursor = !string.IsNullOrEmpty(_pendingIconType) ? Cursors.Cross : Cursors.Arrow;
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Reset view (zoom + pan)
    // ═══════════════════════════════════════════════════════════════

    private void OnResetView_Click(object sender, RoutedEventArgs e)
    {
        _zoomScale = 1.0;
        ZoomTransform.ScaleX = 1.0;
        ZoomTransform.ScaleY = 1.0;
        PanTransform.X = 0;
        PanTransform.Y = 0;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Left-click drawing (manual stroke building)
    // ═══════════════════════════════════════════════════════════════

    private void OnCanvasLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Icon placement has priority
        if (!string.IsNullOrEmpty(_pendingIconType))
        {
            var pos = e.GetPosition(ZoomCanvas);
            var iconType = _pendingIconType!;
            _pendingIconType = null;
            var placedIcon = PlaceIconAt(iconType, pos.X, pos.Y);
            DrawingHost.Cursor = Cursors.Arrow;
            // Sync to server (fire-and-forget; assigns serverId when response arrives)
            _ = PostIconAsync(placedIcon, iconType, pos);
            e.Handled = true;
            return;
        }

        if (!_drawModeActive || !_isDrawingAuthorized) return;

        _isLeftDrawing = true;
        _leftDrawSnapshot = DrawingCanvas.Strokes.Clone();
        _redoStack.Clear();

        if (_isEraserMode)
        {
            EraseAtPoint(e.GetPosition(DrawingCanvas));
            e.Handled = true;
            return;
        }

        var start = e.GetPosition(ZoomCanvas);
        DebugStatus.Text = $"✓ CLICK at {start.X:F0},{start.Y:F0}";
        var points = new StylusPointCollection { new StylusPoint(start.X, start.Y) };
        var da = new DrawingAttributes
        {
            Color = _currentColor,
            Width = _currentSize,
            Height = _currentSize,
            FitToCurve = true
        };
        _activeLeftStroke = new Stroke(points, da);
        DrawingCanvas.Strokes.Add(_activeLeftStroke);
        DrawingHost.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isLeftDrawing) return;

        var completedStroke = _activeLeftStroke; // capture before nulling
        _isLeftDrawing = false;
        _activeLeftStroke = null;

        if (_leftDrawSnapshot != null)
        {
            _undoStack.Push(_leftDrawSnapshot);
            TrimUndoStack();
            _leftDrawSnapshot = null;
        }

        if (!_isPanning)
            DrawingHost.ReleaseMouseCapture();

        // Sync completed stroke to server
        if (completedStroke != null && !_isEraserMode)
            _ = PostStrokeAsync(completedStroke);

        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        // Handle panning (middle-click drag) — takes priority
        if (_isPanning)
        {
            var pos = e.GetPosition(DrawingHost);
            PanTransform.X = _panOriginX + (pos.X - _panStart.X);
            PanTransform.Y = _panOriginY + (pos.Y - _panStart.Y);
            return;
        }

        if (_drawModeActive)
        {
            var mp = e.GetPosition(DrawingHost);
            var action = _isEraserMode ? "ERASE" : (_isLeftDrawing ? "DRAWING" : "ready");
            DebugStatus.Text = $"● {action}  X:{mp.X:F0} Y:{mp.Y:F0}";
        }

        if (!_isLeftDrawing || !_drawModeActive) return;
        if (_isEraserMode)
        {
            EraseAtPoint(e.GetPosition(DrawingCanvas));
            return;
        }

        if (_activeLeftStroke == null) return;
        var drawPos = e.GetPosition(ZoomCanvas);
        _activeLeftStroke.StylusPoints.Add(new StylusPoint(drawPos.X, drawPos.Y));
    }

    private void EraseAtPoint(Point p)
    {
        var hit = DrawingCanvas.Strokes.HitTest(p, _currentSize * 2);
        if (hit.Count == 0) return;
        var factionId = _viewModel.SelectedFactionId;
        foreach (var s in hit)
        {
            // Sync deletion to server
            if (!string.IsNullOrEmpty(factionId) && !string.IsNullOrEmpty(_currentOrderId)
                && s.ContainsPropertyData(StrokeIdProperty))
            {
                var strokeId = (string)s.GetPropertyData(StrokeIdProperty);
                _ = _apiService.DeleteMapStrokeAsync(factionId, _currentOrderId, strokeId);
            }
            DrawingCanvas.Strokes.Remove(s);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Icon picker popup
    // ═══════════════════════════════════════════════════════════════

    private void OnShowIconPicker_Click(object sender, RoutedEventArgs e)
        => IconPickerPopup.IsOpen = !IconPickerPopup.IsOpen;

    private void OnPlaceIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _pendingIconType = btn.Tag?.ToString();
        IconPickerPopup.IsOpen = false;

        if (!string.IsNullOrEmpty(_pendingIconType))
            DrawingHost.Cursor = Cursors.Cross;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Icon placement helpers
    // ═══════════════════════════════════════════════════════════════

    private Border PlaceIconAt(string iconType, double canvasX, double canvasY, string? serverId = null)
    {
        var emoji = iconType switch
        {
            "infantry"  => "🪖",
            "vehicle"   => "🚗",
            "objective" => "⭐",
            "enemy"     => "🔴",
            "friendly"  => "🔵",
            "artillery" => "💥",
            _           => "⭐"
        };

        var border = new Border
        {
            Tag = new IconData(iconType, serverId),
            Cursor = Cursors.SizeAll,
            Child = new TextBlock { Text = emoji, FontSize = 20, IsHitTestVisible = false }
        };

        Canvas.SetLeft(border, canvasX);
        Canvas.SetTop(border, canvasY);

        border.MouseRightButtonDown += OnIconMouseDown;
        border.MouseRightButtonUp += OnIconMouseUp;

        IconsCanvas.Children.Add(border);
        return border;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Icon drag support (right-click drag; shift+right-click = delete menu)
    // ═══════════════════════════════════════════════════════════════

    private void OnIconMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement icon) return;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            ShowIconContextMenu(icon);
            e.Handled = true;
            return;
        }
        _draggingIcon = icon;
        _dragStartPoint = e.GetPosition(IconsCanvas);
        _dragStartLeft = Canvas.GetLeft(icon);
        _dragStartTop = Canvas.GetTop(icon);
        icon.CaptureMouse();
        icon.MouseMove += OnIconMouseMove;
        icon.MouseRightButtonUp += OnIconMouseUp;
        e.Handled = true;
    }

    private void OnIconMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingIcon == null || !_draggingIcon.IsMouseCaptured) return;
        var pos = e.GetPosition(IconsCanvas);
        Canvas.SetLeft(_draggingIcon, _dragStartLeft + (pos.X - _dragStartPoint.X));
        Canvas.SetTop(_draggingIcon, _dragStartTop + (pos.Y - _dragStartPoint.Y));
    }

    private void OnIconMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingIcon == null) return;
        var movedIcon = _draggingIcon;
        _draggingIcon.ReleaseMouseCapture();
        _draggingIcon.MouseMove -= OnIconMouseMove;
        _draggingIcon.MouseRightButtonUp -= OnIconMouseUp;
        _draggingIcon = null;

        // Sync move to server
        _ = PutIconMoveAsync(movedIcon);
    }

    private void ShowIconContextMenu(FrameworkElement icon)
    {
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) =>
        {
            IconsCanvas.Children.Remove(icon);
            // Sync deletion to server
            var iconData = icon.Tag as IconData;
            if (iconData?.ServerId != null)
            {
                var factionId = _viewModel.SelectedFactionId;
                if (!string.IsNullOrEmpty(factionId) && !string.IsNullOrEmpty(_currentOrderId))
                    _ = _apiService.DeleteMapIconAsync(factionId, _currentOrderId, iconData.ServerId);
            }
        };
        icon.ContextMenu = new ContextMenu { Items = { deleteItem } };
        icon.ContextMenu.IsOpen = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Clear all
    // ═══════════════════════════════════════════════════════════════

    private void OnClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Clear all drawings and icons?", "Clear Map",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        ClearCanvasInternal();
        // Sync to server
        var factionId = _viewModel.SelectedFactionId;
        if (!string.IsNullOrEmpty(factionId) && !string.IsNullOrEmpty(_currentOrderId))
            _ = _apiService.ClearMapAsync(factionId, _currentOrderId);
    }

    private void ClearCanvasInternal()
    {
        _suppressStrokeEvents = true;
        DrawingCanvas.Strokes.Clear();
        _suppressStrokeEvents = false;
        IconsCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Load custom map image
    // ═══════════════════════════════════════════════════════════════

    private void OnLoadMap_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Map Image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        _customMapPath = dlg.FileName;
        try
        {
            MapImage.Source = new BitmapImage(new Uri(_customMapPath));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapWindow] Load map error: {ex.Message}");
            MessageBox.Show("Could not load the selected image.", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ═══════════════════════════════════════════════════════════════
    //  API sync — outgoing
    // ═══════════════════════════════════════════════════════════════

    private async Task PostStrokeAsync(Stroke stroke)
    {
        var factionId = _viewModel.SelectedFactionId;
        if (string.IsNullOrEmpty(factionId) || string.IsNullOrEmpty(_currentOrderId)) return;

        var points = stroke.StylusPoints
            .Select(p => CanvasToImage(new Point(p.X, p.Y)))
            .Select(ip => (ip.X, ip.Y))
            .ToList();

        var colorStr = stroke.DrawingAttributes.Color.ToString();
        var size = stroke.DrawingAttributes.Width;

        var result = await _apiService.AddMapStrokeAsync(factionId, _currentOrderId, colorStr, size, points);
        if (result != null)
        {
            Dispatcher.Invoke(() =>
            {
                if (DrawingCanvas.Strokes.Contains(stroke))
                    stroke.AddPropertyData(StrokeIdProperty, result.Id);
            });
        }
    }

    private async Task PostIconAsync(Border element, string iconType, Point canvasPos)
    {
        var factionId = _viewModel.SelectedFactionId;
        if (string.IsNullOrEmpty(factionId) || string.IsNullOrEmpty(_currentOrderId)) return;

        var imgPos = CanvasToImage(canvasPos);
        var result = await _apiService.AddMapIconAsync(factionId, _currentOrderId, iconType, imgPos.X, imgPos.Y);
        if (result != null)
        {
            Dispatcher.Invoke(() =>
            {
                if (IconsCanvas.Children.Contains(element))
                    element.Tag = new IconData(iconType, result.Id);
            });
        }
    }

    private async Task PutIconMoveAsync(FrameworkElement icon)
    {
        var iconData = icon.Tag as IconData;
        if (iconData?.ServerId == null) return;

        var factionId = _viewModel.SelectedFactionId;
        if (string.IsNullOrEmpty(factionId) || string.IsNullOrEmpty(_currentOrderId)) return;

        var canvasX = Canvas.GetLeft(icon);
        var canvasY = Canvas.GetTop(icon);
        var imgPos = CanvasToImage(new Point(canvasX, canvasY));
        await _apiService.MoveMapIconAsync(factionId, _currentOrderId, iconData.ServerId, imgPos.X, imgPos.Y);
    }

    // ═══════════════════════════════════════════════════════════════
    //  SignalR — incoming map updates from other clients
    // ═══════════════════════════════════════════════════════════════

    private void HandleMapUpdated(string factionId, string orderId, string action, JsonElement data)
    {
        // Ignore updates for other orders
        if (orderId != _currentOrderId) return;

        Dispatcher.Invoke(() =>
        {
            try
            {
                switch (action)
                {
                    case "StrokeAdded":   ApplyRemoteStrokeAdded(data);   break;
                    case "StrokeDeleted": ApplyRemoteStrokeDeleted(data); break;
                    case "IconAdded":     ApplyRemoteIconAdded(data);     break;
                    case "IconMoved":     ApplyRemoteIconMoved(data);     break;
                    case "IconDeleted":   ApplyRemoteIconDeleted(data);   break;
                    case "Cleared":       ClearCanvasInternal();          break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MapWindow] HandleMapUpdated error: {ex.Message}");
            }
        });
    }

    private void ApplyRemoteStrokeAdded(JsonElement data)
    {
        var id = data.GetProperty("id").GetString() ?? string.Empty;

        // Skip if we already have this stroke (echo of our own action)
        if (DrawingCanvas.Strokes.Any(s =>
            s.ContainsPropertyData(StrokeIdProperty) &&
            (string)s.GetPropertyData(StrokeIdProperty) == id)) return;

        var color = data.GetProperty("color").GetString() ?? "#FF0000";
        var size  = data.GetProperty("size").GetDouble();
        var canvasPoints = data.GetProperty("points").EnumerateArray()
            .Select(p => ImageToCanvas(new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())))
            .ToList();

        if (canvasPoints.Count == 0) return;

        var sc = new StylusPointCollection(canvasPoints.Select(p => new StylusPoint(p.X, p.Y)));
        var da = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString(color),
            Width = size, Height = size, FitToCurve = true
        };
        var stroke = new Stroke(sc, da);
        stroke.AddPropertyData(StrokeIdProperty, id);

        _suppressStrokeEvents = true;
        DrawingCanvas.Strokes.Add(stroke);
        _suppressStrokeEvents = false;
    }

    private void ApplyRemoteStrokeDeleted(JsonElement data)
    {
        // data is the raw strokeId string
        var strokeId = data.ValueKind == JsonValueKind.String
            ? data.GetString()
            : data.GetProperty("strokeId").GetString();
        if (string.IsNullOrEmpty(strokeId)) return;

        var toRemove = DrawingCanvas.Strokes.FirstOrDefault(s =>
            s.ContainsPropertyData(StrokeIdProperty) &&
            (string)s.GetPropertyData(StrokeIdProperty) == strokeId);

        if (toRemove == null) return;
        _suppressStrokeEvents = true;
        DrawingCanvas.Strokes.Remove(toRemove);
        _suppressStrokeEvents = false;
    }

    private void ApplyRemoteIconAdded(JsonElement data)
    {
        var id       = data.GetProperty("id").GetString() ?? string.Empty;
        var iconType = data.GetProperty("iconType").GetString() ?? "objective";
        var x        = data.GetProperty("x").GetDouble();
        var y        = data.GetProperty("y").GetDouble();

        // Skip if we already have an icon with this server ID (echo of our own action)
        if (IconsCanvas.Children.OfType<FrameworkElement>()
                .Any(el => (el.Tag as IconData)?.ServerId == id)) return;

        var cp = ImageToCanvas(new Point(x, y));
        PlaceIconAt(iconType, cp.X, cp.Y, id);
    }

    private void ApplyRemoteIconMoved(JsonElement data)
    {
        var id = data.GetProperty("id").GetString();
        var x  = data.GetProperty("x").GetDouble();
        var y  = data.GetProperty("y").GetDouble();

        var icon = IconsCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(el => (el.Tag as IconData)?.ServerId == id);
        if (icon == null) return;

        var cp = ImageToCanvas(new Point(x, y));
        Canvas.SetLeft(icon, cp.X);
        Canvas.SetTop(icon, cp.Y);
    }

    private void ApplyRemoteIconDeleted(JsonElement data)
    {
        var id = data.ValueKind == JsonValueKind.String
            ? data.GetString()
            : data.GetProperty("iconId").GetString();

        var icon = IconsCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(el => (el.Tag as IconData)?.ServerId == id);
        if (icon != null)
            IconsCanvas.Children.Remove(icon);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Win32 imports
    // ═══════════════════════════════════════════════════════════════

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
