using System;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ScriptProgram"/>, which asks a script component to rebuild after
    /// <c>gh_script</c> writes source to it and reads back the program it will actually run.
    ///
    /// <para>The fakes mirror the real shapes. Rhino 8's script component implements
    /// <c>IScriptObject</c> <em>explicitly</em> — its members are invisible on the class and
    /// reachable only through the interface — and its built <c>Code</c> carries both a public
    /// non-string <c>Text</c> container and an explicitly implemented string <c>ICode.Text</c>.
    /// Both traps are reproduced below, because a matcher that falls for either one degrades to a
    /// silent no-op against the real host, which is the failure this module exists to end.</para>
    /// </summary>
    public class ScriptProgramTests
    {
        private const string RunningSource = "// #! csharp\na = 1;";

        #region Fakes

        /// <summary>The subset of Rhino's script-object interface this module drives.</summary>
        private interface IScriptObject
        {
            void Expire();
            void ReBuild();
            void ReBuild(int buildKind);
            bool TryGetCode(out object code);
        }

        /// <summary>Rhino's <c>ICode</c> shape: the string text lives here, not on the class.</summary>
        private interface ICode
        {
            string Text { get; }
        }

        /// <summary>
        /// Rhino's <c>Code</c> shape: a public <c>Text</c> of a container type shadowing the string
        /// <c>Text</c> that the explicitly implemented interface carries.
        /// </summary>
        private class FakeCode : ICode
        {
            public FakeCode(string text) => Source = text;

            public string Source { get; }

            /// <summary>The container property — same name, wrong type, deliberately first.</summary>
            public object Text => new object();

            string ICode.Text => Source;
        }

        /// <summary>Rhino 8 shape: explicit implementation, overloaded ReBuild, a built code.</summary>
        private class ScriptComponent : IScriptObject
        {
            public ScriptComponent(object code = null) => Code = code;

            public object Code { get; set; }
            public int ExpireCalls { get; private set; }
            public int ReBuildCalls { get; private set; }
            public string CallOrder { get; private set; } = "";

            void IScriptObject.Expire()
            {
                ExpireCalls++;
                CallOrder += "E";
            }

            void IScriptObject.ReBuild()
            {
                ReBuildCalls++;
                CallOrder += "R";
            }

            void IScriptObject.ReBuild(int buildKind) => throw new InvalidOperationException(
                "The no-argument overload is the one a rebuild request must select.");

            bool IScriptObject.TryGetCode(out object code)
            {
                code = Code;
                return Code != null;
            }
        }

        /// <summary>A component whose rebuild hook fails the way a host exception would.</summary>
        private class ThrowingScriptComponent : IScriptObject
        {
            void IScriptObject.Expire() { }

            void IScriptObject.ReBuild() => throw new InvalidOperationException("language not ready");

            void IScriptObject.ReBuild(int buildKind) { }

            bool IScriptObject.TryGetCode(out object code)
            {
                code = null;
                return false;
            }
        }

        /// <summary>
        /// An unrelated interface with the same simple name, nested so both can coexist. Matching
        /// on the name alone would drive rebuilds into this one; the member shape is what rejects it.
        /// </summary>
        private static class Unrelated
        {
            internal interface IScriptObject
            {
                void SomethingElse();
            }
        }

        private class LookalikeComponent : Unrelated.IScriptObject
        {
            void Unrelated.IScriptObject.SomethingElse() { }
        }

        /// <summary>
        /// A future Rhino promoting the hooks onto a base interface. The members are declared one
        /// level up, so a member search that reads only the matched interface's own declarations
        /// finds nothing and every rebuild silently degrades to a no-op.
        /// </summary>
        private static class Promoted
        {
            internal interface IScriptObjectBase
            {
                void Expire();
                void ReBuild();
                bool TryGetCode(out object code);
            }

            /// <summary>Same simple name the probe matches on; declares nothing itself.</summary>
            internal interface IScriptObject : IScriptObjectBase
            {
            }
        }

        private class InheritedHooksComponent : Promoted.IScriptObject
        {
            public InheritedHooksComponent(object code = null) => Code = code;

            public object Code { get; }
            public string CallOrder { get; private set; } = "";

            void Promoted.IScriptObjectBase.Expire() => CallOrder += "E";

            void Promoted.IScriptObjectBase.ReBuild() => CallOrder += "R";

            bool Promoted.IScriptObjectBase.TryGetCode(out object code)
            {
                code = Code;
                return Code != null;
            }
        }

        /// <summary>Rhino 7 GhPython shape: source lives on a Code property, no rebuild hooks.</summary>
        private class HookLessComponent
        {
            public string Code { get; set; } = "";
        }

        #endregion

        #region Rebuild

        [Fact]
        public void Rebuild_ExplicitInterface_ExpiresThenRebuilds()
        {
            var component = new ScriptComponent();

            var result = ScriptProgram.Rebuild(component);

            Assert.True(result.Rebuilt);
            Assert.Null(result.Reason);
            Assert.Equal(1, component.ExpireCalls);
            Assert.Equal(1, component.ReBuildCalls);
        }

        [Fact]
        public void Rebuild_ExpiresBeforeRebuilding()
        {
            // Order is load-bearing: rebuilding a component that still holds a cached compile
            // result can hand back the previous program.
            var component = new ScriptComponent();

            ScriptProgram.Rebuild(component);

            Assert.Equal("ER", component.CallOrder);
        }

        [Fact]
        public void Rebuild_HookLessComponent_ReportsReasonWithoutFailing()
        {
            var result = ScriptProgram.Rebuild(new HookLessComponent());

            Assert.False(result.Rebuilt);
            Assert.Contains("no rebuild hook", result.Reason);
            Assert.Contains(nameof(HookLessComponent), result.Reason);
            Assert.NotEmpty(result.Probes);
        }

        [Fact]
        public void Rebuild_InterfaceNameWithoutTheHooks_IsNotMistakenForTheRealOne()
        {
            var result = ScriptProgram.Rebuild(new LookalikeComponent());

            Assert.False(result.Rebuilt);
            Assert.Contains("without no-argument", string.Join(" ", result.Probes));
        }

        [Fact]
        public void Rebuild_ThrowingHook_ReportsTheHostExceptionAndDoesNotEscape()
        {
            var result = ScriptProgram.Rebuild(new ThrowingScriptComponent());

            Assert.False(result.Rebuilt);
            Assert.Contains("language not ready", result.Reason);
            Assert.Contains("gh_inspect", result.Reason);
        }

        [Fact]
        public void Rebuild_HooksOnABaseInterface_AreStillFound()
        {
            // The interface the component implements declares nothing itself; a search that does
            // not walk base interfaces reports "no rebuild hook" and the fix quietly stops working.
            var component = new InheritedHooksComponent();

            var result = ScriptProgram.Rebuild(component);

            Assert.True(result.Rebuilt);
            Assert.Equal("ER", component.CallOrder);
        }

        [Fact]
        public void CompareRunning_TryGetCodeOnABaseInterface_IsStillFound()
        {
            var component = new InheritedHooksComponent(new FakeCode(RunningSource));

            var comparison = ScriptProgram.CompareRunning(component, RunningSource);

            Assert.True(comparison.Readable);
            Assert.Equal(RunningSource, comparison.RunningSource);
        }

        [Fact]
        public void Rebuild_NullComponent_IsReportedNotThrown()
        {
            var result = ScriptProgram.Rebuild(null);

            Assert.False(result.Rebuilt);
            Assert.Contains("no component", result.Reason);
        }


        #endregion

        #region Reading the running program

        [Fact]
        public void CompareRunning_PrefersTheStringTextOverTheContainerOfTheSameName()
        {
            var component = new ScriptComponent(new FakeCode(RunningSource));

            var comparison = ScriptProgram.CompareRunning(component, RunningSource);

            Assert.True(comparison.Readable);
            Assert.False(comparison.Diverged);
            Assert.Equal(RunningSource, comparison.RunningSource);
        }

        [Fact]
        public void CompareRunning_RunningProgramDiffers_IsReportedAsDivergence()
        {
            var component = new ScriptComponent(new FakeCode(RunningSource));

            var comparison = ScriptProgram.CompareRunning(component, "// #! csharp\na = 2;");

            Assert.True(comparison.Readable);
            Assert.True(comparison.Diverged);
            Assert.Equal(RunningSource, comparison.RunningSource);
        }

        [Fact]
        public void CompareRunning_ComponentThatHasNeverBuilt_SaysWhyRatherThanClaimingAMatch()
        {
            // No built code yet: there is no running program, and saying so is the honest answer.
            var component = new ScriptComponent(code: null);

            var comparison = ScriptProgram.CompareRunning(component, RunningSource);

            Assert.False(comparison.Readable);
            Assert.False(comparison.Diverged);
            Assert.Null(comparison.RunningSource);
            Assert.Contains("has not built", comparison.Reason);
            Assert.NotEmpty(comparison.Probes);
        }

        [Fact]
        public void CompareRunning_HookLessComponent_SaysWhy()
        {
            var comparison = ScriptProgram.CompareRunning(new HookLessComponent(), RunningSource);

            Assert.False(comparison.Readable);
            Assert.Contains("no IScriptObject", comparison.Reason);
        }

        [Fact]
        public void CompareRunning_NullComponent_SaysWhy()
        {
            var comparison = ScriptProgram.CompareRunning(null, RunningSource);

            Assert.False(comparison.Readable);
            Assert.Contains("No component", comparison.Reason);
        }

        [Fact]
        public void CompareRunning_CodeWithoutReadableText_SaysWhy()
        {
            var component = new ScriptComponent(new object());

            var comparison = ScriptProgram.CompareRunning(component, RunningSource);

            Assert.False(comparison.Readable);
            Assert.Contains("no readable string Text", comparison.Reason);
        }

        #endregion
        #region Response fields

        // These cover the decision the tool response is built from: which fields appear, and what
        // an absent one means. An inverted condition here reports agreement the component never
        // confirmed — the false confirmation issue #33 was reported through.

        private static RunningSourceComparison Matching()
            => ScriptProgram.CompareRunning(new ScriptComponent(new FakeCode(RunningSource)), RunningSource);

        private static RunningSourceComparison Diverged()
            => ScriptProgram.CompareRunning(new ScriptComponent(new FakeCode(RunningSource)), "// #! csharp\nb = 9;");

        private static RunningSourceComparison Unreadable()
            => ScriptProgram.CompareRunning(new ScriptComponent(code: null), RunningSource);

        [Fact]
        public void DescribeWrite_RebuiltAndMatching_ReportsVerifiedWithNoDivergence()
        {
            var fields = ScriptProgram.DescribeWrite(ScriptProgram.Rebuild(new ScriptComponent()), Matching());

            Assert.Equal(true, fields["rebuilt"]);
            Assert.Equal(true, fields["verified"]);
            Assert.False(fields.ContainsKey("sourceDiverged"));
            Assert.False(fields.ContainsKey("runningSource"));
            Assert.False(fields.ContainsKey("verificationSkipped"));
            Assert.False(fields.ContainsKey("rebuildSkipped"));
            Assert.False(fields.ContainsKey("rebuildFailed"));
        }

        [Fact]
        public void DescribeWrite_RunningProgramDiffers_SpeaksBothVocabularies()
        {
            // An agent that learned sourceDiverged from a 'get' response must find it here too,
            // or it reads the absence as agreement.
            var fields = ScriptProgram.DescribeWrite(ScriptProgram.Rebuild(new ScriptComponent()), Diverged());

            Assert.Equal(false, fields["verified"]);
            Assert.Equal(true, fields["sourceDiverged"]);
            Assert.Equal(RunningSource, fields["runningSource"]);
            Assert.Equal(ScriptProgram.DivergenceNote, fields["divergenceNote"]);
        }

        [Fact]
        public void DescribeWrite_UnreadableProgram_OmitsVerifiedAndSaysWhy()
        {
            var fields = ScriptProgram.DescribeWrite(ScriptProgram.Rebuild(new ScriptComponent()), Unreadable());

            Assert.False(fields.ContainsKey("verified"));
            Assert.Contains("has not built", (string)fields["verificationSkipped"]);
        }

        [Fact]
        public void DescribeWrite_NoRebuildHooks_IsSkippedNotFailed()
        {
            var fields = ScriptProgram.DescribeWrite(ScriptProgram.Rebuild(new HookLessComponent()), Unreadable());

            Assert.Equal(false, fields["rebuilt"]);
            Assert.True(fields.ContainsKey("rebuildSkipped"));
            Assert.False(fields.ContainsKey("rebuildFailed"));
        }

        [Fact]
        public void DescribeWrite_RebuildHookThrew_IsFailedNotSkipped()
        {
            // The opposite fact from having no hooks: this component is probably still running its
            // previous program, and a caller must be able to tell the two apart without parsing prose.
            var fields = ScriptProgram.DescribeWrite(ScriptProgram.Rebuild(new ThrowingScriptComponent()), Unreadable());

            Assert.Equal(false, fields["rebuilt"]);
            Assert.True(fields.ContainsKey("rebuildFailed"));
            Assert.False(fields.ContainsKey("rebuildSkipped"));
        }

        [Fact]
        public void DescribeRead_Matching_StatesTheAgreementRatherThanImplyingIt()
        {
            var fields = ScriptProgram.DescribeRead(Matching());

            Assert.Equal(false, fields["sourceDiverged"]);
            Assert.False(fields.ContainsKey("runningSource"));
            Assert.False(fields.ContainsKey("runningSourceUnavailable"));
        }

        [Fact]
        public void DescribeRead_Diverged_CarriesTheRunningProgram()
        {
            var fields = ScriptProgram.DescribeRead(Diverged());

            Assert.Equal(true, fields["sourceDiverged"]);
            Assert.Equal(RunningSource, fields["runningSource"]);
            Assert.Equal(ScriptProgram.DivergenceNote, fields["divergenceNote"]);
        }

        [Fact]
        public void DescribeRead_Unreadable_SaysSoInsteadOfLookingLikeAgreement()
        {
            var fields = ScriptProgram.DescribeRead(Unreadable());

            Assert.False(fields.ContainsKey("sourceDiverged"));
            Assert.Contains("has not built", (string)fields["runningSourceUnavailable"]);
        }

        #endregion
    }
}
