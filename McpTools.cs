using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoEditor;

public static class McpTools
{
    public sealed class Tool
    {
        public string name { get; }
        public string description { get; }
        public object inputSchema { get; }
        [JsonIgnore] public string Method { get; }
        private readonly Dictionary<string, string> properties;
        private readonly string[] required;

        public Tool(string method, string description, string fields = "", string required = "")
        {
            Method = method;
            name = method.Replace('.', '_');
            this.description = description;
            properties = fields.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(field => field.Split(':')).ToDictionary(pair => pair[0], pair => pair[1]);
            this.required = required.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            inputSchema = new
            {
                type = "object",
                properties = properties.ToDictionary(pair => pair.Key, pair => pair.Value == "array"
                    ? (object)new { type = "array", items = new { type = "string" } }
                    : new { type = pair.Value }),
                required = this.required,
                additionalProperties = false
            };
        }

        public bool ValidArguments(JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object || required.Any(key => !arguments.TryGetProperty(key, out _))) return false;
            foreach (var property in arguments.EnumerateObject())
            {
                if (!properties.TryGetValue(property.Name, out var type)) return false;
                bool valid = type switch
                {
                    "string" => property.Value.ValueKind == JsonValueKind.String,
                    "number" => property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number) && double.IsFinite(number),
                    "integer" => property.Value.TryGetInt32(out _),
                    "boolean" => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "array" => property.Value.ValueKind == JsonValueKind.Array && property.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String),
                    _ => false
                };
                if (!valid) return false;
            }
            return true;
        }
    }

    public static readonly Tool[] All =
    [
        new("project.get", "Get project settings and all clips with IDs."),
        new("clip.add", "Add a local media file. By default append; atPlayhead places at current time.", "path:string atPlayhead:boolean", "path"),
        new("clip.update", "Update clip timing (seconds), layer (1 is front), volume, position (pixels), or scale. Refresh preview after editing.", "id:string start:number trim:number duration:number layer:integer volume:number offsetX:number offsetY:number scale:number", "id"),
        new("clip.duplicate", "Duplicate a clip and return the new clip.", "id:string", "id"),
        new("clip.split", "Split at absolute project seconds; defaults to playhead.", "id:string seconds:number", "id"),
        new("clip.delete", "Delete a clip.", "id:string", "id"),
        new("selection.get", "Get selected clip IDs and primary selection."),
        new("selection.set", "Set selected clip IDs.", "ids:array", "ids"),
        new("selection.move", "Move selected clips by relative seconds and layers.", "seconds:number layers:integer", "seconds layers"),
        new("workspace.set", "Set local workspace folder.", "path:string", "path"),
        new("project.save", "Save project to local path; replaces an existing project file.", "path:string", "path"),
        new("project.open", "Open a local project file, replacing current project.", "path:string", "path"),
        new("preview.refresh", "Apply edited project to preview."),
        new("playback.get", "Get playhead seconds, duration, playback state and errors."),
        new("playback.play", "Start playback."),
        new("playback.pause", "Pause playback."),
        new("playback.seek", "Seek to absolute seconds (0 through 86400).", "seconds:number", "seconds"),
        new("export.mp4", "Export MP4 to a new local path. Existing files are rejected. Returns after completion.", "path:string", "path")
    ];
}
