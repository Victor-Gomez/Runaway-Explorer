using System.Buffers.Binary;
using System.Text;
using RunawayExplorer.Core.FileSystem;
using Xunit;

namespace RunawayExplorer.Core.Tests;

public class PhraseArchiveTests
{
    private static readonly Encoding Windows1252;

    static PhraseArchiveTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    private static byte[] BuildSyntheticPhraseArchive((uint Id, string Text)[] entries)
    {
        uint count = (uint)entries.Length;
        int totalSize = 4 + (int)count * 4 + (int)count * PhraseArchive.RecordSize;
        byte[] buffer = new byte[totalSize];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), count);

        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4 + i * 4, 4), entries[i].Id);
        }

        int dataStart = 4 + (int)count * 4;
        for (int i = 0; i < entries.Length; i++)
        {
            int recOff = dataStart + i * PhraseArchive.RecordSize;
            byte[] textBytes = Windows1252.GetBytes(entries[i].Text);
            byte[] plain = new byte[PhraseArchive.RecordSize];
            Array.Copy(textBytes, plain, Math.Min(textBytes.Length, PhraseArchive.RecordSize - 1));
            // plain[textBytes.Length] is 0 (NUL terminator)

            // Encode with index seed and delta XOR
            byte seed = (byte)(i & 0xFF);
            buffer[recOff] = (byte)(plain[0] ^ seed);
            for (int j = 1; j < PhraseArchive.RecordSize; j++)
            {
                buffer[recOff + j] = (byte)(plain[j] ^ buffer[recOff + j - 1]);
            }
        }

        return buffer;
    }

    [Fact]
    public void IsPhraseArchive_ValidBuffer_ReturnsTrue()
    {
        var entries = new (uint Id, string Text)[]
        {
            (100, "That won't work."),
            (110, "I doubt that would work."),
        };
        // Needs at least 100 entries for IsPhraseArchive size threshold:
        var manyEntries = new List<(uint Id, string Text)>();
        for (int i = 0; i < 150; i++)
            manyEntries.Add(((uint)(100 + i * 10), $"Phrase number {i}"));

        byte[] archiveBytes = BuildSyntheticPhraseArchive(manyEntries.ToArray());
        using var ms = new MemoryStream(archiveBytes);

        Assert.True(PhraseArchive.IsPhraseArchive(ms));
    }

    [Fact]
    public void ReadPhrases_DecodesSyntheticStringsAccurately()
    {
        var entries = new (uint Id, string Text)[]
        {
            (100, "That won't work."),
            (110, "I doubt that would work."),
            (200, "¡Hola! ¿Cómo estás?"),
            (300, "El niño vio el árbol."),
            (400, "Special symbols: € © ® ™ - all safe"),
        };

        byte[] archiveBytes = BuildSyntheticPhraseArchive(entries);
        using var ms = new MemoryStream(archiveBytes);

        List<PhraseArchive.Phrase> phrases = PhraseArchive.ReadPhrases(ms);

        Assert.Equal(entries.Length, phrases.Count);
        for (int i = 0; i < entries.Length; i++)
        {
            Assert.Equal(i, phrases[i].Index);
            Assert.Equal(entries[i].Id, phrases[i].Id);
            Assert.Equal(entries[i].Text, phrases[i].Text);
        }
    }

    [Fact]
    public void ReadSinglePhrase_DirectSeeking_MatchesFullRead()
    {
        var entries = new (uint Id, string Text)[]
        {
            (100, "Line 0"),
            (110, "Line 1"),
            (120, "Line 2"),
            (130, "Line 3"),
        };

        byte[] archiveBytes = BuildSyntheticPhraseArchive(entries);
        using var ms = new MemoryStream(archiveBytes);

        PhraseArchive.Phrase? p2 = PhraseArchive.ReadSinglePhrase(ms, 2);

        Assert.NotNull(p2);
        Assert.Equal(2, p2.Index);
        Assert.Equal(120u, p2.Id);
        Assert.Equal("Line 2", p2.Text);
    }

    [Fact]
    public void Runaway2_SteamInstall_DecodesExpectedPhrases()
    {
        string path = @"F:\Games\Steam\steamapps\common\Runaway The Dream of the Turtle\Resource\RESOURCE.003";
        if (!File.Exists(path))
            return;

        using var fs = File.OpenRead(path);
        Assert.True(PhraseArchive.IsPhraseArchive(fs));

        List<PhraseArchive.Phrase> phrases = PhraseArchive.ReadPhrases(fs);
        Assert.Equal(10454, phrases.Count);

        Assert.Equal("That won't work.", phrases[0].Text);
        Assert.Equal("I doubt that would work.", phrases[1].Text);
        Assert.Equal("That won't do any good.", phrases[2].Text);
        Assert.Equal("That makes no sense.", phrases[3].Text);
        Assert.Equal("It's too large.", phrases[4].Text);
        Assert.Equal("No.", phrases[5].Text);
        Assert.Equal("Impossible.", phrases[6].Text);
    }

    [Fact]
    public void Runaway3_SteamInstall_DecodesSpanishPhrases()
    {
        string path = @"F:\Games\Steam\steamapps\common\Runaway A Twist Of Fate\Resource\RESOURCE.003";
        if (!File.Exists(path))
            return;

        using var fs = File.OpenRead(path);
        Assert.True(PhraseArchive.IsPhraseArchive(fs));

        List<PhraseArchive.Phrase> phrases = PhraseArchive.ReadPhrases(fs);
        Assert.Equal(7228, phrases.Count);

        Assert.Equal("Otra cosa...", phrases[0].Text);
        Assert.Equal("Pero...", phrases[1].Text);
        Assert.Equal("Y...", phrases[2].Text);
        Assert.Equal("Vamos a ver...", phrases[3].Text);
        Assert.Equal("Voy.", phrases[4].Text);
        Assert.Equal("Iré a ver.", phrases[5].Text);
    }
}
