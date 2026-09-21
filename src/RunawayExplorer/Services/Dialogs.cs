using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using RunawayExplorer.Views;

namespace RunawayExplorer.Services;

/// <summary>Mirrors WPF's <c>MessageBoxButton</c> enum shape so call sites read the same.</summary>
public enum MessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }

/// <summary>Mirrors WPF's <c>MessageBoxImage</c> enum shape so call sites read the same.</summary>
public enum MessageBoxImage { None, Information, Warning, Error, Question }

/// <summary>Mirrors WPF's <c>MessageBoxResult</c> enum shape so call sites read the same.</summary>
public enum MessageBoxResult { None, OK, Cancel, Yes, No }

/// <summary>
/// Avalonia replacements for the WPF-only dialog/clipboard APIs the app used to call directly
/// (<c>System.Windows.MessageBox</c>, <c>Microsoft.Win32.OpenFileDialog</c>/<c>SaveFileDialog</c>/
/// <c>OpenFolderDialog</c>, <c>System.Windows.Clipboard</c>). Avalonia's dialog and clipboard APIs are
/// all <see cref="Task"/>-based (there's no synchronous "block until closed" equivalent), so every call
/// site becomes <see langword="await"/>.
/// </summary>
public static class Dialogs
{
    public static Task<MessageBoxResult> ShowMessageBox(
        Window owner,
        string message,
        string title,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var window = new MessageBoxWindow(message, title, button, icon);
        return window.ShowDialog<MessageBoxResult>(owner);
    }

    /// <summary>Opens a folder picker. Returns the chosen absolute path, or <see langword="null"/> if cancelled.</summary>
    public static async Task<string?> ShowOpenFolderDialog(Visual owner, string title, string? suggestedStartDirectory = null)
    {
        IStorageProvider? provider = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (provider is null)
            return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await TryGetStartFolder(provider, suggestedStartDirectory),
        };

        var result = await provider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>Opens a file picker. Returns the chosen absolute path, or <see langword="null"/> if cancelled.</summary>
    public static async Task<string?> ShowOpenFileDialog(
        Visual owner,
        string title,
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string? suggestedStartDirectory = null)
    {
        IStorageProvider? provider = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (provider is null)
            return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            FileTypeFilter = fileTypes,
            SuggestedStartLocation = await TryGetStartFolder(provider, suggestedStartDirectory),
        };

        var result = await provider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    /// <summary>Opens a save-file picker. Returns the chosen absolute path, or <see langword="null"/> if cancelled.</summary>
    public static async Task<string?> ShowSaveFileDialog(
        Visual owner,
        string title,
        string? suggestedFileName = null,
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string? suggestedStartDirectory = null)
    {
        IStorageProvider? provider = TopLevel.GetTopLevel(owner)?.StorageProvider;
        if (provider is null)
            return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = fileTypes,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await TryGetStartFolder(provider, suggestedStartDirectory),
        };

        IStorageFile? file = await provider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public static Task SetClipboardTextAsync(Visual owner, string text)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        return clipboard is null ? Task.CompletedTask : clipboard.SetTextAsync(text);
    }

    /// <summary>Copies a decoded image to the clipboard as PNG and DIBV5.</summary>
    public static async Task SetClipboardImageAsync(Visual owner, RunawayExplorer.Core.Formats.DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            if (TrySetWindowsClipboardImage(image))
                return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard is not null)
        {
            var dataObject = new Avalonia.Input.DataObject();
            byte[] pngBytes = RunawayExplorer.Core.Formats.PngWriter.ToBytes(image);
            dataObject.Set("PNG", pngBytes);
            await clipboard.SetDataObjectAsync(dataObject);
        }
    }

    private static bool TrySetWindowsClipboardImage(RunawayExplorer.Core.Formats.DecodedImage image)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
                return false;

            try
            {
                EmptyClipboard();

                // 1. Write PNG format
                byte[] pngBytes = RunawayExplorer.Core.Formats.PngWriter.ToBytes(image);
                uint pngFormat = RegisterClipboardFormat("PNG");
                if (pngFormat != 0)
                {
                    IntPtr hPng = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)pngBytes.Length);
                    if (hPng != IntPtr.Zero)
                    {
                        IntPtr ptr = GlobalLock(hPng);
                        if (ptr != IntPtr.Zero)
                        {
                            System.Runtime.InteropServices.Marshal.Copy(pngBytes, 0, ptr, pngBytes.Length);
                            GlobalUnlock(hPng);
                            SetClipboardData(pngFormat, hPng);
                        }
                        else
                        {
                            GlobalFree(hPng);
                        }
                    }
                }

                // 2. Write CF_DIBV5 format (bottom-up BGRA32)
                const int headerSize = 124; // sizeof(BITMAPV5HEADER)
                int pixelBytes = image.Width * image.Height * 4;
                int totalSize = headerSize + pixelBytes;
                IntPtr hDib = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)totalSize);
                if (hDib != IntPtr.Zero)
                {
                    IntPtr ptr = GlobalLock(hDib);
                    if (ptr != IntPtr.Zero)
                    {
                        byte[] buffer = new byte[totalSize];
                        using var ms = new MemoryStream(buffer);
                        using var writer = new BinaryWriter(ms);

                        // BITMAPV5HEADER
                        writer.Write((uint)headerSize);       // bV5Size
                        writer.Write(image.Width);            // bV5Width
                        writer.Write(image.Height);           // bV5Height (positive = bottom-up)
                        writer.Write((ushort)1);              // bV5Planes
                        writer.Write((ushort)32);             // bV5BitCount
                        writer.Write((uint)3);                // bV5Compression = BI_BITFIELDS
                        writer.Write((uint)pixelBytes);       // bV5SizeImage
                        writer.Write(0);                      // bV5XPelsPerMeter
                        writer.Write(0);                      // bV5YPelsPerMeter
                        writer.Write((uint)0);                // bV5ClrUsed
                        writer.Write((uint)0);                // bV5ClrImportant
                        writer.Write((uint)0x00FF0000);       // bV5RedMask
                        writer.Write((uint)0x0000FF00);       // bV5GreenMask
                        writer.Write((uint)0x000000FF);       // bV5BlueMask
                        writer.Write(0xFF000000);             // bV5AlphaMask
                        writer.Write((uint)0x73524742);       // bV5CSType = 'sRGB'
                        // CIEXYZTRIPLE: 36 bytes of zeros
                        for (int i = 0; i < 9; i++) writer.Write((uint)0);
                        writer.Write((uint)0);                // bV5GammaRed
                        writer.Write((uint)0);                // bV5GammaGreen
                        writer.Write((uint)0);                // bV5GammaBlue
                        writer.Write((uint)4);                // bV5Intent = LCS_GM_IMAGES
                        writer.Write((uint)0);                // bV5ProfileData
                        writer.Write((uint)0);                // bV5ProfileSize
                        writer.Write((uint)0);                // bV5Reserved

                        // Flip rows to bottom-up order
                        int stride = image.Width * 4;
                        for (int y = 0; y < image.Height; y++)
                        {
                            int srcOffset = (image.Height - 1 - y) * stride;
                            Buffer.BlockCopy(image.Pixels, srcOffset, buffer, headerSize + y * stride, stride);
                        }

                        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, ptr, totalSize);
                        GlobalUnlock(hDib);
                        const uint CF_DIBV5 = 17;
                        SetClipboardData(CF_DIBV5, hDib);
                    }
                    else
                    {
                        GlobalFree(hDib);
                    }
                }

                return true;
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            Log.Exception("Clipboard image copy", ex);
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    private static Task<IStorageFolder?> TryGetStartFolder(IStorageProvider provider, string? directory) =>
        string.IsNullOrEmpty(directory) ? Task.FromResult<IStorageFolder?>(null) : provider.TryGetFolderFromPathAsync(directory);
}
