using VideoEditor;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.Graphics.Imaging;

string directory = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/playback");
Directory.CreateDirectory(directory);
StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directory);
MediaComposition fixture = new();
fixture.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 255, 0, 0), TimeSpan.FromSeconds(2)));
fixture.Clips.Add(MediaClip.CreateFromColor(Color.FromArgb(255, 0, 255, 0), TimeSpan.FromSeconds(2)));
StorageFile video = await folder.CreateFileAsync("motion-test.mp4", CreationCollisionOption.ReplaceExisting);
TranscodeFailureReason result = await fixture.RenderToFileAsync(video, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Wvga));
if (result != TranscodeFailureReason.None) throw new Exception($"Fixture render failed: {result}");
EditProject project = new() { Clips = [new EditClip { Path = video.Path, Kind = "video", Duration = 4 }] };
MediaComposition composition = await CompositionBuilder.Build(project);
Console.WriteLine($"Composition profile: {composition.CreateDefaultEncodingProfile().Video.Width}x{composition.CreateDefaultEncodingProfile().Video.Height}");
Windows.Foundation.Rect position = composition.OverlayLayers[0].Overlays[0].Position;
Console.WriteLine($"Overlay position: {position}");
foreach (double time in new[] { .5, 2.5 })
{
    using ImageStream thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(time), 320, 180, VideoFramePrecision.NearestFrame);
    StorageFile output = await folder.CreateFileAsync($"frame-{time:0.0}.jpg", CreationCollisionOption.ReplaceExisting);
    using IRandomAccessStream stream = await output.OpenAsync(FileAccessMode.ReadWrite);
    await RandomAccessStream.CopyAsync(thumbnail, stream);
}
StorageFile compositeVideo = await folder.CreateFileAsync("composited.mp4", CreationCollisionOption.ReplaceExisting);
TranscodeFailureReason rendered = await composition.RenderToFileAsync(compositeVideo, MediaTrimmingPreference.Precise, MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p));
if (rendered != TranscodeFailureReason.None) throw new Exception($"Composition render failed: {rendered}");
MediaComposition decoded = new();
decoded.Clips.Add(await MediaClip.CreateFromFileAsync(compositeVideo));
foreach (double time in new[] { .5, 2.5 })
{
    using ImageStream thumbnail = await decoded.GetThumbnailAsync(TimeSpan.FromSeconds(time), 320, 180, VideoFramePrecision.NearestFrame);
    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(thumbnail);
    PixelDataProvider pixelData = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    byte[] pixels = pixelData.DetachPixelData();
    int center = (int)((decoder.PixelHeight / 2 * decoder.PixelWidth + decoder.PixelWidth / 2) * 4);
    byte red = pixels[center]; byte green = pixels[center + 1];
    if (time < 2 ? red < 150 || green > 80 : green < 150 || red > 80)
        throw new Exception($"Composited video frame at {time}s is wrong: R={red}, G={green}");
    thumbnail.Seek(0);
    StorageFile output = await folder.CreateFileAsync($"decoded-{time:0.0}.jpg", CreationCollisionOption.ReplaceExisting);
    using IRandomAccessStream stream = await output.OpenAsync(FileAccessMode.ReadWrite);
    await RandomAccessStream.CopyAsync(thumbnail, stream);
}
Console.WriteLine($"PASS: real H264 fixture and composition thumbnails generated in {directory}");
foreach (double time in new[] { .5, 2.5 })
{
    List<IRandomAccessStream> frames = await StillPreview.ReadFrames(project, time);
    try
    {
        if (frames.Count != 1) throw new Exception("Missing paused frame");
        BitmapDecoder bitmap = await BitmapDecoder.CreateAsync(frames[0]);
        PixelDataProvider data = await bitmap.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        byte[] pixels = data.DetachPixelData();
        int center = (int)((bitmap.PixelHeight / 2 * bitmap.PixelWidth + bitmap.PixelWidth / 2) * 4);
        if (time < 2 ? pixels[center] < 150 || pixels[center + 1] > 80 : pixels[center + 1] < 150 || pixels[center] > 80)
            throw new Exception($"Wrong paused frame at {time}s");
    }
    finally { foreach (IRandomAccessStream frame in frames) frame.Dispose(); }
}
if ((await StillPreview.ReadFrames(project, 10)).Count != 0) throw new Exception("Gap should be black");
Console.WriteLine("PASS: paused preview at 0.5s is red, at 2.5s is green, gap has no visible clips");

{
    project.Clips[0].Scale = .5;
    project.Clips[0].OffsetX = 600;
    MediaComposition transformedComposition = await CompositionBuilder.Build(project);
    StorageFile transformedFile = await folder.CreateFileAsync("transformed.mp4", CreationCollisionOption.ReplaceExisting);
    TranscodeFailureReason transformedResult = await transformedComposition.RenderToFileAsync(transformedFile, MediaTrimmingPreference.Precise, CompositionBuilder.CreateEncodingProfile(project));
    if (transformedResult != TranscodeFailureReason.None) throw new Exception("Transform render failed");
    MediaClip transformedClip = await MediaClip.CreateFromFileAsync(transformedFile);
    VideoEncodingProperties encoding = transformedClip.GetVideoEncodingProperties();
    if (encoding.Width != 1920 || encoding.Height != 1080) throw new Exception("Wrong output resolution");
    MediaComposition decodedTransform = new();
    decodedTransform.Clips.Add(transformedClip);
    using ImageStream frame = await decodedTransform.GetThumbnailAsync(TimeSpan.FromSeconds(.5), 320, 180, VideoFramePrecision.NearestFrame);
    BitmapDecoder bitmap = await BitmapDecoder.CreateAsync(frame);
    PixelDataProvider data = await bitmap.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    byte[] pixels = data.DetachPixelData();
    int center = (90 * 320 + 160) * 4;
    int right = (90 * 320 + 260) * 4;
    if (pixels[center] > 40 || pixels[center + 1] > 40 || pixels[right] < 150 || pixels[right + 1] > 80)
        throw new Exception("Export does not match the scaled and translated clip bounds");
    Console.WriteLine("PASS: transformed MP4 is 1920x1080, center is black and translated right side is red");
}

{
    string output = Path.Combine(directory, $"export-check-{Guid.NewGuid():N}.mp4");
    try
    {
        await VideoExport.Write(project, output, videoBitrate: 5_000_000);
        MediaClip exported = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(output));
        if (exported.GetVideoEncodingProperties().Width != 1920 || Math.Abs(exported.OriginalDuration.TotalSeconds - 4) > .1)
            throw new Exception("Exported video has wrong dimensions or duration");
        byte[] originalBytes = await File.ReadAllBytesAsync(output);
        try
        {
            await VideoExport.Write(project, output);
            throw new Exception("Existing export was overwritten");
        }
        catch (ArgumentException) { }
        using CancellationTokenSource cancellation = new();
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
        using CancellationTokenSource duringRender = new();
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
    EditProject captionProject = project.Copy();
    captionProject.Clips[0].Scale = 1;
    captionProject.Clips[0].OffsetX = 0;
    captionProject.Clips[0].Layer = 10;
    EditClip caption = new()
    {
        Kind = "subtitle", Text = "日本語テスト字幕", Start = 0, Duration = 1, Layer = 9,
        Subtitle = new SubtitleStyle { FontFamily = "Yu Gothic UI", FontSize = 80, MarginY = 80 }
    };
    captionProject.Clips.Add(caption);
    string path = SubtitleRenderer.SourcePath(caption, captionProject.Video);
    using (System.Drawing.Bitmap png = new(path))
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
    List<StillPreview.Layer> frames = await StillPreview.ReadLayers(captionProject, .5);
    if (frames.Count != 2 || frames[1].Clip.Kind != "subtitle") throw new Exception("Subtitle missing in paused preview");
    foreach (StillPreview.Layer layer in frames) layer.Stream.Dispose();
    string output = Path.Combine(directory, "subtitle-check.mp4");
    await VideoExport.Write(captionProject, output, overwrite: true);
    MediaComposition decodedCaption = new();
    decodedCaption.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(output)));
    using ImageStream thumbnail = await decodedCaption.GetThumbnailAsync(TimeSpan.FromSeconds(.5), 960, 540, VideoFramePrecision.NearestFrame);
    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(thumbnail);
    byte[] data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore,
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
    using ImageStream after = await decodedCaption.GetThumbnailAsync(TimeSpan.FromSeconds(2.5), 320, 180, VideoFramePrecision.NearestFrame);
    BitmapDecoder afterDecoder = await BitmapDecoder.CreateAsync(after);
    byte[] afterData = (await afterDecoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore,
        new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
    for (int i = 0; i < afterData.Length; i += 4)
        if (afterData[i] > 170 && afterData[i + 1] > 170 && afterData[i + 2] > 170)
            throw new Exception("Subtitle remained after its end time");
    caption.Subtitle.Vertical = "top";
    caption.Subtitle.Horizontal = "left";
    using System.Drawing.Bitmap top = new(SubtitleRenderer.SourcePath(caption, captionProject.Video));
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



