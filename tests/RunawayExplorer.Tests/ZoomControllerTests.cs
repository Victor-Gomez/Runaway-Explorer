using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RunawayExplorer.Views;
using Xunit;

namespace RunawayExplorer.Tests;

public class ZoomControllerTests : UiTestBase
{
    [AvaloniaFact]
    public void Zoom100_ResetsZoomToExact100Percent()
    {
        var scroll = new ScrollViewer();
        var host = new LayoutTransformControl();
        var slider = new Slider { Minimum = 10, Maximum = 800, Value = 100 };
        var label = new TextBlock();

        var controller = new ZoomController(scroll, host, slider, label, () => new Size(640, 480));

        // Start by zooming in
        controller.In();
        Assert.True(controller.Zoom > 1.0);
        Assert.NotEqual(100.0, slider.Value);
        Assert.NotEqual("100%", label.Text);

        // Click 100%
        controller.Zoom100();
        Assert.Equal(1.0, controller.Zoom);
        Assert.Equal(100.0, slider.Value);
        Assert.Equal("100%", label.Text);
        Assert.False(controller.FitToWindow);

        // Zoom out
        controller.Out();
        Assert.True(controller.Zoom < 1.0);
        Assert.NotEqual(100.0, slider.Value);

        // Click 100% again
        controller.Zoom100();
        Assert.Equal(1.0, controller.Zoom);
        Assert.Equal(100.0, slider.Value);
        Assert.Equal("100%", label.Text);
        Assert.False(controller.FitToWindow);
    }
}
