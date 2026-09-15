using System.Text.Json;
using Atelia.MdJson;
using Xunit;

namespace Atelia.MdJson.Tests;

public sealed class ValidationTests {
    private static JsonElement Parse(string json) {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    private static string Document(string json, string bodies = "") => "# structure\n\n~~~json\n" + json + "\n~~~\n\n# content\n\n" + bodies;
    private static string Body(string pointer, string content) => "## " + JsonSerializer.Serialize(pointer) + "\n\n~~~text\n" + content + "\n~~~\n\n";

    [Theory]
    [InlineData("x")]
    [InlineData("#/x")]
    [InlineData("/missing")]
    [InlineData("/x~")]
    [InlineData("/x~2")]
    [InlineData("/arr/01")]
    [InlineData("/arr/-")]
    [InlineData("/arr/-1")]
    [InlineData("/arr/+0")]
    [InlineData("/arr/214748364800000000000")]
    [InlineData("/arr/1")]
    [InlineData("/number")]
    [InlineData("/nil")]
    [InlineData("/arr")]
    [InlineData("/x/child")]
    [InlineData("")]
    public void InvalidSelectionFails(string path) {
        var value = Parse("{\"x\":\"str\",\"arr\":[\"str\"],\"number\":1,\"nil\":null}");
        Assert.Throws<FormatException>(() => MdJsonSerializer.Write(value, [path]));
    }

    [Fact]
    public void DuplicateSelectionFails() => Assert.Throws<FormatException>(() => MdJsonSerializer.Write(Parse("{\"x\":\"s\"}"), ["/x", "/x"]));

    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"unselected\":{\"x\":1,\"x\":2},\"text\":\"s\"}")]
    [InlineData("{\"x\":1,\"\\u0078\":2}")]
    public void DuplicateKeysAnywhereAreOutsideDomain(string json) {
        Assert.Throws<FormatException>(() => MdJsonSerializer.Write(Parse(json), []));
        Assert.Throws<FormatException>(() => MdJsonSerializer.Read(Document(json)));
    }

    [Theory]
    [InlineData("\"\\uD800\"")]
    [InlineData("\"\\uDC00\"")]
    [InlineData("{\"\\uD800\":1}")]
    public void InvalidUnicodeEscapesAreRejected(string json) {
        Assert.Throws<FormatException>(() => MdJsonSerializer.Write(Parse(json), []));
        Assert.Throws<FormatException>(() => MdJsonSerializer.Read(Document(json)));
    }

    [Fact]
    public void NullArgumentsHaveArgumentExceptions() {
        Assert.Throws<ArgumentNullException>(() => MdJsonSerializer.Read(null!));
        Assert.Throws<ArgumentNullException>(() => MdJsonSerializer.Write(Parse("null"), null!));
    }

    [Fact]
    public void UndefinedValueIsRejected() => Assert.Throws<FormatException>(() => MdJsonSerializer.Write(default, []));

    [Theory]
    [InlineData("/x", "{\"x\":\"ordinary\"}")]
    [InlineData("/x", "{\"x\":42}")]
    [InlineData("/missing", "{}")]
    [InlineData("x", "{\"x\":\"x\"}")]
    [InlineData("/x~2", "{\"x~2\":\"/x~2\"}")]
    [InlineData("/a/00", "{\"a\":[\"/a/00\"]}")]
    public void BadBodyReferenceFails(string pointer, string json) => Assert.Throws<FormatException>(() => MdJsonSerializer.Read(Document(json, Body(pointer, "replacement"))));

    [Fact]
    public void DuplicateBodyFailsEvenIfFirstRestoresItsOwnPointer() => Assert.Throws<FormatException>(() => MdJsonSerializer.Read(Document("{\"x\":\"/x\"}", Body("/x", "/x") + Body("/x", "second"))));

    [Fact]
    public void PathLookingValuesAndBodiesAreNotRecursivelyDereferenced() {
        var result = MdJsonSerializer.Read(Document("{\"x\":\"/x\",\"y\":\"/y\",\"literal\":\"/x\"}", Body("/y", "final") + Body("/x", "/y")));
        Assert.Equal("/y", result.GetProperty("x").GetString());
        Assert.Equal("final", result.GetProperty("y").GetString());
        Assert.Equal("/x", result.GetProperty("literal").GetString());
        Assert.Equal("/x", MdJsonSerializer.Read(Document("{\"x\":\"/x\"}")).GetProperty("x").GetString());
    }

    [Fact]
    public void HandAuthoredBodiesUseRawSourceAndRemoveOnlyOneLf() {
        var result = MdJsonSerializer.Read(Document("{\"x\":\"/x\"}", Body("/x", "\0a\r")));
        Assert.Equal("\0a\r", result.GetProperty("x").GetString());
        Assert.Equal("[unused]: /url", MdJsonSerializer.Read(Document("\"\"", Body("", "[unused]: /url"))).GetString());
    }

    [Fact]
    public void HandAuthoredPointersDecodeJsonThenSplitThenUnescapeOnce() {
        var result = MdJsonSerializer.Read(Document(
            "{\"a/b\":\"/a~1b\",\"~1\":\"/~01\",\"line\\nbreak\":\"/line\\nbreak\",\"\":\"/\"}",
            "## \"/a~1b\"\n\n~~~text\nslash\n~~~\n\n" +
            "## \"/~01\"\n\n~~~text\ntilde-one\n~~~\n\n" +
            "## \"/line\\nbreak\"\n\n~~~text\nnewline-key\n~~~\n\n" +
            "## \"/\"\n\n~~~text\nempty-key\n~~~\n"));
        Assert.Equal("slash", result.GetProperty("a/b").GetString());
        Assert.Equal("tilde-one", result.GetProperty("~1").GetString());
        Assert.Equal("newline-key", result.GetProperty("line\nbreak").GetString());
        Assert.Equal("empty-key", result.GetProperty("").GetString());
    }

    public static IEnumerable<object[]> MalformedDocuments() {
        yield return [""];
        yield return ["# content\n\n# structure\n\n~~~json\nnull\n~~~\n"];
        yield return ["# structure\n\n~~~json\nnull\n~~~\n"];
        yield return [Document("null") + "# extra\n"];
        yield return [Document("null") + "~~~text\nbody\n~~~\n"];
        yield return [Document("null") + "## \"/x\"\n"];
        yield return [Document("null") + "ordinary paragraph\n"];
        yield return [Document("null") + "<!-- hidden -->\n"];
        yield return [Document("null") + "[unused]: /url\n"];
        yield return ["[unused]: /url\n\n" + Document("null")];
        yield return [Document("null").Replace("# content", "[unused]: /url\n\n# content", StringComparison.Ordinal)];
        yield return [Document("null").Replace("# content", "> # content", StringComparison.Ordinal)];
        yield return [Document("null").Replace("# content", "- # content", StringComparison.Ordinal)];
        yield return [Document("null").Replace("~~~json", "    ~~~json", StringComparison.Ordinal)];
        yield return [Document("null").Replace("~~~json", "~~~text", StringComparison.Ordinal)];
        yield return [Document("null").Replace("# structure", "structure\n=========", StringComparison.Ordinal)];
        yield return [Document("{\"x\":\"/x\"}", "## /x\n\n~~~text\nbody\n~~~\n")];
        yield return [Document("{\"x\":\"/x\"}", "## \"/x\" trailing\n\n~~~text\nbody\n~~~\n")];
        yield return [Document("{\"x\":\"/x\"}", "## \"/x\"\n\n~~~text\nbody\n")];
        yield return [Document("{\"x\":\"/x\"}", "## \"/x\"\n\n~~~text\n~~~\n")];
        yield return ["# structure\n\n~~~json\nnull\n\n# content\n"];
        yield return [Document("{\"x\":1,}")];
        yield return [Document("/* comment */ null")];
        yield return [Document("NaN")];
        yield return [Document("null true")];
        yield return [Document("null").Replace("# content", "~~~json\nnull\n~~~\n\n# content", StringComparison.Ordinal)];
    }

    [Theory]
    [MemberData(nameof(MalformedDocuments))]
    public void StrictShapeRejectsMalformedDocuments(string markdown) => Assert.Throws<FormatException>(() => MdJsonSerializer.Read(markdown));

    [Fact]
    public void RawInvalidUnicodeBodyIsRejected() => Assert.Throws<FormatException>(() => MdJsonSerializer.Read(Document("\"\"", Body("", "\ud800"))));
}
