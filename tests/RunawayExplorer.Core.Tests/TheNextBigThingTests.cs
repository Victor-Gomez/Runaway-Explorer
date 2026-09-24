using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Settings;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class TheNextBigThingTests
{
    public const string TnbtSteamDir = @"F:\Games\Steam\steamapps\common\The Next BIG Thing";

    [Fact]
    public void GameDetector_DetectsTheSignature()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "The Next Big Thing.exe"), "dummy");
            Assert.Equal(GameVersion.TheNextBigThing, GameDetector.Detect(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GameDetector_DetectsARealInstall()
    {
        if (Directory.Exists(TnbtSteamDir))
            Assert.Equal(GameVersion.TheNextBigThing, GameDetector.Detect(TnbtSteamDir));
    }

    [Fact]
    public void AppSettings_ManagesTheTnbtPath()
    {
        var settings = new AppSettings
        {
            TheNextBigThingDir = @"C:\Games\TNBT",
            ActiveGame = GameVersion.TheNextBigThing
        };

        Assert.Equal(@"C:\Games\TNBT", settings.GetGameDir(GameVersion.TheNextBigThing));
        Assert.Equal(@"C:\Games\TNBT", settings.ActiveGameDir);

        settings.SetGameDir(GameVersion.TheNextBigThing, @"D:\TNBT");
        Assert.Equal(@"D:\TNBT", settings.GetGameDir(GameVersion.TheNextBigThing));
    }

    [Fact]
    public void SceneArchive_RecognizesTnbtNames()
    {
        // Scene archives
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.A00", GameVersion.TheNextBigThing));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.B04", GameVersion.TheNextBigThing));
        Assert.True(SceneArchive.IsSceneArchiveName("RESOURCE.SP1", GameVersion.TheNextBigThing));

        // Non-scene archives
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.TAB", GameVersion.TheNextBigThing));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.IFZ", GameVersion.TheNextBigThing));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.003", GameVersion.TheNextBigThing));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.004", GameVersion.TheNextBigThing));
        Assert.False(SceneArchive.IsSceneArchiveName("RESOURCE.S00", GameVersion.TheNextBigThing)); // Audio
    }

    [Fact]
    public void RealInstall_CanLoadAndScan()
    {
        if (!Directory.Exists(TnbtSteamDir))
            return;

        var cache = ScanCache.Ephemeral();
        var vfs = VirtualFileSystem.Init(TnbtSteamDir, cache: cache);
        Assert.Equal(GameVersion.TheNextBigThing, vfs.GameVersion);
        Assert.True(vfs.Summary.SceneArchives > 0);
        Assert.True(vfs.Summary.Backgrounds > 0);
        Assert.True(vfs.Summary.Videos > 0);
        Assert.True(vfs.Summary.VoiceClips > 0);

        // Second scan with same cache must hit cache
        var vfs2 = VirtualFileSystem.Init(TnbtSteamDir, cache: cache);
        Assert.True(vfs2.Summary.FromCache);
        Assert.Equal(vfs.Summary.SceneArchives, vfs2.Summary.SceneArchives);
        Assert.Equal(vfs.Summary.Backgrounds, vfs2.Summary.Backgrounds);
    }

    [Fact]
    public void ResourceD02_ClassifiesAndDecodesCorrectly()
    {
        if (!Directory.Exists(TnbtSteamDir))
            return;

        var vfs = VirtualFileSystem.Init(TnbtSteamDir, cache: ScanCache.Ephemeral());
        var d02 = vfs.Root.Children
            .FirstOrDefault(c => c.Name == VirtualFileSystem.ScenesFolder)?
            .Children.FirstOrDefault(c => c.Name.Equals("RESOURCE.D02", StringComparison.OrdinalIgnoreCase));
        if (d02 is null)
            return;

        var e00 = d02.Children.FirstOrDefault(c => c.EntryIndex == 0);
        Assert.NotNull(e00);
        Assert.Equal(EntryKind.Background, e00.Kind);
        Assert.Equal(1920, e00.Image?.Width);
    }
}
