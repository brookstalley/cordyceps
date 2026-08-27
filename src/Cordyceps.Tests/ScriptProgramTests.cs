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
        public void Rebuild_NullComponent_IsReportedNotThrown()
        {
            var result = ScriptProgram.Rebuild(null);

            Assert.False(result.Rebuilt);
            Assert.Contains("no component", result.Reason);
        }

        [Fact]
        public void CanRebuild_DistinguishesTheShapes()
        {
            Assert.True(ScriptProgram.CanRebuild(new ScriptComponent()));
            Assert.False(ScriptProgram.CanRebuild(new HookLessComponent()));
            Assert.False(ScriptProgram.CanRebuild(new LookalikeComponent()));
            Assert.False(ScriptProgram.CanRebuild(null));
        }

        #endregion

        #region Reading the running program

        [Fact]
        public void TryReadRunningSource_PrefersTheStringTextOverTheContainerOfTheSameName()
        {
            var component = new ScriptComponent(new FakeCode(RunningSource));

            Assert.True(ScriptProgram.TryReadRunningSource(component, out var source));
            Assert.Equal(RunningSource, source);
        }

        [Fact]
        public void TryReadRunningSource_ComponentThatHasNeverBuilt_ReportsNothing()
        {
            // No built code yet: there is no running program, and saying so is the honest answer.
            var component = new ScriptComponent(code: null);

            Assert.False(ScriptProgram.TryReadRunningSource(component, out var source));
            Assert.Null(source);
        }

        [Fact]
        public void TryReadRunningSource_HookLessComponent_ReportsNothing()
        {
            Assert.False(ScriptProgram.TryReadRunningSource(new HookLessComponent(), out var source));
            Assert.Null(source);
        }

        [Fact]
        public void TryReadRunningSource_NullComponent_ReportsNothing()
        {
            Assert.False(ScriptProgram.TryReadRunningSource(null, out var source));
            Assert.Null(source);
        }

        [Fact]
        public void TryReadRunningSource_CodeWithoutReadableText_ReportsNothing()
        {
            var component = new ScriptComponent(new object());

            Assert.False(ScriptProgram.TryReadRunningSource(component, out var source));
            Assert.Null(source);
        }

        #endregion
    }
}
