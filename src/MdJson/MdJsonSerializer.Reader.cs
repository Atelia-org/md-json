using System.Text.Json;
using Markdig;
using Markdig.Syntax;

namespace Atelia.MdJson;

public static partial class MdJsonSerializer {
    /// <summary>Reads a complete md-json document and returns an independently owned JSON value.</summary>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="FormatException">The document is outside the supported md-json format.</exception>
    public static JsonElement Read(string markdown) {
        ArgumentNullException.ThrowIfNull(markdown);
        ValidateUnicode(markdown);

        var blocks = Markdown.Parse(markdown);
        if (blocks.Count < 3 || (blocks.Count - 3) % 2 != 0)
            throw new FormatException("Expected structure, JSON fence, content, then pairs of path headings and text fences.");

        int consumed = 0;
        ReadHeading(blocks[0], 1, "# structure");
        string skeletonSource = ReadFence(blocks[1], "json", skeleton: true);
        ReadHeading(blocks[2], 1, "# content");

        using var skeleton = ParseJson(skeletonSource);
        JsonValues.Validate(skeleton.RootElement);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 3; i < blocks.Count; i += 2) {
            string heading = ReadHeading(blocks[i], 2);
            if (!heading.StartsWith("## \"", StringComparison.Ordinal) || !heading.EndsWith('"'))
                throw new FormatException("A body heading must contain one JSON string literal after '## '.");

            using var title = ParseJson(heading[3..]);
            JsonValues.Validate(title.RootElement);
            if (title.RootElement.ValueKind != JsonValueKind.String)
                throw new FormatException("A body heading must be a JSON string.");
            string path = title.RootElement.GetString()!;
            string text = ReadFence(blocks[i + 1], "text", skeleton: false);
            if (!replacements.TryAdd(path, text))
                throw new FormatException($"Duplicate body path: {path}");

            JsonElement target = JsonValues.Resolve(skeleton.RootElement, path);
            if (target.ValueKind != JsonValueKind.String || !string.Equals(target.GetString(), path, StringComparison.Ordinal))
                throw new FormatException($"The skeleton does not contain the matching string placeholder at: {path}");
        }

        RequireWhitespace(consumed, markdown.Length);
        return JsonValues.Replace(skeleton.RootElement, replacements);

        string ReadHeading(Block block, int level, string? expected = null) {
            BeginBlock(block);
            if (block is not HeadingBlock heading || heading.Level != level || heading.IsSetext)
                throw new FormatException("Expected a top-level ATX heading.");
            int lineEnd = FindLineEnd(block.Span.Start);
            if (block.Span.End >= lineEnd)
                throw new FormatException("A heading must occupy exactly one line.");
            string line = markdown[block.Span.Start..lineEnd];
            if (expected is not null && !string.Equals(line, expected, StringComparison.Ordinal))
                throw new FormatException($"Expected heading: {expected}");
            consumed = AfterLine(lineEnd);
            return line;
        }

        string ReadFence(Block block, string info, bool skeleton) {
            BeginBlock(block);
            if (block is not FencedCodeBlock fence || fence.OpeningFencedCharCount < 3 ||
                fence.ClosingFencedCharCount != fence.OpeningFencedCharCount ||
                (fence.FencedChar != '~' && fence.FencedChar != '`'))
                throw new FormatException("Expected an explicitly closed code fence with matching delimiters.");
            if (skeleton && (fence.FencedChar != '~' || fence.OpeningFencedCharCount != 3))
                throw new FormatException("The JSON skeleton must use a three-tilde fence.");

            string delimiter = new(fence.FencedChar, fence.OpeningFencedCharCount);
            int openingEnd = FindLineEnd(block.Span.Start);
            if (openingEnd == markdown.Length || markdown[block.Span.Start..openingEnd] != delimiter + info)
                throw new FormatException("Invalid fence opening line or missing LF separator.");

            // Markdig's inclusive span ends at the closing fence. Only use it after
            // checking ClosingFencedCharCount: unclosed blocks have different spans.
            int closingStart = markdown.LastIndexOf('\n', block.Span.End) + 1;
            int closingEnd = FindLineEnd(closingStart);
            if (closingStart <= openingEnd || markdown[closingStart..closingEnd] != delimiter ||
                block.Span.End != closingEnd - 1)
                throw new FormatException("Invalid fence closing line.");

            string region = markdown[(openingEnd + 1)..closingStart];
            consumed = AfterLine(closingEnd);
            if (skeleton)
                return region;
            if (!region.EndsWith('\n'))
                throw new FormatException("A text body requires a final LF framing character.");
            return region[..^1];
        }

        void BeginBlock(Block block) {
            if (block.Column != 0 || block.Span.Start < consumed || block.Span.Start >= markdown.Length ||
                block.Span.End < block.Span.Start || block.Span.End >= markdown.Length ||
                (block.Span.Start != 0 && markdown[block.Span.Start - 1] != '\n'))
                throw new FormatException("Blocks must start at column zero with valid source spans.");
            RequireWhitespace(consumed, block.Span.Start);
        }

        void RequireWhitespace(int start, int end) {
            for (int i = start; i < end; i++)
                if (markdown[i] is not (' ' or '\t' or '\r' or '\n'))
                    throw new FormatException("Unexpected source outside the md-json blocks.");
        }

        int FindLineEnd(int start) {
            int end = markdown.IndexOf('\n', start);
            return end < 0 ? markdown.Length : end;
        }

        int AfterLine(int end) => end < markdown.Length ? end + 1 : end;
    }

    private static JsonDocument ParseJson(string source) {
        try {
            return JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = int.MaxValue });
        } catch (JsonException error) {
            throw new FormatException("Invalid JSON in the md-json document.", error);
        }
    }

    private static void ValidateUnicode(string text) {
        // Check before a JSON writer can replace invalid UTF-16 with U+FFFD.
        for (int i = 0; i < text.Length; i++) {
            if (!char.IsSurrogate(text[i]))
                continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1]))
                throw new FormatException("The document must contain valid Unicode.");
            i++;
        }
    }
}
