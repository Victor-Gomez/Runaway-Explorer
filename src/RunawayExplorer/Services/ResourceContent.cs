using RunawayExplorer.Core.FileSystem;
using RunawayExplorer.Core.Formats;

namespace RunawayExplorer.Services;

/// <summary>
/// Discriminated result of loading an <see cref="FsNode"/> through <see cref="ResourceLoader"/>. The UI
/// switches on the concrete type to decide which viewer to show.
/// </summary>
public abstract record ResourceContent;

/// <summary>
/// One still image. <see cref="X"/>/<see cref="Y"/> is where its top-left sits on the game screen, which
/// only means something when <see cref="Positioned"/> (overlays); backgrounds fill the screen from 0,0.
/// </summary>
public sealed record ImageResource(DecodedImage Image, int X, int Y, bool Positioned, string Kind) : ResourceContent;

/// <summary>A sprite animation, parsed and ready for frame-by-frame decoding.</summary>
public sealed record AnimationResource(SpriteAsset Asset) : ResourceContent;

/// <summary>Plain monospace text: a hex dump, a viseme track, or an informational message.</summary>
public sealed record TextResource(string Text) : ResourceContent;

/// <summary>A playable sound, already wrapped as a WAV in a temp file.</summary>
public sealed record SoundResource(string TempFilePath, AudioInfo Pcm, double DurationSeconds) : ResourceContent;

/// <summary>A Bink video with its header restored, written to a temp <c>.bik</c> LibVLC can play directly.</summary>
public sealed record VideoResource(string TempFilePath) : ResourceContent;

/// <summary>
/// A scene archive selected as a whole: its background plus a summary of what it holds. The status
/// text carries the counts; the background is shown as a still.
/// </summary>
public sealed record SceneResource(DecodedImage? Background, string Summary, FsNode Archive) : ResourceContent;

/// <summary>Loading the resource failed; <see cref="Message"/> is shown to the user in the text panel.</summary>
public sealed record ErrorResource(string Message) : ResourceContent;
