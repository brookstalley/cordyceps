using System.Text.Json;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Characterization tests: they pin the JSON-RPC envelope's observable WIRE FORMAT, as opposed to
/// <see cref="JsonRpcEnvelopeTests"/>, which pins its id-echo contract.
///
/// <para>These exist because the wire format is the external contract every MCP client parses, and
/// several of its properties are emergent behavior of the serializer rather than anything the code
/// states explicitly. A serializer swap, or an options change as small as adding a null-handling
/// setting, would alter them silently — every existing envelope test would still pass, because none
/// of them look at these particular bytes. Anything that changes an assertion here is changing what
/// clients receive, and must be a deliberate decision rather than a side effect.</para>
/// </summary>
public class JsonRpcWireFormatTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string Serialize(string requestJson, object result = null, string error = null, int code = 0)
    {
        var id = JsonRpcEnvelope.EchoId(Parse(requestJson));
        return JsonRpcEnvelope.Serialize(JsonRpcEnvelope.Build(id, result, error, code));
    }

    // --- Null result ---

    /// <summary>
    /// A null result is emitted as an explicit "result":null, NOT omitted.
    ///
    /// <para>This is the non-obvious one. The serializer sets DefaultIgnoreCondition =
    /// WhenWritingNull, which reads as though it would drop the member — but that condition applies
    /// to POCO properties, not to values inside a Dictionary&lt;string, object&gt;, and the envelope
    /// is built as a dictionary. So the member survives.</para>
    ///
    /// <para>It matters because JSON-RPC 2.0 requires a response to carry either result or error;
    /// a response with neither is malformed. Omitting the member would produce exactly that. Any
    /// serializer or options change that starts honoring null-omission here would silently emit
    /// malformed responses that no other test would catch.</para>
    /// </summary>
    [Fact]
    public void NullResultIsEmittedExplicitly_NotOmitted()
    {
        var json = Serialize("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"}", result: null);

        Assert.Contains("\"result\":null", json);
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
    }

    /// <summary>An error response carries error and NO result member — the two are exclusive.</summary>
    [Fact]
    public void ErrorResponseOmitsResultEntirely()
    {
        var json = Serialize("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"}", result: null, error: "boom", code: -32000);

        Assert.DoesNotContain("\"result\"", json);
        Assert.Contains("\"code\":-32000", json);
        Assert.Contains("\"message\":\"boom\"", json);
    }

    // --- String ids are echoed verbatim, never reinterpreted ---

    /// <summary>
    /// A string id that happens to look like a date stays a string, byte for byte.
    ///
    /// <para>Some JSON libraries parse date-shaped strings into date values on read and re-emit them
    /// in a normalized form. That would corrupt the id, and a corrupted id is not a cosmetic defect:
    /// the client cannot match the response to its request, so it retries a call whose side effects
    /// have already run.</para>
    /// </summary>
    [Theory]
    [InlineData("2026-08-21T14:02:11Z")]
    [InlineData("2026-08-21")]
    [InlineData("/Date(1600000000000)/")]
    public void DateShapedStringIdIsEchoedVerbatim(string id)
    {
        var json = Serialize($"{{\"jsonrpc\":\"2.0\",\"id\":\"{id}\",\"method\":\"m\"}}");

        Assert.Contains($"\"id\":\"{id}\"", json);
    }

    // --- Numeric ids keep their exact literal form ---

    /// <summary>
    /// Numeric ids are echoed as the client wrote them, including trailing zeros, exponent notation,
    /// and values beyond long's range. The raw text is preserved rather than the parsed value, so
    /// no precision or formatting is lost on the way back.
    /// </summary>
    [Theory]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.00")]
    [InlineData("1e2")]
    [InlineData("-0")]
    [InlineData("9223372036854775808")]      // long.MaxValue + 1
    [InlineData("12345678901234567890123")]  // beyond every fixed-width integer
    [InlineData("0.30000000000000004")]      // a double that does not round-trip through shortest-form printing
    public void NumericIdKeepsItsExactLiteralForm(string id)
    {
        var json = Serialize($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"m\"}}");

        Assert.Contains($"\"id\":{id}", json);
    }

    // --- Compactness ---

    /// <summary>
    /// Output is compact: no indentation and no spaces after separators. Clients read this off a
    /// stream, and the transport's framing assumes a single line.
    /// </summary>
    [Fact]
    public void OutputIsCompact()
    {
        var json = Serialize("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"}", result: new { a = 1, b = "x" });

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain(", ", json);
        Assert.DoesNotContain(": ", json);
    }

    /// <summary>
    /// Member names are emitted verbatim from the anonymous type — no camelCase or other naming
    /// policy is applied. Tool payloads are already authored at their wire names, so a naming policy
    /// would rename every field an MCP client reads.
    /// </summary>
    [Fact]
    public void NoNamingPolicyIsAppliedToResultMembers()
    {
        var json = Serialize("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"}",
            result: new { activeDocument = "x.gh", ToolCount = 7 });

        Assert.Contains("\"activeDocument\":\"x.gh\"", json);
        Assert.Contains("\"ToolCount\":7", json);
    }

    /// <summary>
    /// Non-ASCII content is escaped to \uXXXX rather than emitted as literal characters.
    ///
    /// <para>This is the serializer's default encoder being deliberately conservative, and it is
    /// still valid JSON — a conforming client decodes it back to the original string, so Rhino
    /// documents and layers with non-ASCII names round-trip correctly. Pinned because it is a
    /// visible property of the bytes on the wire: a serializer change would flip it, which is
    /// harmless for conforming clients but would break anything doing raw substring matching on
    /// the response, and it should be observed rather than discovered.</para>
    /// </summary>
    [Fact]
    public void NonAsciiIsUnicodeEscapedButRoundTrips()
    {
        var json = Serialize("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"}",
            result: new { name = "Wandstärke" });

        Assert.DoesNotContain("Wandstärke", json);
        Assert.Contains("Wandst\\u00E4rke", json);

        // The contract that actually matters: a conforming parser recovers the original string.
        var name = Parse(json).GetProperty("result").GetProperty("name").GetString();
        Assert.Equal("Wandstärke", name);
    }
}
