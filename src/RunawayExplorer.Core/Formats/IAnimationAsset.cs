namespace RunawayExplorer.Core.Formats;

/// <summary>
/// What the animation viewer and the APNG writer need from an animation, whatever format it came from.
/// <para>
/// Two unrelated codecs produce animations in these games: the scene archives' segment streams
/// (<see cref="SpriteAsset"/>) and <em>Runaway 1</em>'s character sprite library
/// (<see cref="CharacterSpriteAsset"/>). They share nothing but this shape -- a count, a canvas, and a
/// frame decoded to a cropped image with the position it goes at -- which is all the viewer ever used.
/// </para>
/// <para>
/// A frame's (<c>X</c>, <c>Y</c>) is in the same space as <see cref="Bounds"/>: absolute screen
/// coordinates for scene sprites, canvas coordinates for the character library. Either way, blitting
/// every frame at its own position onto one surface the size of <see cref="Bounds"/> lines them up.
/// </para>
/// </summary>
public interface IAnimationAsset
{
    /// <summary>How many frames the animation has.</summary>
    int FrameCount { get; }

    /// <summary>The union of every frame's box: the natural canvas for reassembly.</summary>
    (int X, int Y, int Width, int Height) Bounds { get; }

    /// <summary>Decodes one frame, cropped to its own box, with the position that box sits at.</summary>
    SpriteFrame DecodeFrame(int index);

    /// <summary>A description of the first structural problem found, or <see langword="null"/> when the asset walks clean.</summary>
    string? Verify();
}
