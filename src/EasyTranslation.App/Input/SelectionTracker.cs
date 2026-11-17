using EasyTranslation.Interop;

namespace EasyTranslation.Input;

public enum SelectionKind
{
    Drag,
    DoubleClick,
    TripleClick,
}

public readonly record struct SelectionSnapshot(
    SelectionKind Kind,
    Rectangle Bounds,
    IntPtr Window,
    DateTime CreatedAt);

/// <summary>
/// Tracks the last mouse selection without UI Automation. A selection is only reported for a
/// right click that lands inside its bounding box and inside the same foreground window.
/// </summary>
public sealed class SelectionTracker
{
    private const int DragThreshold = 4;
    private const int WordBoxWidth = 240;
    private const int WordBoxHeight = 30;

    private bool _leftDown;
    private bool _dragging;
    private Point _downPoint;
    private int _clickCount;
    private DateTime _lastDownAt = DateTime.MinValue;
    private Point _lastDownPoint;
    private SelectionSnapshot? _current;

    public TimeSpan MaxAge { get; set; } = TimeSpan.FromMinutes(5);

    public SelectionSnapshot? Current => _current;

    public void OnLeftDown(Point point, DateTime now)
    {
        _leftDown = true;
        _dragging = false;
        _downPoint = point;

        var doubleClickTicks = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime());
        var near = Math.Abs(point.X - _lastDownPoint.X) <= DragThreshold &&
                   Math.Abs(point.Y - _lastDownPoint.Y) <= DragThreshold;
        _clickCount = now - _lastDownAt <= doubleClickTicks && near ? Math.Min(_clickCount + 1, 3) : 1;
        _lastDownAt = now;
        _lastDownPoint = point;

        if (_current is { } selection && !selection.Bounds.Contains(point))
            _current = null;
    }

    public void OnMove(Point point)
    {
        if (!_leftDown || _dragging) return;
        if (Math.Abs(point.X - _downPoint.X) > DragThreshold || Math.Abs(point.Y - _downPoint.Y) > DragThreshold)
            _dragging = true;
    }

    public SelectionSnapshot? OnLeftUp(Point point, IntPtr window, DateTime now)
    {
        _leftDown = false;

        if (_dragging)
        {
            var rect = Rectangle.FromLTRB(
                Math.Min(_downPoint.X, point.X),
                Math.Min(_downPoint.Y, point.Y),
                Math.Max(_downPoint.X, point.X),
                Math.Max(_downPoint.Y, point.Y));
            if (rect.Width < DragThreshold && rect.Height < DragThreshold)
            {
                _current = null;
                return null;
            }

            // A normal single-line selection is a nearly horizontal drag (height 1-2 px).
            // Inflate it and give it a text-line worth of height so the right click still lands inside.
            var bounds = Rectangle.Inflate(rect, 4, 4);
            if (bounds.Height < 20)
                bounds = new Rectangle(bounds.Left, bounds.Top + bounds.Height / 2 - 10, bounds.Width, 20);

            _current = new SelectionSnapshot(SelectionKind.Drag, bounds, window, now);
            return _current;
        }

        if (_clickCount >= 2)
        {
            var width = _clickCount == 2 ? WordBoxWidth : WordBoxWidth * 2;
            var rect = new Rectangle(point.X - width / 2, point.Y - WordBoxHeight / 2, width, WordBoxHeight);
            _current = new SelectionSnapshot(
                _clickCount == 2 ? SelectionKind.DoubleClick : SelectionKind.TripleClick, rect, window, now);
            return _current;
        }

        _current = null;
        return null;
    }

    public SelectionSnapshot? ForRightClick(Point point, IntPtr window, DateTime now)
    {
        if (_current is not { } selection) return null;
        if (now - selection.CreatedAt > MaxAge)
        {
            _current = null;
            return null;
        }

        if (selection.Window != window) return null;
        return selection.Bounds.Contains(point) ? selection : null;
    }

    public SelectionSnapshot? Latest(DateTime now) =>
        _current is { } selection && now - selection.CreatedAt <= MaxAge ? selection : null;

    public void Clear() => _current = null;
}
