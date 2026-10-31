using System.Text.Json;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private Task<object> DispatchCommand(JsonElement request)
    {
        TaskCompletionSource<object> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_closed || !DispatcherQueue.TryEnqueue(async () =>
        {
            using IDisposable operation = BeginStatusOperation();
            await _gate.WaitAsync();
            try
            {
                if (_closed)
                    throw new InvalidOperationException("終了中です。");
                string previous = ProjectSnapshot();
                object result = await Command(request);
                await AutoSaveChanges(previous);
                completion.SetResult(result);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
            finally
            {
                _gate.Release();
            }
        }))
            completion.TrySetException(new InvalidOperationException("アプリは終了しました。"));
        return completion.Task;
    }

    private async Task<object> Command(JsonElement r)
    {
        string method = r.GetProperty("method").GetString()!;
        JsonElement p = r.TryGetProperty("params", out JsonElement parameters) ? parameters : default;
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
                return _project.Copy();
            case "speech.voices":
                return await MediaGeneration.Voices();
            case "speech.create":
                return await GenerateMedia(p, true);
            case "image.create":
                return await GenerateMedia(p, false);
            case "clip.add":
                EditClip added = await Add(Arg("path"), p.TryGetProperty("atPlayhead", out JsonElement atPlayhead) && atPlayhead.GetBoolean() ? _playhead : null);
                return added;
            case "selection.get":
                return new
                {
                    ids = _selectedIds.ToArray(),
                    primary = _selected
                };
            case "selection.set":
                HashSet<string> ids = p.GetProperty("ids").EnumerateArray().Select(value => value.GetString()!).ToHashSet();
                if (ids.Any(id => !_project.Clips.Any(clip => clip.Id == id)))
                    throw new ArgumentException("素材IDがありません。");
                _selectedIds.Clear();
                _selectedIds.UnionWith(ids);
                UpdatePrimarySelection();
                Draw();
                return new
                {
                    ids = _selectedIds.ToArray()
                };
            case "selection.move":
                await MoveSelection(p.GetProperty("seconds").GetDouble(), p.GetProperty("layers").GetInt32());
                return new
                {
                    ids = _selectedIds.ToArray()
                };
            case "clip.update":
                EditProject next = _project.Copy();
                EditClip clip = next.Clips.Single(c => c.Id == Arg("id"));
                _project.RequireEditable(clip.Layer);
                if (p.TryGetProperty("start", out JsonElement start))
                    clip.Start = start.GetDouble();
                if (p.TryGetProperty("trim", out JsonElement trim))
                    clip.Trim = trim.GetDouble();
                if (p.TryGetProperty("duration", out JsonElement duration))
                    clip.Duration = duration.GetDouble();
                if (p.TryGetProperty("layer", out JsonElement layer))
                    clip.Layer = layer.GetInt32();
                if (p.TryGetProperty("volume", out JsonElement volume))
                    clip.Volume = volume.GetDouble();
                if (p.TryGetProperty("fadeIn", out JsonElement fadeIn)) clip.FadeIn = fadeIn.GetDouble();
                if (p.TryGetProperty("fadeOut", out JsonElement fadeOut)) clip.FadeOut = fadeOut.GetDouble();
                _project.RequireEditable(clip.Layer);
                if (p.TryGetProperty("offsetX", out JsonElement offsetX))
                    clip.OffsetX = offsetX.GetDouble();
                if (p.TryGetProperty("offsetY", out JsonElement offsetY))
                    clip.OffsetY = offsetY.GetDouble();
                if (p.TryGetProperty("scale", out JsonElement scale))
                    clip.Scale = scale.GetDouble();
                await CompositionBuilder.Build(next);
                Remember();
                _project = next;
                Select(clip);
                return clip;
            case "clip.duplicate":
                return await DuplicateClip(Arg("id"));
            case "workspace.set":
                string workspacePath = Path.GetFullPath(Arg("path"));
                await LoadWorkspaceDirectory(workspacePath);
                Remember();
                _project.WorkspaceDirectory = workspacePath;
                UpdateGenerationAvailability();
                UpdateStatusBar();
                return new
                {
                    path = workspacePath
                };
            case "clip.split":
                return await Split(Arg("id"), p.TryGetProperty("seconds", out JsonElement splitTime) ? splitTime.GetDouble() : _playhead);
            case "playback.get":
                return new
                {
                    seconds = _playhead,
                    duration = ProjectEnd,
                    state = Preview.MediaPlayer.PlaybackSession.PlaybackState.ToString(),
                    opening = _previewOpening,
                    hasSource = Preview.MediaPlayer.Source != null,
                    error = _playbackError
                };
            case "clip.delete":
                string id = Arg("id");
                if (!_project.Clips.Any(c => c.Id == id))
                    throw new ArgumentException("IDがありません。");
                _project.RequireEditable(_project.Clips.Single(c => c.Id == id).Layer);
                Remember();
                _project.Clips.RemoveAll(c => c.Id == id);
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
                double seconds = p.GetProperty("seconds").GetDouble();
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
