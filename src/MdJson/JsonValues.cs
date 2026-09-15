using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Atelia.MdJson;

internal static class JsonValues {
    internal static void Validate(JsonElement value) {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new FormatException("Undefined is not a JSON value.");

        // Inspect escapes before DOM decoding can discard invalid surrogate information.
        ValidateRawStrings(value.GetRawText());
        ValidateMembers(value);
    }

    private static void ValidateMembers(JsonElement value) {
        if (value.ValueKind == JsonValueKind.Object) {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) {
                if (!names.Add(property.Name))
                    throw new FormatException("JSON object property names must be unique.");
                ValidateMembers(property.Value);
            }
        } else if (value.ValueKind == JsonValueKind.Array) {
            foreach (var item in value.EnumerateArray())
                ValidateMembers(item);
        }
    }

    private static void ValidateRawStrings(string json) {
        // A JsonElement may come from a document that accepted comments or trailing
        // commas. Tokenize its source so comment text cannot masquerade as strings.
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = int.MaxValue
        });
        while (reader.Read()) {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) continue;
            var rawString = Encoding.UTF8.GetString(reader.ValueSpan);
            var pendingHighSurrogate = false;
            for (var index = 0; index < rawString.Length; index++) {
                var character = rawString[index];
                if (character == '\\') {
                    character = rawString[++index];
                    if (character == 'u') {
                        character = (char)ushort.Parse(rawString.AsSpan(index + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        index += 4;
                    }
                    // All other valid JSON escapes decode to non-surrogate characters.
                }

                if (pendingHighSurrogate) {
                    if (!char.IsLowSurrogate(character))
                        throw new FormatException("JSON strings must contain valid Unicode.");
                    pendingHighSurrogate = false;
                } else if (char.IsLowSurrogate(character))
                    throw new FormatException("JSON strings must contain valid Unicode.");
                else
                    pendingHighSurrogate = char.IsHighSurrogate(character);
            }
            if (pendingHighSurrogate)
                throw new FormatException("JSON strings must contain valid Unicode.");
        }
    }

    internal static JsonElement Resolve(JsonElement value, string pointer) {
        ArgumentNullException.ThrowIfNull(pointer);
        if (pointer.Length == 0) return value;
        if (pointer[0] != '/') throw new FormatException("A JSON Pointer must be empty or start with '/'.");

        foreach (var encodedToken in pointer[1..].Split('/')) {
            for (var index = 0; index < encodedToken.Length; index++) {
                if (encodedToken[index] != '~') continue;
                if (++index == encodedToken.Length || encodedToken[index] is not ('0' or '1'))
                    throw new FormatException("Invalid JSON Pointer escape.");
            }
            var token = encodedToken.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object) {
                if (!value.TryGetProperty(token, out value))
                    throw new FormatException("JSON Pointer property does not exist.");
            } else if (value.ValueKind == JsonValueKind.Array) {
                if (token.Length == 0 || (token.Length > 1 && token[0] == '0') ||
                    token.Any(character => character is < '0' or > '9') ||
                    !int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var arrayIndex) ||
                    arrayIndex >= value.GetArrayLength())
                    throw new FormatException("JSON Pointer array index is invalid or out of range.");
                value = value[arrayIndex];
            } else
                throw new FormatException("JSON Pointer traverses a scalar value.");
        }
        return value;
    }

    internal static JsonElement Replace(JsonElement value, IReadOnlyDictionary<string, string> replacements) {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = int.MaxValue
        })) {
            Copy(writer, value, "", replacements);
        }
        using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = int.MaxValue });
        return document.RootElement.Clone();
    }

    private static void Copy(Utf8JsonWriter writer, JsonElement value, string path, IReadOnlyDictionary<string, string> replacements) {
        if (replacements.TryGetValue(path, out var replacement)) {
            writer.WriteStringValue(replacement);
            return;
        }
        switch (value.ValueKind) {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject()) {
                    writer.WritePropertyName(property.Name);
                    var token = property.Name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
                    Copy(writer, property.Value, path + "/" + token, replacements);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    Copy(writer, item, path + "/" + (index++).ToString(CultureInfo.InvariantCulture), replacements);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
