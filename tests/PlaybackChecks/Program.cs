using VideoEditor;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.Graphics.Imaging;

var directory = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/playback");
Directory.CreateDirectory(directory);
var folder = await StorageFolder.GetFolderFromPathAsync(directory);
var fixture = new MediaComposition();
fixture.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 255, 0, 0), TimeSpan.FromSeconds(2)));
fixture.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 0, 255, 0), TimeSpan.FromSeconds(2)));
var video = await folder.CreateFileAsync("motion-test.mp4", CreationCollisionOption.ReplaceExisting);
var result = await fixture.RenderToFileAsync(video, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Wvga));
if (result != TranscodeFailureReason.None) throw new Exception($"Fixture render failed: {result}");
var project = new EditProject { Clips = [new EditClip { Path = video.Path, Kind = "video", Duration = 4 }] };
var composition = await CompositionBuilder.Build(project);
Console.WriteLine($"Composition profile: {composition.CreateDefaultEncodingProfile().Video.Width}x{composition.CreateDefaultEncodingProfile().Video.Height}");
var position = composition.OverlayLayers[0].Overlays[0].Position;
Console.WriteLine($"Overlay position: {position}");
foreach (var time in new[] { .5, 2.5 })
{
    using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(time), 320, 180, VideoFramePrecision.NearestFrame);
    var output = await folder.CreateFileAsync($"frame-{time:0.0}.jpg", CreationCollisionOption.ReplaceExisting);
    using var stream = await output.OpenAsync(FileAccessMode.ReadWrite);
    await RandomAccessStream.CopyAsync(thumbnail, stream);
}
var compositeVideo = await folder.CreateFileAsync("composited.mp4", CreationCollisionOption.ReplaceExisting);
var rendered = await composition.RenderToFileAsync(compositeVideo, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p));
if (rendered != TranscodeFailureReason.None) throw new Exception($"Composition render failed: {rendered}");
var decoded = new MediaComposition();
decoded.Clips.Add(await MediaClip.CreateFromFileAsync(compositeVideo));
foreach (var time in new[] { .5, 2.5 })
{
    using var thumbnail = await decoded.GetThumbnailAsync(TimeSpan.FromSeconds(time), 320, 180, VideoFramePrecision.NearestFrame);
    var decoder = await BitmapDecoder.CreateAsync(thumbnail);
    var pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    var pixels = pixelData.DetachPixelData();
    var center = (int)((decoder.PixelHeight / 2 * decoder.PixelWidth + decoder.PixelWidth / 2) * 4);
    var red = pixels[center]; var green = pixels[center + 1];
    if (time < 2 ? red < 150 || green > 80 : green < 150 || red > 80)
        throw new Exception($"Composited video frame at {time}s is wrong: R={red}, G={green}");
    thumbnail.Seek(0);
    var output = await folder.CreateFileAsync($"decoded-{time:0.0}.jpg", CreationCollisionOption.ReplaceExisting);
    using var stream = await output.OpenAsync(FileAccessMode.ReadWrite);
    await RandomAccessStream.CopyAsync(thumbnail, stream);
}
Console.WriteLine($"PASS: real H264 fixture and composition thumbnails generated in {directory}");
foreach (var time in new[] { .5, 2.5 })
{
    var frames = await StillPreview.ReadFrames(project, time);
    try
    {
        if (frames.Count != 1) throw new Exception("Missing paused frame");
        var bitmap = await BitmapDecoder.CreateAsync(frames[0]);
        var data = await bitmap.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();
        var center = (int)((bitmap.PixelHeight / 2 * bitmap.PixelWidth + bitmap.PixelWidth / 2) * 4);
        if (time < 2 ? pixels[center] < 150 || pixels[center + 1] > 80 : pixels[center + 1] < 150 || pixels[center] > 80)
            throw new Exception($"Wrong paused frame at {time}s");
    }
    finally { foreach (var frame in frames) frame.Dispose(); }
}
if ((await StillPreview.ReadFrames(project, 10)).Count != 0) throw new Exception("Gap should be black");
Console.WriteLine("PASS: paused preview at 0.5s is red, at 2.5s is green, gap has no visible clips");

{
    project.Clips[0].Scale = .5;
    project.Clips[0].OffsetX = 600;
    var transformedComposition = await CompositionBuilder.Build(project);
    var transformedFile = await folder.CreateFileAsync("transformed.mp4", CreationCollisionOption.ReplaceExisting);
    var transformedResult = await transformedComposition.RenderToFileAsync(transformedFile, MediaTrimmingPreference.Precise, CompositionBuilder.CreateEncodingProfile(project));
    if (transformedResult != TranscodeFailureReason.None) throw new Exception("Transform render failed");
    var transformedClip = await MediaClip.CreateFromFileAsync(transformedFile);
    var encoding = transformedClip.GetVideoEncodingProperties();
    if (encoding.Width != 1920 || encoding.Height != 1080) throw new Exception("Wrong output resolution");
    var decodedTransform = new MediaComposition();
    decodedTransform.Clips.Add(transformedClip);
    using var frame = await decodedTransform.GetThumbnailAsync(TimeSpan.FromSeconds(.5), 320, 180, VideoFramePrecision.NearestFrame);
    var bitmap = await BitmapDecoder.CreateAsync(frame);
    var data = await bitmap.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    var pixels = data.DetachPixelData();
    int center = (90 * 320 + 160) * 4;
    int right = (90 * 320 + 260) * 4;
    if (pixels[center] > 40 || pixels[center + 1] > 40 || pixels[right] < 150 || pixels[right + 1] > 80)
        throw new Exception("Export does not match the scaled and translated clip bounds");
    Console.WriteLine("PASS: transformed MP4 is 1920x1080, center is black and translated right side is red");
}

{
    var output = Path.Combine(directory, $"export-check-{Guid.NewGuid():N}.mp4");
    try
    {
        await VideoExport.Write(project, output, videoBitrate: 5_000_000);
        var exported = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(output));
        if (exported.GetVideoEncodingProperties().Width != 1920 || Math.Abs(exported.OriginalDuration.TotalSeconds - 4) > .1)
            throw new Exception("Exported video has wrong dimensions or duration");
        var originalBytes = await File.ReadAllBytesAsync(output);
        try
        {
            await VideoExport.Write(project, output);
            throw new Exception("Existing export was overwritten");
        }
        catch (ArgumentException) { }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await VideoExport.Write(project, output, overwrite: true, cancellationToken: cancellation.Token);
            throw new Exception("Cancelled export succeeded");
        }
        catch (OperationCanceledException) { }
        if (!originalBytes.SequenceEqual(await File.ReadAllBytesAsync(output)))
            throw new Exception("Cancelled export changed the existing destination");
        // Cancel when encoding actually starts and verify temporary-file cleanup.
        using var duringRender = new CancellationTokenSource();
        try
        {
            await VideoExport.Write(project, output, overwrite: true,
                progress: new CancelExportProgress(duringRender), cancellationToken: duringRender.Token);
            throw new Exception("Render cancellation was ignored");
        }
        catch (OperationCanceledException) { }
        if (!originalBytes.SequenceEqual(await File.ReadAllBytesAsync(output)))
            throw new Exception("Render cancellation changed the existing destination");
        if (Directory.GetFiles(directory, ".hikari-export-*.mp4").Length != 0)
            throw new Exception("Temporary export was not cleaned up");
        await VideoExport.Write(project, output, overwrite: true, videoBitrate: 10_000_000);
        Console.WriteLine("PASS: MP4 export, dimensions/duration, overwrite protection, cancellation before/during encoding, destination preservation and temporary cleanup");
    }
    finally
    {
        if (File.Exists(output)) File.Delete(output);
    }
}

{
    var captionProject = project.Copy();
    captionProject.Clips[0].Scale = 1;
    captionProject.Clips[0].OffsetX = 0;
    captionProject.Clips[0].Layer = 10;
    var caption = new EditClip
    {
        Kind = "subtitle", Text = "日本語テスト字幕", Start = 0, Duration = 1, Layer = 9,
        Subtitle = new SubtitleStyle { FontFamily = "Yu Gothic UI", FontSize = 80, MarginY = 80 }
    };
    captionProject.Clips.Add(caption);
    var path = SubtitleRenderer.SourcePath(caption, captionProject.Video);
    using (var png = new System.Drawing.Bitmap(path))
    {
        if (png.GetPixel(0, 0).A != 0) throw new Exception("Subtitle canvas is not transparent");
        if (caption.SubtitleBounds is not { } measured || measured.Width >= captionProject.Video.Width || measured.Height >= captionProject.Video.Height)
            throw new Exception("Subtitle image was not cropped to the text");
        if (caption.SubtitleBounds is not { Y: > 750, Height: < 250 })
            throw new Exception("Cropped subtitle lost its bottom placement");
        int visible = 0;
        for (int y = 0; y < png.Height; y++)
            for (int x = 0; x < png.Width; x++)
                if (png.GetPixel(x, y).A > 128) visible++;
        if (visible < 500) throw new Exception("Bottom-centered Japanese subtitle was not drawn");
    }
    var frames = await StillPreview.ReadLayers(captionProject, .5);
    if (frames.Count != 2 || frames[1].Clip.Kind != "subtitle") throw new Exception("Subtitle missing in paused preview");
    foreach (var layer in frames) layer.Stream.Dispose();
    var output = Path.Combine(directory, "subtitle-check.mp4");
    await VideoExport.Write(captionProject, output, overwrite: true);
    var decodedCaption = new MediaComposition();
    decodedCaption.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(output)));
    using var thumbnail = await decodedCaption.GetThumbnailAsync(TimeSpan.FromSeconds(.5), 960, 540, VideoFramePrecision.NearestFrame);
    var decoder = await BitmapDecoder.CreateAsync(thumbnail);
    var data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore,
        new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
    int white = 0;
    for (int y = 375; y < 510; y++)
        for (int x = 200; x < 760; x++)
        {
            int i = (y * 960 + x) * 4;
            if (data[i] > 170 && data[i + 1] > 170 && data[i + 2] > 170) white++;
        }
    if (white < 100) throw new Exception("Subtitle text missing from exported MP4");
    int sample = (100 * 960 + 480) * 4;
    if (data[sample] < 150 || data[sample + 1] > 80) throw new Exception("Subtitle obscures underlying video");
    using var after = await decodedCaption.GetThumbnailAsync(TimeSpan.FromSeconds(2.5), 320, 180, VideoFramePrecision.NearestFrame);
    var afterDecoder = await BitmapDecoder.CreateAsync(after);
    var afterData = (await afterDecoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore,
        new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
    for (int i = 0; i < afterData.Length; i += 4)
        if (afterData[i] > 170 && afterData[i + 1] > 170 && afterData[i + 2] > 170)
            throw new Exception("Subtitle remained after its end time");
    caption.Subtitle.Vertical = "top";
    caption.Subtitle.Horizontal = "left";
    using var top = new System.Drawing.Bitmap(SubtitleRenderer.SourcePath(caption, captionProject.Video));
    if (caption.SubtitleBounds is not { X: >= 70 and < 160, Y: >= 70 and < 200 })
        throw new Exception("Cropped subtitle lost its top-left placement");
    int topPixels = 0;
    for (int y = 0; y < top.Height; y++)
        for (int x = 0; x < top.Width; x++)
            if (top.GetPixel(x, y).A > 128) topPixels++;
    if (topPixels < 500) throw new Exception("Top-left subtitle position was ignored");
    Console.WriteLine("PASS: Japanese subtitles, transparent raster, bottom/center and top/left placement, paused preview, MP4 text/alpha/timing, upper layer priority");
}
sealed class CancelExportProgress(CancellationTokenSource cancellation) : IProgress<double>
{
    public void Report(double value) => cancellation.Cancel();
}



