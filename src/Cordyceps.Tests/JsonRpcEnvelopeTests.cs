using System.Text.Json;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Contract tests for the JSON-RPC 2.0 response envelope (Core/JsonRpcEnvelope).
/// The id-echo contract matters because a lossy echo (the old GetInt32-style path)
/// destroyed the response AFTER the tool's side effects ran, making clients retry
/// and double-apply mutations. These tests pin lossless echo for every id shape.
/// </summary>
public class JsonRpcEnvelopeTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static string RoundTrip(string requestJson, object result = null, string error = null, int code = 0)
    {
        var request = Parse(requestJson);
        var id = JsonRpcEnvelope.EchoId(request);
        var envelope = JsonRpcEnvelope.Build(id, result ?? new { ok = true }, error, code);
        return JsonRpcEnvelope.Serialize(envelope);
    }

    // --- Notification detection: ABSENT id only ---

    [Fact]
    public void AbsentIdIsNotification() =>
        Assert.True(JsonRpcEnvelope.IsNotification(Parse("{\"jsonrpc\":\"2.0\",\"method\":\"m\"}")));

    [Fact]
    public void ExplicitNullIdIsNotANotification() =>
        Assert.False(JsonRpcEnvelope.IsNotification(Parse("{\"jsonrpc\":\"2.0\",\"method\":\"m\",\"id\":null}")));

    [Theory]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"id\":\"abc\"}")]
    [InlineData("{\"id\":0}")]
    [InlineData("{\"id\":false}")]
    public void AnyPresentIdIsNotANotification(string json) =>
        Assert.False(JsonRpcEnvelope.IsNotification(Parse(json)));

    [Fact]
    public void EchoIdReturnsNullForNotifications() =>
        Assert.Null(JsonRpcEnvelope.EchoId(Parse("{\"method\":\"m\"}")));

    // --- Lossless id echo through full serialization ---

    [Fact]
    public void IntIdRoundTrips() =>
        Assert.Contains("\"id\":42", RoundTrip("{\"id\":42}"));

    [Fact]
    public void LargeInt64IdRoundTrips() =>
        // Above int.MaxValue — the old GetInt32 echo threw here and the client got HTTP 400.
        Assert.Contains("\"id\":8589934592", RoundTrip("{\"id\":8589934592}"));

    [Fact]
    public void FractionalWholeIdStaysFractional() =>
        // 1.0 must echo as 1.0, not 1 — byte-lossless echo of what the client sent.
        Assert.Contains("\"id\":1.0", RoundTrip("{\"id\":1.0}"));

    [Fact]
    public void TrueFractionalIdRoundTrips() =>
        Assert.Contains("\"id\":1.5", RoundTrip("{\"id\":1.5}"));

    [Fact]
    public void StringIdRoundTrips() =>
        Assert.Contains("\"id\":\"req-a7f3\"", RoundTrip("{\"id\":\"req-a7f3\"}"));

    [Fact]
    public void NullIdSurvivesWhenWritingNull()
    {
        // A boxed JsonElement of ValueKind.Null is not a null reference, so the
        // WhenWritingNull serializer option must not strip the "id" member.
        var json = RoundTrip("{\"id\":null}");
        Assert.Contains("\"id\":null", json);
    }

    [Fact]
    public void EchoIdSurvivesSourceDocumentDisposal()
    {
        JsonElement? id;
        using (var doc = JsonDocument.Parse("{\"id\":\"outlives-the-document\"}"))
        {
            id = JsonRpcEnvelope.EchoId(doc.RootElement);
        }
        // Clone() detaches from the parent document; serializing after disposal must work.
        var json = JsonRpcEnvelope.Serialize(JsonRpcEnvelope.Build(id, new { }, null, 0));
        Assert.Contains("\"id\":\"outlives-the-document\"", json);
    }

    // --- Envelope shape ---

    [Fact]
    public void SuccessEnvelopeHasResultAndNoError()
    {
        var json = RoundTrip("{\"id\":1}", result: new { value = 7 });
        Assert.Contains("\"result\":", json);
        Assert.Contains("\"jsonrpc\":\"2.0\"", json);
        Assert.DoesNotContain("\"error\":", json);
    }

    [Fact]
    public void ErrorEnvelopeCarriesCodeAndMessageAndNoResult()
    {
        var json = RoundTrip("{\"id\":1}", error: "boom", code: -32603);
        Assert.Contains("\"error\":", json);
        Assert.Contains("\"code\":-32603", json);
        Assert.Contains("\"message\":\"boom\"", json);
        Assert.DoesNotContain("\"result\":", json);
    }

    [Fact]
    public void NotificationBuildOmitsIdEntirely()
    {
        var envelope = JsonRpcEnvelope.Build(null, new { ok = true }, null, 0);
        Assert.False(envelope.ContainsKey("id"));
    }
}
