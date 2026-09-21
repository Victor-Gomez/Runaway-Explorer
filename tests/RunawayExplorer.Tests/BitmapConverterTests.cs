using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RunawayExplorer.Core.Formats;
using RunawayExplorer.Core.Settings;
using RunawayExplorer.Services;
using Xunit;

namespace RunawayExplorer.Tests;

public class BitmapConverterTests
{
    [AvaloniaFact]
    public void ToBitmap_NullOrEmptyImage_ReturnsNull()
    {
        Assert.Null(BitmapConverter.ToBitmap(null));
        Assert.Null(BitmapConverter.ToBitmap(new DecodedImage(0, 0, [])));
    }

    [AvaloniaFact]
    public void ToBitmap_ColorConversion_PreservesPixels()
    {
        // 1x1 image: B=50, G=100, R=200, A=255
        byte[] pixels = [50, 100, 200, 255];
        var image = new DecodedImage(1, 1, pixels);

        Bitmap? bmp = BitmapConverter.ToBitmap(image, grayscale: false);
        Assert.NotNull(bmp);
        var wb = Assert.IsType<WriteableBitmap>(bmp);

        using ILockedFramebuffer fb = wb.Lock();
        byte[] readBack = new byte[4];
        Marshal.Copy(fb.Address, readBack, 0, 4);

        Assert.Equal(50, readBack[0]);  // B
        Assert.Equal(100, readBack[1]); // G
        Assert.Equal(200, readBack[2]); // R
        Assert.Equal(255, readBack[3]); // A
    }

    [AvaloniaFact]
    public void ToBitmap_GrayscaleConversion_AppliesLuma()
    {
        // 1x1 image: B=50, G=100, R=200, A=255
        // Expected luma: (200 * 77 + 100 * 150 + 50 * 29) >> 8 = (15400 + 15000 + 1450) >> 8 = 31850 >> 8 = 124
        byte[] pixels = [50, 100, 200, 255];
        var image = new DecodedImage(1, 1, pixels);

        Bitmap? bmp = BitmapConverter.ToBitmap(image, grayscale: true);
        Assert.NotNull(bmp);
        var wb = Assert.IsType<WriteableBitmap>(bmp);

        using ILockedFramebuffer fb = wb.Lock();
        byte[] readBack = new byte[4];
        Marshal.Copy(fb.Address, readBack, 0, 4);

        byte expectedGray = (byte)((200 * 77 + 100 * 150 + 50 * 29) >> 8);
        Assert.Equal(expectedGray, readBack[0]); // B
        Assert.Equal(expectedGray, readBack[1]); // G
        Assert.Equal(expectedGray, readBack[2]); // R
        Assert.Equal(255, readBack[3]);          // A remains unchanged
    }

    [Fact]
    public void AppSettings_BackgroundMode_And_ShowOnBackground_Sync()
    {
        var settings = new AppSettings();
        Assert.Equal("yes", settings.BackgroundMode);
        Assert.True(settings.ShowOnBackground);

        settings.BackgroundMode = "greyed";
        Assert.True(settings.ShowOnBackground);

        settings.BackgroundMode = "no";
        Assert.False(settings.ShowOnBackground);

        settings.ShowOnBackground = true;
        Assert.Equal("yes", settings.BackgroundMode);

        settings.BackgroundMode = "greyed";
        settings.ShowOnBackground = true;
        Assert.Equal("greyed", settings.BackgroundMode);

        settings.ShowOnBackground = false;
        Assert.Equal("no", settings.BackgroundMode);
    }
}
