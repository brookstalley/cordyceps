using System;
using System.Reflection;
using System.Text.Json;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    public class ToolArgumentBinderTests
    {
        private static int _invocations;

        public static string Sample(string action, string sourceId = null, string color = "red", int count = 1)
        {
            _invocations++;
            return $"{action}/{sourceId}/{color}/{count}";
        }

        public static void AllOptional(string a = "x") { }

        private static MethodInfo M => typeof(ToolArgumentBinderTests).GetMethod(nameof(Sample));
        private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

        // Mirrors McpServer: bind, then invoke. Rejection must precede invocation.
        private static object Call(string json)
        {
            var args = ToolArgumentBinder.Bind(M, J(json));
            return M.Invoke(null, args);
        }

        private static string Err(string json)
        {
            var before = _invocations;
            var ex = Assert.Throws<InvalidOperationException>(() => Call(json));
            Assert.Equal(before, _invocations); // method not invoked
            return ex.Message;
        }

        [Fact]
        public void KnownArguments_BindAsBefore()
        {
            Assert.Equal("go/a/blue/3", Call("{\"action\":\"go\",\"sourceId\":\"a\",\"color\":\"blue\",\"count\":3}"));
        }

        [Fact]
        public void Defaults_Used_WhenOptionalOmitted()
        {
            Assert.Equal("go//red/1", Call("{\"action\":\"go\"}"));
        }

        [Fact]
        public void UnknownArgument_Rejected_AndMethodNotInvoked()
        {
            var msg = Err("{\"action\":\"go\",\"bogus\":1}");
            Assert.Contains("Unknown argument: 'bogus'", msg);
            Assert.Contains("Valid parameters: action, sourceId, color, count", msg);
        }

        [Fact]
        public void MultipleUnknown_AllReported()
        {
            var msg = Err("{\"action\":\"go\",\"zzzzzz\":1,\"qqqqqq\":2}");
            Assert.Contains("Unknown arguments:", msg);
            Assert.Contains("'zzzzzz'", msg);
            Assert.Contains("'qqqqqq'", msg);
        }

        [Fact]
        public void CaseMismatch_SuggestsCorrectName()
        {
            var msg = Err("{\"action\":\"go\",\"sourceid\":\"a\"}");
            Assert.Contains("'sourceid' (did you mean 'sourceId'?)", msg);
        }

        [Fact]
        public void NearMiss_SuggestsCorrectName()
        {
            var msg = Err("{\"action\":\"go\",\"colour\":\"a\"}");
            Assert.Contains("'colour' (did you mean 'color'?)", msg);
        }

        [Fact]
        public void NothingClose_NoSuggestion()
        {
            var msg = Err("{\"action\":\"go\",\"elephant\":\"a\"}");
            Assert.DoesNotContain("did you mean", msg);
        }

        [Fact]
        public void UnknownReported_EvenWhenRequiredAlsoMissing()
        {
            var msg = Err("{\"Action\":\"go\"}");
            Assert.Contains("'Action' (did you mean 'action'?)", msg);
        }

        [Fact]
        public void MissingRequired_StillReported()
        {
            Assert.Contains("Missing required parameter: action", Err("{}"));
        }

        [Fact]
        public void ValidArgumentMatching_StaysCaseSensitive()
        {
            Err("{\"ACTION\":\"go\"}");
        }

        [Theory]
        [InlineData("null")]
        [InlineData("{}")]
        public void NullOrEmptyArguments_Valid(string json)
        {
            var m = typeof(ToolArgumentBinderTests).GetMethod(nameof(AllOptional));
            Assert.Equal("x", ToolArgumentBinder.Bind(m, J(json))[0]);
        }

        [Fact]
        public void AbsentArguments_Valid()
        {
            var m = typeof(ToolArgumentBinderTests).GetMethod(nameof(AllOptional));
            Assert.Equal("x", ToolArgumentBinder.Bind(m, default)[0]);
        }

        [Theory]
        [InlineData("[1]", "array")]
        [InlineData("\"s\"", "string")]
        [InlineData("5", "number")]
        public void NonObjectArguments_Rejected(string json, string kind)
        {
            var msg = Err(json);
            Assert.Contains("must be a JSON object", msg);
            Assert.Contains(kind, msg);
        }
    }
}
