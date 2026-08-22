using System;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ScriptSourceWriter"/>, the probe-and-write cascade
    /// <c>gh_script</c> uses to store source on a script component.
    ///
    /// <para>The fakes below stand in for the real component shapes: Rhino 8's unified Script
    /// component (a <c>SetSource(string)</c> method), Rhino 7's GhPython component (no
    /// <c>SetSource</c>, a writable <c>Code</c> property guarded by <c>HiddenCodeInput</c>), and
    /// third-party components that expose neither. Grasshopper types cannot load in a unit test,
    /// so the writer takes <see cref="object"/> and reflects — which is exactly what makes the
    /// whole cascade, writes included, testable here.</para>
    /// </summary>
    public class ScriptSourceWriterTests
    {
        private const string Source = "#! python 3\nprint('hello')";

        #region Fakes

        /// <summary>Rhino 8 shape: a plain <c>SetSource(string)</c> method.</summary>
        private class SetSourceComponent
        {
            public string Written;
            public void SetSource(string source) => Written = source;
        }

        /// <summary>
        /// Rhino 7 GhPython shape: no <c>SetSource</c>, a writable <c>Code</c> property whose
        /// setter refuses while a visible code input parameter exists.
        /// </summary>
        private class CodePropertyComponent
        {
            private string _code = "";

            public CodePropertyComponent(bool hiddenCodeInput) => HiddenCodeInput = hiddenCodeInput;

            public bool HiddenCodeInput { get; }

            public string Code
            {
                get => _code;
                set
                {
                    if (!HiddenCodeInput)
                        throw new InvalidOperationException("Cannot assign to code while code parameter exists");
                    _code = value ?? string.Empty;
                }
            }
        }

        /// <summary>A writable <c>Code</c> property with no <c>HiddenCodeInput</c> concept at all.</summary>
        private class CodePropertyWithoutGuardComponent
        {
            public string Code { get; set; } = "";
        }

        /// <summary><c>HiddenCodeInput</c> exists but is not public — the guard must still be read.</summary>
        private class NonPublicGuardComponent
        {
            public string Code { get; set; } = "";
            private bool HiddenCodeInput => false;
        }

        /// <summary>A <c>HiddenCodeInput</c> getter that throws — unreadable, not evidence of a guard.</summary>
        private class ThrowingGuardComponent
        {
            public string Code { get; set; } = "";
            public bool HiddenCodeInput => throw new InvalidOperationException("no active document");
        }

        /// <summary>Neither writable member — e.g. a plain panel or a non-script component.</summary>
        private class NoWritableSourceComponent
        {
            public string Code => "read only";
        }

        /// <summary>Both members present: the cascade must prefer <c>SetSource</c>.</summary>
        private class BothMembersComponent
        {
            public string Code { get; set; } = "";
            public string WrittenViaSetSource;
            public void SetSource(string source) => WrittenViaSetSource = source;
        }

        /// <summary>Overloaded <c>SetSource</c>: the string overload must be selected.</summary>
        private class OverloadedSetSourceComponent
        {
            public string WrittenString;
            public int WrittenInt = -1;
            public void SetSource(int handle) => WrittenInt = handle;
            public void SetSource(string source) => WrittenString = source;
        }

        /// <summary>A <c>SetSource(object)</c> a string is still assignable to.</summary>
        private class AssignableSetSourceComponent
        {
            public object Written;
            public void SetSource(object source) => Written = source;
        }

        /// <summary>A <c>SetSource</c> that cannot take a string — not a usable write path.</summary>
        private class WrongAritySetSourceComponent
        {
            public void SetSource(string source, bool compile) { }
        }

        /// <summary>Base of a shadowed-<c>Code</c> hierarchy.</summary>
        private class ShadowedCodeBase
        {
            public string Code { get; set; } = "";
        }

        /// <summary>
        /// A derived component that redeclares <c>Code</c> with <c>new</c>. Reflection's name
        /// lookup is ambiguous here; the most-derived declaration must win.
        /// </summary>
        private class ShadowedCodeComponent : ShadowedCodeBase
        {
            public string DerivedCode = "";

            public new string Code
            {
                get => DerivedCode;
                set => DerivedCode = value;
            }
        }

        /// <summary>A <c>SetSource(string)</c> that throws — the failure must surface, not vanish.</summary>
        private class ThrowingSetSourceComponent
        {
            public void SetSource(string source) => throw new InvalidOperationException("component is locked");
        }

        #endregion

        [Fact]
        public void Write_WithSetSourceMethod_UsesIt()
        {
            var component = new SetSourceComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.SetSourceMethod, result.Method);
            Assert.Null(result.Error);
            Assert.Equal(Source, component.Written);
        }

        [Fact]
        public void Write_WithBothMembers_PrefersSetSource()
        {
            // Preserves the pre-cascade behavior exactly for every component that has SetSource:
            // the Code property is a fallback, never a substitute.
            var component = new BothMembersComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.SetSourceMethod, result.Method);
            Assert.Equal(Source, component.WrittenViaSetSource);
            Assert.Equal("", component.Code);
        }

        [Fact]
        public void Write_WithOverloadedSetSource_SelectsTheStringOverload()
        {
            // GetMethod(name) throws AmbiguousMatchException on overloads; the cascade must choose.
            var component = new OverloadedSetSourceComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(Source, component.WrittenString);
            Assert.Equal(-1, component.WrittenInt);
        }

        [Fact]
        public void Write_WithSetSourceTakingObject_StillUsesIt()
        {
            var component = new AssignableSetSourceComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.SetSourceMethod, result.Method);
            Assert.Equal(Source, component.Written);
        }

        [Fact]
        public void Write_WithUnusableSetSourceArity_FallsThroughToNotFound()
        {
            // A two-argument SetSource is not the member we mean; it must not be treated as one.
            var result = ScriptSourceWriter.Write(new WrongAritySetSourceComponent(), Source);

            Assert.False(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.None, result.Method);
            Assert.Contains("WrongAritySetSourceComponent", result.Error);
        }

        [Fact]
        public void Write_WithoutSetSourceAndHiddenCodeInputTrue_UsesCodeProperty()
        {
            // The Rhino 7 GhPython case on a stock component: the code input param is hidden,
            // so the Code setter accepts the assignment.
            var component = new CodePropertyComponent(hiddenCodeInput: true);

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.CodeProperty, result.Method);
            Assert.Null(result.Error);
            Assert.Equal(Source, component.Code);
        }

        [Fact]
        public void Write_WithHiddenCodeInputFalse_ReturnsActionableErrorAndDoesNotWrite()
        {
            // The pre-check is the whole point: without it the caller gets the setter's opaque
            // "Cannot assign to code while code parameter exists" and no idea what to do about it.
            var component = new CodePropertyComponent(hiddenCodeInput: false);

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.False(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.CodeProperty, result.Method);
            Assert.Equal("", component.Code);

            Assert.Contains("CodePropertyComponent", result.Error);
            Assert.Contains("code input parameter", result.Error);
            Assert.Contains("remove", result.Error, StringComparison.OrdinalIgnoreCase);
            // The opaque host message must not be what the caller is handed.
            Assert.DoesNotContain("Cannot assign to code while code parameter exists", result.Error);
        }

        [Fact]
        public void Write_WithNonPublicHiddenCodeInput_StillHonorsTheGuard()
        {
            // GhPython's guard is an implementation detail of the component; visibility must not
            // decide whether we check it.
            var component = new NonPublicGuardComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.False(result.Success);
            Assert.Contains("code input parameter", result.Error);
            Assert.Equal("", component.Code);
        }

        [Fact]
        public void Write_WithNoHiddenCodeInputMember_WritesAnyway()
        {
            // Documented default: a component that does not expose the concept has no such
            // precondition, so absence must not block the write. If its setter does object, the
            // exception is caught and surfaced (see Write_WhenSetterThrows_*), never swallowed.
            var component = new CodePropertyWithoutGuardComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.CodeProperty, result.Method);
            Assert.Equal(Source, component.Code);
            Assert.Contains(result.Probes, p => p.Contains("no readable bool HiddenCodeInput"));
        }

        [Fact]
        public void Write_WithUnreadableHiddenCodeInput_WritesAnyway()
        {
            // Same default as absence: a guard we could not read is not evidence of a guard.
            var component = new ThrowingGuardComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(Source, component.Code);
            Assert.Contains(result.Probes, p => p.Contains("HiddenCodeInput getter threw"));
        }

        [Fact]
        public void Write_WithShadowedCodeProperty_UsesTheMostDerivedDeclaration()
        {
            // A shadowed property makes reflection's name lookup ambiguous, and an unhandled
            // AmbiguousMatchException there would defeat the point of a defensive cascade.
            var component = new ShadowedCodeComponent();

            var result = ScriptSourceWriter.Write(component, Source);

            Assert.True(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.CodeProperty, result.Method);
            Assert.Equal(Source, component.DerivedCode);
            Assert.Equal("", ((ShadowedCodeBase)component).Code);
        }

        [Fact]
        public void Write_WithNoWritableMember_ReturnsErrorNamingTypeAndProbes()
        {
            var result = ScriptSourceWriter.Write(new NoWritableSourceComponent(), Source);

            Assert.False(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.None, result.Method);
            Assert.Contains("NoWritableSourceComponent", result.Error);
            Assert.Contains("SetSource(string)", result.Error);
            Assert.Contains("Code property", result.Error);
        }

        [Fact]
        public void Write_WhenSetSourceThrows_ReportsFailureWithTheHostMessage()
        {
            // Never a silent success: a throwing host member is a failed write, reported as one.
            var result = ScriptSourceWriter.Write(new ThrowingSetSourceComponent(), Source);

            Assert.False(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.SetSourceMethod, result.Method);
            Assert.Contains("ThrowingSetSourceComponent", result.Error);
            Assert.Contains("component is locked", result.Error);
        }

        [Fact]
        public void Write_WithNullComponent_FailsInsteadOfThrowing()
        {
            var result = ScriptSourceWriter.Write(null, Source);

            Assert.False(result.Success);
            Assert.Equal(ScriptSourceWriteMethod.None, result.Method);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }

        [Fact]
        public void Write_StoresTheSourceVerbatim()
        {
            // The language directive is preserved by the caller before the write; the writer must
            // store exactly what it is handed, directive and all.
            const string withDirective = "// #! csharp\npublic void RunScript() { }";
            var component = new SetSourceComponent();

            ScriptSourceWriter.Write(component, withDirective);

            Assert.Equal(withDirective, component.Written);
        }

        [Fact]
        public void Write_AlwaysReturnsAProbeTrace()
        {
            // The trace is what makes a miss diagnosable from gh_inspect(action='log').
            Assert.NotEmpty(ScriptSourceWriter.Write(new SetSourceComponent(), Source).Probes);
            Assert.NotEmpty(ScriptSourceWriter.Write(new NoWritableSourceComponent(), Source).Probes);
            Assert.NotEmpty(ScriptSourceWriter.Write(new CodePropertyComponent(true), Source).Probes);
        }

        [Theory]
        [InlineData(typeof(SetSourceComponent), true)]
        [InlineData(typeof(CodePropertyWithoutGuardComponent), true)]
        [InlineData(typeof(AssignableSetSourceComponent), true)]
        [InlineData(typeof(NoWritableSourceComponent), false)]
        [InlineData(typeof(WrongAritySetSourceComponent), false)]
        public void CanWrite_MatchesWhetherAWritePathExists(Type componentType, bool expected)
        {
            // CanWrite is what lets gh_script recognize a Code-only script component instead of
            // rejecting it as "not a script component" before the fallback can run.
            Assert.Equal(expected, ScriptSourceWriter.CanWrite(Activator.CreateInstance(componentType)));
        }

        [Fact]
        public void CanWrite_WithGuardedCodeProperty_IsTrue()
        {
            // The guard governs whether a write is permitted right now, not whether the component
            // has a write path at all — otherwise a guarded component would be misreported as
            // "not a script component" instead of getting the actionable guard message.
            Assert.True(ScriptSourceWriter.CanWrite(new CodePropertyComponent(hiddenCodeInput: false)));
        }

        [Fact]
        public void CanWrite_WithNull_IsFalse()
        {
            Assert.False(ScriptSourceWriter.CanWrite(null));
        }
    }
}
