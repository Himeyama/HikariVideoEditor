using System.Text.Json;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private Task<object> DispatchCommand(JsonElement request)
    {
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (closed || !DispatcherQueue.TryEnqueue(async () =>
        {
            await gate.WaitAsync();
            try
            {
                if (closed)
                    throw new InvalidOperationException("終了中です。");
                var previous = ProjectSnapshot();
                var result = await Command(request);
                await AutoSaveChanges(previous);
                completion.SetResult(result);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                gate.Release();
            }
        }))
            completion.TrySetException(new InvalidOperationException("アプリは終了しました。"));
        return completion.Task;
    }

    private async Task<object> Command(JsonElement r)
    {
        string method = r.GetProperty("method").GetString()!;
        var p = r.TryGetProperty("params", out var parameters) ? parameters : default;
        string Arg(string key) => p.GetProperty(key).GetString() ?? throw new ArgumentException(key);
        switch (method)
        {
            case "ping":
                return new
                {
                    name = "Hikari Video Editor",
                    version = 1
                };
            case "project.get":
                return project.Copy();
            case "speech.voices":
                return await MediaGeneration.Voices();
            case "speech.create":
                return await GenerateMedia(p, true);
            case "image.create":
                return await GenerateMedia(p, false);
            case "clip.add":
                var added = await Add(Arg("path"), p.TryGetProperty("atPlayhead", out var atPlayhead) && atPlayhead.GetBoolean() ? playhead : null);
                return added;
            case "selection.get":
                return new
                {
                    ids = selectedIds.ToArray(),
                    primary = selected
                };
            case "selection.set":
                var ids = p.GetProperty("ids").EnumerateArray().Select(value => value.GetString()!).ToHashSet();
                if (ids.Any(id => !project.Clips.Any(clip => clip.Id == id)))
                    throw new ArgumentException("素材IDがありません。");
                selectedIds.Clear();
                selectedIds.UnionWith(ids);
                UpdatePrimarySelection();
                Draw();
                return new
                {
                    ids = selectedIds.ToArray()
                };
            case "selection.move":
                await MoveSelection(p.GetProperty("seconds").GetDouble(), p.GetProperty("layers").GetInt32());
                return new
                {
                    ids = selectedIds.ToArray()
                };
            case "clip.update":
                var next = project.Copy();
                var clip = next.Clips.Single(c => c.Id == Arg("id"));
                project.RequireEditable(clip.Layer);
                if (p.TryGetProperty("start", out var start))
                    clip.Start = start.GetDouble();
                if (p.TryGetProperty("trim", out var trim))
                    clip.Trim = trim.GetDouble();
                if (p.TryGetProperty("duration", out var duration))
                    clip.Duration = duration.GetDouble();
                if (p.TryGetProperty("layer", out var layer))
                    clip.Layer = layer.GetInt32();
                if (p.TryGetProperty("volume", out var volume))
                    clip.Volume = volume.GetDouble();
                if (p.TryGetProperty("fadeIn", out var fadeIn)) clip.FadeIn = fadeIn.GetDouble();
                if (p.TryGetProperty("fadeOut", out var fadeOut)) clip.FadeOut = fadeOut.GetDouble();
                project.RequireEditable(clip.Layer);
                if (p.TryGetProperty("offsetX", out var offsetX))
                    clip.OffsetX = offsetX.GetDouble();
                if (p.TryGetProperty("offsetY", out var offsetY))
                    clip.OffsetY = offsetY.GetDouble();
                if (p.TryGetProperty("scale", out var scale))
                    clip.Scale = scale.GetDouble();
                await CompositionBuilder.Build(next);
                Remember();
                project = next;
                Select(clip);
                return clip;
            case "clip.duplicate":
                return await DuplicateClip(Arg("id"));
            case "workspace.set":
                var workspacePath = Path.GetFullPath(Arg("path"));
                await LoadWorkspaceDirectory(workspacePath);
                Remember();
                project.WorkspaceDirectory = workspacePath;
                UpdateGenerationAvailability();
                UpdateStatusBar();
                return new
                {
                    path = workspacePath
                };
            case "clip.split":
                return await Split(Arg("id"), p.TryGetProperty("seconds", out var splitTime) ? splitTime.GetDouble() : playhead);
            case "playback.get":
                return new
                {
                    seconds = playhead,
                    duration = ProjectEnd,
                    state = Preview.MediaPlayer.PlaybackSession.PlaybackState.ToString(),
                    opening = previewOpening,
                    hasSource = Preview.MediaPlayer.Source != null,
                    error = playbackError
                };
            case "clip.delete":
                var id = Arg("id");
                if (!project.Clips.Any(c => c.Id == id))
                    throw new ArgumentException("IDがありません。");
                project.RequireEditable(project.Clips.Single(c => c.Id == id).Layer);
                Remember();
                project.Clips.RemoveAll(c => c.Id == id);
                Draw();
                return new
                {
                    deleted = id
                };
            case "project.save":
                await Save(Arg("path"));
                break;
            case "project.open":
                await Open(Arg("path"));
                break;
            case "preview.refresh":
                await Refresh();
                break;
            case "playback.play":
                Play();
                break;
            case "playback.pause":
                Pause();
                break;
            case "playback.seek":
                var seconds = p.GetProperty("seconds").GetDouble();
                if (!double.IsFinite(seconds) || seconds < 0 || seconds > 86400)
                    throw new ArgumentException("シーク位置が不正です。");
                Seek(seconds);
                break;
            case "export.mp4":
                await Export(Arg("path"));
                break;
            default:
                throw new ArgumentException($"未対応の操作: {method}");
        }

        return new
        {
            ok = true
        };
    }
}
