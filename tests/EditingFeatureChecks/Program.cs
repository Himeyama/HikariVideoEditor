using VideoEditor;
using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Near(double actual, double expected, double tolerance = 1e-6) => Check(Math.Abs(actual - expected) < tolerance, $"{actual} != {expected}");
static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
EditClip Caption(double start, double duration, int layer = 1) => new() { Kind = "subtitle", Text = "test", Start = start, Duration = duration, Layer = layer };
var removed = Caption(2, 2);
var later = Caption(5, 2);
var other = Caption(5, 2, 2);
var project = new EditProject { Clips = [removed, later, other] };
var ripple = TimelineOperations.Delete(project, new HashSet<string> { removed.Id }, true);
Near(ripple.Clips[0].Start, 3); Near(ripple.Clips[1].Start, 5); Near(project.Clips[1].Start, 5);
var regular = TimelineOperations.Delete(project, new HashSet<string> { removed.Id }, false);
Near(regular.Clips[0].Start, 5);
project.Layers[1] = new() { Locked = true };
Reject(() => TimelineOperations.Delete(project, new HashSet<string> { removed.Id }, true));
Reject(() => TimelineOperations.CloseGaps(project));
project.Layers[1].Locked = false;
var closed = TimelineOperations.CloseGaps(project);
Near(closed.Clips[0].Start, 0); Near(closed.Clips[1].Start, 2); Near(closed.Clips[2].Start, 2);
project.Clips.Add(Caption(3, 2));
Reject(() => TimelineOperations.Delete(project, new HashSet<string> { removed.Id }, true));
project.Layers[2] = new() { Locked = true, Muted = true, Hidden = true };
var restored = project.Copy(); restored.Validate();
Check(restored.Layer(2).Locked && restored.Layer(2).Muted && restored.Layer(2).Hidden, "Layer persistence");
Check(!new EditProject().Layer(1).Locked, "Legacy defaults");
Console.WriteLine("PASS: ripple/regular delete, gap closing, overlapping clips rejected, lock protection, persistence, legacy defaults");

var directory = Path.GetFullPath("artifacts/editing-features"); Directory.CreateDirectory(directory);
string input = Path.Combine(directory, "tone.wav");
const int rate = 48000, frames = rate * 2;
using (var writer = new BinaryWriter(File.Create(input)))
{
    writer.Write("RIFF"u8); writer.Write(frames * 4 + 36); writer.Write("WAVEfmt "u8);
    writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(rate); writer.Write(rate * 4);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(frames * 4);
    for (int frame = 0; frame < frames; frame++)
    {
        short sample = (short)(Math.Sin(frame * 440 * 2 * Math.PI / rate) * 16384);
        writer.Write(sample); writer.Write(sample);
    }
}
var clip = new EditClip { Path = input, Kind = "audio", Duration = 2, FadeIn = .5, FadeOut = .5 };
clip.Validate(); Near(clip.AudioGain(0), 0); Near(clip.AudioGain(.25), .5); Near(clip.AudioGain(1), 1); Near(clip.AudioGain(1.75), .5);
Near(ClipEditing.Duplicate(clip).FadeIn, .5);
var decoded = await AudioAnalysis.Get(input);
Near(decoded.Duration, 2, .02); Near(decoded.Peak(.5, 1), .5, .01);
var fadedPath = await AudioAnalysis.Faded(clip);
var faded = PcmAudio.Read(fadedPath);
Check(faded.Peak(0, .01) < .02 && faded.Peak(1.99, 2) < .02, "Fade endpoints");
Near(faded.Peak(.99, 1.01), .5, .02);
var audioProject = new EditProject { Clips = [clip], Video = new() { Width = 320, Height = 180 } };
var composition = await CompositionBuilder.Build(audioProject);
Check(composition.BackgroundAudioTracks.Count == 1, "Faded audio missing");
var output = await (await StorageFolder.GetFolderFromPathAsync(directory)).CreateFileAsync("faded.mp4", CreationCollisionOption.ReplaceExisting);
var result = await composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, CompositionBuilder.CreateEncodingProfile(audioProject));
Check(result == TranscodeFailureReason.None, "Fade export failed");
var exported = await AudioAnalysis.Get(output.Path);
Check(exported.Peak(0, .01) < .05 && exported.Peak(1.99, 2) < .05, "Export lost fades");
Check(exported.Peak(.9, 1.1) > .4, "Export lost audio");
audioProject.Layers[1] = new() { Muted = true };
Check((await CompositionBuilder.Build(audioProject)).BackgroundAudioTracks.Count == 0, "Muted audio exported");
audioProject.Layers[1] = new() { Hidden = true };
Check((await CompositionBuilder.Build(audioProject)).BackgroundAudioTracks.Count == 0, "Hidden audio exported");
var visualProject = new EditProject { Clips = [Caption(0, 2)], Layers = new() { [1] = new() { Hidden = true } } };
Check((await CompositionBuilder.Build(visualProject)).OverlayLayers.Count == 0, "Hidden layer rendered");
Check((await StillPreview.ReadLayers(visualProject, 1)).Count == 0, "Hidden paused layer rendered");
Console.WriteLine("PASS: WAV decode/peak, fade envelope, faded PCM, actual MP4 fade export, mute/hide in composition and paused preview");
var videoProject = new EditProject { Clips = [new EditClip { Kind = "video", Path = output.Path, Duration = 2, FadeIn = .2, FadeOut = .2 }] };
var videoComposition = await CompositionBuilder.Build(videoProject);
Check(videoComposition.OverlayLayers.Count == 1 && videoComposition.BackgroundAudioTracks.Count == 1, "Video fade audio missing");
Near(videoComposition.OverlayLayers[0].Overlays[0].Clip.Volume, 0);
videoProject.Layers[1] = new() { Muted = true };
var mutedVideo = await CompositionBuilder.Build(videoProject);
Check(mutedVideo.OverlayLayers.Count == 1 && mutedVideo.BackgroundAudioTracks.Count == 0, "Video mute changed picture");
videoProject.Layers[1] = new() { Hidden = true };
var hiddenVideo = await CompositionBuilder.Build(videoProject);
Check(hiddenVideo.OverlayLayers.Count == 0 && hiddenVideo.BackgroundAudioTracks.Count == 0, "Video hide left picture or audio");
Console.WriteLine("PASS: video fade has a single audio path; mute preserves picture; hide excludes picture and audio");
