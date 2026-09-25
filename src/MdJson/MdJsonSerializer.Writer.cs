using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Atelia.MdJson;

/// <summary>Converts JSON values to and from md-json Markdown documents.</summary>
public static partial class MdJsonSerializer {
    /// <summary>Renders a JSON value, moving candidate strings that require JSON escaping into verbatim fenced blocks.</summary>
    /// <param name="externalStringPaths">Candidate JSON Pointer paths; every path is validated even when its string stays in the skeleton.</param>
    /// <exception cref="ArgumentNullException">The path list or one of its paths is null.</exception>
    /// <exception cref="FormatException">The value or a selected path is outside the supported domain.</exception>
    public static string Write(JsonElement value, IReadOnlyList<string> externalStringPaths) {
        ArgumentNullException.ThrowIfNull(externalStringPaths);
        JsonValues.Validate(value);
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var bodies = new List<(string Path, string Text)>();
        foreach (var path in externalStringPaths) {
            ArgumentNullException.ThrowIfNull(path);
            if (!seen.Add(path))
                throw new FormatException("An external string path occurs more than once.");
            var selected = JsonValues.Resolve(value, path);
            if (selected.ValueKind != JsonValueKind.String)
                throw new FormatException("External string paths must select strings.");
            var body = selected.GetString()!;
            if (!RequiresJsonEscaping(body, options))
                continue;
            replacements.Add(path, path);
            bodies.Add((path, body));
        }

        var skeleton = JsonValues.Replace(value, replacements);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = int.MaxValue,
            NewLine = "\n"
        })) {
            skeleton.WriteTo(writer);
        }

        var result = new StringBuilder("# structure\n\n~~~json\n");
        result.Append(Encoding.UTF8.GetString(buffer.ToArray()));
        result.Append("\n~~~\n\n# content\n");
        foreach (var (path, body) in bodies) {
            var tildeLength = FenceLength(body, '~');
            var backtickLength = FenceLength(body, '`');
            var fence = tildeLength <= backtickLength ? new string('~', tildeLength) : new string('`', backtickLength);
            result.Append("\n## ").Append(JsonSerializer.Serialize(path, options)).Append("\n\n");
            result.Append(fence).Append("text\n").Append(body).Append('\n').Append(fence).Append('\n');
        }
        return result.ToString();
    }

    private static bool RequiresJsonEscaping(string text, JsonSerializerOptions options) {
        // Use the same encoder as the skeleton, including escapes beyond JSON's minimum.
        var encoded = JsonSerializer.Serialize(text, options);
        return !encoded.AsSpan(1, encoded.Length - 2).SequenceEqual(text.AsSpan());
    }

    private static int FenceLength(string body, char character) {
        var longest = 0;
        var current = 0;
        foreach (var item in body) {
            current = item == character ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }
        return Math.Max(3, checked(longest + 1));
    }
}
