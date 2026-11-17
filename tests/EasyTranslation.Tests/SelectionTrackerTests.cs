using System.Drawing;
using EasyTranslation.Input;
using Xunit;

namespace EasyTranslation.Tests;

public class SelectionTrackerTests
{
    private static readonly IntPtr Win = new(0x1234);
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);

    private static SelectionTracker DragSelection()
    {
        var tracker = new SelectionTracker();
        tracker.OnLeftDown(new Point(100, 100), T0);
        tracker.OnMove(new Point(120, 120));
        tracker.OnLeftUp(new Point(300, 220), Win, T0.AddMilliseconds(500));
        return tracker;
    }

    [Fact]
    public void DragSelection_IsReportedInsideItsBox()
    {
        var tracker = DragSelection();

        var snapshot = tracker.Current;
        Assert.NotNull(snapshot);
        Assert.Equal(SelectionKind.Drag, snapshot!.Value.Kind);

        Assert.NotNull(tracker.ForRightClick(new Point(200, 150), Win, T0.AddSeconds(1)));
        Assert.Null(tracker.ForRightClick(new Point(400, 150), Win, T0.AddSeconds(1)));
        Assert.Null(tracker.ForRightClick(new Point(200, 150), new IntPtr(99), T0.AddSeconds(1)));
    }

    [Fact]
    public void NearlyHorizontalDrag_StillMakesASelection()
    {
        var tracker = new SelectionTracker();
        tracker.OnLeftDown(new Point(100, 100), T0);
        tracker.OnMove(new Point(200, 101));
        var snapshot = tracker.OnLeftUp(new Point(300, 101), Win, T0.AddMilliseconds(300));

        Assert.NotNull(snapshot);
        Assert.Equal(SelectionKind.Drag, snapshot!.Value.Kind);
        Assert.True(snapshot.Value.Bounds.Height >= 20);
        Assert.NotNull(tracker.ForRightClick(new Point(200, 104), Win, T0.AddSeconds(1)));
    }

    [Fact]
    public void PlainClick_HasNoSelection()
    {
        var tracker = new SelectionTracker();
        tracker.OnLeftDown(new Point(50, 50), T0);

        Assert.Null(tracker.OnLeftUp(new Point(50, 50), Win, T0.AddMilliseconds(100)));
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void DoubleClick_SelectsAWordBox()
    {
        var tracker = new SelectionTracker();
        tracker.OnLeftDown(new Point(200, 200), T0);
        tracker.OnLeftUp(new Point(200, 200), Win, T0.AddMilliseconds(40));
        tracker.OnLeftDown(new Point(202, 201), T0.AddMilliseconds(160));
        var snapshot = tracker.OnLeftUp(new Point(202, 201), Win, T0.AddMilliseconds(200));

        Assert.NotNull(snapshot);
        Assert.Equal(SelectionKind.DoubleClick, snapshot!.Value.Kind);
        Assert.Equal(240, snapshot.Value.Bounds.Width);
        Assert.NotNull(tracker.ForRightClick(new Point(210, 205), Win, T0.AddMilliseconds(300)));
        Assert.Null(tracker.ForRightClick(new Point(330, 205), Win, T0.AddMilliseconds(300)));
    }

    [Fact]
    public void TripleClick_UsesAWiderBox()
    {
        var tracker = new SelectionTracker();
        tracker.OnLeftDown(new Point(300, 300), T0);
        tracker.OnLeftUp(new Point(300, 300), Win, T0.AddMilliseconds(30));
        tracker.OnLeftDown(new Point(301, 300), T0.AddMilliseconds(120));
        tracker.OnLeftUp(new Point(301, 300), Win, T0.AddMilliseconds(150));
        tracker.OnLeftDown(new Point(300, 301), T0.AddMilliseconds(240));
        var snapshot = tracker.OnLeftUp(new Point(300, 301), Win, T0.AddMilliseconds(270));

        Assert.NotNull(snapshot);
        Assert.Equal(SelectionKind.TripleClick, snapshot!.Value.Kind);
        Assert.Equal(480, snapshot.Value.Bounds.Width);
    }

    [Fact]
    public void ClickingElsewhere_ClearsTheSelection()
    {
        var tracker = DragSelection();

        tracker.OnLeftDown(new Point(600, 600), T0.AddSeconds(1));

        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Selection_ExpiresAfterMaxAge()
    {
        var tracker = DragSelection();

        Assert.Null(tracker.ForRightClick(new Point(200, 150), Win, T0.AddMinutes(6)));
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Latest_ReturnsTheLastSelectionWithinMaxAge()
    {
        var tracker = DragSelection();

        Assert.NotNull(tracker.Latest(T0.AddSeconds(2)));
        Assert.Null(tracker.Latest(T0.AddMinutes(10)));
    }
}
