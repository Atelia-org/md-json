using System.Text.Json;
using Atelia.MdJson;
using Xunit;

namespace Atelia.MdJson.Tests;

public sealed class SerializationTests {
    private static JsonElement Parse(string json) {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    private static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ObservationAndRecallRoundTrip(int selected) {
        var value = Value(new {
            observation = new[] { new { id = "42", name = "张三", intention = "喝口水，继续说道：\n“我跟你说呀！”\n“那天我在后山看见了一个……”" } },
            recall = new[] { new { timestamp = "2026-09-15", content = "张三常常吹牛皮。" }, new { timestamp = "2026-09-10", content = "好几个村民都在后山听到过怪响。" } }
        });
        string[] paths = ["/observation/0/intention", "/recall/0/content", "/recall/1/content"];
        var markdown = MdJsonSerializer.Write(value, paths.Take(selected).ToArray());
        AssertValues(value, MdJsonSerializer.Read(markdown));
        Assert.Contains("张三", markdown);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[null,false,{},[],\"str\"]")]
    public void AnyRootValueWithNoSelections(string json) {
        var value = Parse(json);
        var markdown = MdJsonSerializer.Write(value, []);
        Assert.Contains("# content", markdown);
        AssertValues(value, MdJsonSerializer.Read(markdown));
    }

    public static TheoryData<string> Bodies => new()
    {
        "", "a", "a\n", "a\r", "a\r\n", "a\n\n", "\r", "\n", "\r\n",
        " \t \n\t  ", "a\rb\nc\r\nd", "\0", "a\0b\n\0", "\u0001\u000b\u000c\u001f",
        "张三 😀 e\u0301 é \u2028\u2029", "# content\n## \"/fake\"\n~~~\n````\n~~~text",
        "[unused]: /url", "&amp; <b>literal</b> \\ \"", "\n    indented\n\tline\r"
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public void RootBodyRetainsEveryCharacter(string body) {
        var markdown = MdJsonSerializer.Write(Value(body), [""]);
        Assert.Contains("## \"\"", markdown);
        Assert.Equal(body, MdJsonSerializer.Read(markdown).GetString());
    }

    [Theory]
    [InlineData("", "~~~")]
    [InlineData("``", "~~~")]
    [InlineData("~~~", "```")]
    [InlineData("```", "~~~")]
    [InlineData("~~~ ```", "~~~~")]
    [InlineData("~~~~~~~ ```", "````")]
    [InlineData("a~~~b```c", "~~~~")]
    public void FenceChoiceAndFramingAreObservable(string body, string fence) {
        var markdown = MdJsonSerializer.Write(Value(body), [""]);
        Assert.Contains(fence + "text\n" + body + "\n" + fence, markdown);
        Assert.Equal(body, MdJsonSerializer.Read(markdown).GetString());
    }

    [Fact]
    public void SpecialKeysUsePointerTokensAndJsonHeadingEncoding() {
        var keys = new[] { "", "a/b", "~1", "~", "quote\"", "line\nbreak", "张三😀", "01", "-", "*em* [link](url)", "Case", "case", "é", "e\u0301" };
        var value = Value(keys.ToDictionary(k => k, k => "body:" + k));
        var paths = keys.Select(k => "/" + k.Replace("~", "~0").Replace("/", "~1")).ToArray();
        AssertValues(value, MdJsonSerializer.Read(MdJsonSerializer.Write(value, paths)));
        Assert.Equal("body:~1", MdJsonSerializer.Read(MdJsonSerializer.Write(value, ["/~01"])).GetProperty("~1").GetString());
    }

    [Fact]
    public void DeepStructureDoesNotDependOnHeadingDepth() {
        var json = "\"leaf\"";
        for (var i = 0; i < 24; i++) json = "{\"x\":" + json + "}";
        var value = Parse(json);
        AssertValues(value, MdJsonSerializer.Read(MdJsonSerializer.Write(value, [string.Concat(Enumerable.Repeat("/x", 24))])));
    }

    [Fact]
    public void DomDeeperThanDefaultJsonParserLimitRoundTrips() {
        var json = "\"leaf\"";
        for (var i = 0; i < 96; i++) json = "{\"x\":" + json + "}";
        using var source = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
        var markdown = MdJsonSerializer.Write(source.RootElement, [string.Concat(Enumerable.Repeat("/x", 96))]);
        AssertValues(source.RootElement, MdJsonSerializer.Read(markdown));
    }

    [Fact]
    public void PermissivelyParsedDomWritesStrictJsonWithoutTreatingCommentsAsStrings() {
        const string json = "{ /* malformed JSON text in comment: \" \\uD800 \\q */ \"x\": \"body\", \"n\": 1.2300e+80, }";
        using var source = JsonDocument.Parse(json, new JsonDocumentOptions {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        var markdown = MdJsonSerializer.Write(source.RootElement, ["/x"]);
        AssertValues(source.RootElement, MdJsonSerializer.Read(markdown));
        Assert.DoesNotContain("malformed JSON text", markdown);
        const string opener = "~~~json\n";
        var start = markdown.IndexOf(opener, StringComparison.Ordinal) + opener.Length;
        var end = markdown.IndexOf("\n~~~", start, StringComparison.Ordinal);
        using var strictSkeleton = JsonDocument.Parse(markdown[start..end]);
        Assert.Equal("/x", strictSkeleton.RootElement.GetProperty("x").GetString());
    }

    [Fact]
    public void SkeletonFenceIsFixedEvenWithFenceRunsInKeysAndValues() {
        var markdown = MdJsonSerializer.Write(Value(new Dictionary<string, string> { ["~~~~~~~~"] = "\n~~~~~~~\n````````" }), []);
        Assert.StartsWith("# structure\n", markdown);
        Assert.Contains("\n~~~json\n", markdown);
        Assert.DoesNotContain("~~~~json", markdown);
        AssertValues(Value(new Dictionary<string, string> { ["~~~~~~~~"] = "\n~~~~~~~\n````````" }), MdJsonSerializer.Read(markdown));
    }

    [Fact]
    public void ListOrderControlsBodiesButNotValuesOrInput() {
        var value = Parse("{\"z\":\"same\",\"a\":[\"same\",\"other\"]}");
        var before = value.GetRawText();
        var first = MdJsonSerializer.Write(value, ["/z", "/a/1", "/a/0"]);
        var second = MdJsonSerializer.Write(value, ["/a/0", "/a/1", "/z"]);
        Assert.True(first.IndexOf("## \"/z\"", StringComparison.Ordinal) < first.IndexOf("## \"/a/1\"", StringComparison.Ordinal));
        Assert.True(second.IndexOf("## \"/a/0\"", StringComparison.Ordinal) < second.IndexOf("## \"/a/1\"", StringComparison.Ordinal));
        Assert.True(second.IndexOf("## \"/a/1\"", StringComparison.Ordinal) < second.IndexOf("## \"/z\"", StringComparison.Ordinal));
        AssertValues(value, MdJsonSerializer.Read(first));
        AssertValues(value, MdJsonSerializer.Read(second));
        Assert.Equal(before, value.GetRawText());
    }

    [Fact]
    public void NumbersKeepRawPrecisionAndPropertyOrderAcrossRefill() {
        var value = Parse("{\"z\":1234567890123456789012345678901234567890,\"body\":\"hello\",\"a\":[0.123456789012345678901234567890,1e+400,-0,1.2300E-1000]}");
        AssertValues(value, MdJsonSerializer.Read(MdJsonSerializer.Write(value, ["/body"])));
    }

    [Fact]
    public void ReturnedValueOwnsItsLifetime() {
        JsonElement result;
        using (var source = JsonDocument.Parse("{\"x\":\"body\"}"))
            result = MdJsonSerializer.Read(MdJsonSerializer.Write(source.RootElement, ["/x"]));
        Assert.Equal("body", result.GetProperty("x").GetString());
        Assert.NotEmpty(result.GetRawText());
    }

    [Fact]
    public void DeterministicAdversarialStringRoundTrips() {
        string[] fragments = ["a", "张", "😀", "\0", "\r", "\n", "\r\n", "\t", " ", "~~~", "````", "# content", "\\", "\"", "\u2028", "[x]: /url"];
        var random = new Random(6901);
        for (var sample = 0; sample < 128; sample++) {
            var body = string.Concat(Enumerable.Range(0, random.Next(0, 40)).Select(_ => fragments[random.Next(fragments.Length)]));
            var value = Value(new { before = "meta", bodies = new[] { body, "/bodies/0" }, after = 23 });
            AssertValues(value, MdJsonSerializer.Read(MdJsonSerializer.Write(value, ["/bodies/1", "/bodies/0"])));
        }
    }

    private static void AssertValues(JsonElement expected, JsonElement actual) {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        switch (expected.ValueKind) {
            case JsonValueKind.Object:
                var left = expected.EnumerateObject().ToArray();
                var right = actual.EnumerateObject().ToArray();
                Assert.Equal(left.Select(p => p.Name), right.Select(p => p.Name));
                for (var i = 0; i < left.Length; i++) AssertValues(left[i].Value, right[i].Value);
                break;
            case JsonValueKind.Array:
                Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
                for (var i = 0; i < expected.GetArrayLength(); i++) AssertValues(expected[i], actual[i]);
                break;
            case JsonValueKind.String: Assert.Equal(expected.GetString(), actual.GetString()); break;
            case JsonValueKind.Number: Assert.Equal(expected.GetRawText(), actual.GetRawText()); break;
        }
    }
}
