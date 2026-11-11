using System;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Contract tests for <see cref="PortRemoval"/>: which existing ports a call would remove,
    /// decided before anything changes. A non-empty set is what makes a call destructive, and a
    /// destructive call refuses to select its component by a default nickname; an empty set leaves
    /// ordinary resolution standing. The host glue (re-resolving with the destructive intent) is
    /// queued for operator verification; these pin the decision it acts on.
    /// </summary>
    public class PortRemovalTests
    {
        private static readonly Func<int, bool> Always = _ => true;
        private static readonly Func<int, bool> Never = _ => false;

        // ---- zoomable remove ----

        [Fact]
        public void ForRemove_ExplicitIndex_RemovesThatPort()
        {
            var r = PortRemoval.ForRemove(new[] { "D0", "D1", "D2" }, input: true, index: 1, Always);

            Assert.False(r.IsEmpty);
            Assert.Equal(new[] { "D1" }, r.Inputs);
            Assert.Empty(r.Outputs);
        }

        [Fact]
        public void ForRemove_NegativeIndex_RemovesTheLastPort()
        {
            var r = PortRemoval.ForRemove(new[] { "x", "y" }, input: false, index: -1, Always);

            Assert.Equal(new[] { "y" }, r.Outputs);
            Assert.Empty(r.Inputs);
        }

        [Theory]
        [InlineData(3)]
        [InlineData(99)]
        public void ForRemove_IndexOutOfRange_RemovesNothing(int index)
        {
            // The call fails its own range check without touching the component, so it is not
            // destructive and must not be refused for its id instead of for its index.
            Assert.True(PortRemoval.ForRemove(new[] { "a", "b", "c" }, true, index, Always).IsEmpty);
        }

        [Fact]
        public void ForRemove_EmptySide_RemovesNothing()
        {
            Assert.True(PortRemoval.ForRemove(Array.Empty<string>(), true, -1, Always).IsEmpty);
        }

        [Fact]
        public void ForRemove_ComponentRefusesTheRemoval_RemovesNothing()
        {
            Assert.True(PortRemoval.ForRemove(new[] { "a", "b" }, true, 0, Never).IsEmpty);
        }

        [Fact]
        public void ForRemove_AsksTheComponentAboutTheResolvedIndex()
        {
            int asked = -1;
            PortRemoval.ForRemove(new[] { "a", "b", "c" }, true, -1, i => { asked = i; return true; });

            Assert.Equal(2, asked);
        }

        // ---- zoomable set_count ----

        [Fact]
        public void ForSetCount_Down_RemovesThePortsPastTheTarget()
        {
            var r = PortRemoval.ForSetCount(new[] { "D0", "D1", "D2", "D3" }, input: true, targetCount: 2, Always);

            Assert.Equal(new[] { "D2", "D3" }, r.Inputs);
        }

        [Fact]
        public void ForSetCount_ToZero_RemovesEveryPort()
        {
            var r = PortRemoval.ForSetCount(new[] { "a", "b" }, input: false, targetCount: 0, Always);

            Assert.Equal(new[] { "a", "b" }, r.Outputs);
        }

        [Fact]
        public void ForSetCount_Equal_RemovesNothing()
        {
            Assert.True(PortRemoval.ForSetCount(new[] { "a", "b" }, true, 2, Always).IsEmpty);
        }

        [Fact]
        public void ForSetCount_Up_RemovesNothing()
        {
            // Growing a Merge from 2 inputs to 5 cuts no wire, so a unique default nickname still
            // selects it.
            Assert.True(PortRemoval.ForSetCount(new[] { "a", "b" }, true, 5, Always).IsEmpty);
        }

        [Fact]
        public void ForSetCount_NegativeTarget_RemovesNothing()
        {
            Assert.True(PortRemoval.ForSetCount(new[] { "a", "b" }, true, -1, Always).IsEmpty);
        }

        [Fact]
        public void ForSetCount_LastPortCannotBeRemoved_RemovesNothing()
        {
            // set_count removes from the end and stops at the first refusal, so a refused last port
            // means it removes nothing at all.
            int asked = -1;
            var r = PortRemoval.ForSetCount(new[] { "a", "b", "c" }, true, 1, i => { asked = i; return false; });

            Assert.True(r.IsEmpty);
            Assert.Equal(2, asked);
        }

        // ---- configure (name-matched sync) ----

        [Fact]
        public void ForSync_DroppingAPort_RemovesIt()
        {
            var r = PortRemoval.ForSync(new[] { "x", "y", "z" }, new[] { "x", "z" }, new[] { "a" }, null);

            Assert.Equal(new[] { "y" }, r.Inputs);
            Assert.Empty(r.Outputs);
        }

        [Fact]
        public void ForSync_KeepingEveryPort_RemovesNothing()
        {
            var r = PortRemoval.ForSync(new[] { "x", "y" }, new[] { "x", "y" }, new[] { "a" }, new[] { "a" });

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSync_AddingPortsOnly_RemovesNothing()
        {
            var r = PortRemoval.ForSync(new[] { "x" }, new[] { "x", "y" }, new[] { "a" }, new[] { "a", "b" });

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSync_RenamingAPort_RemovesTheOldName()
        {
            // The sync matches by name, so a rename is a removal plus an insertion: the old port's
            // wires are cut, which is what makes it destructive.
            var r = PortRemoval.ForSync(new[] { "x", "y" }, new[] { "x", "radius" }, Array.Empty<string>(), null);

            Assert.Equal(new[] { "y" }, r.Inputs);
        }

        [Fact]
        public void ForSync_OmittedSide_IsLeftAlone()
        {
            // null means "side not supplied", which configure leaves untouched with its wires.
            var r = PortRemoval.ForSync(new[] { "x", "y" }, null, new[] { "a", "b" }, null);

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSync_EmptyTarget_ClearsTheSide()
        {
            // "[]" clears a side, so every existing port on it is removed.
            var r = PortRemoval.ForSync(new[] { "x" }, null, new[] { "a", "b" }, Array.Empty<string>());

            Assert.Equal(new[] { "a", "b" }, r.Outputs);
        }

        [Fact]
        public void ForSync_ReportsRemovedPortsInPortOrder()
        {
            var r = PortRemoval.ForSync(new[] { "a", "b", "c", "d" }, new[] { "c" }, Array.Empty<string>(), null);

            Assert.Equal(new[] { "a", "b", "d" }, r.Inputs);
        }

        // ---- set (sync derived from code) ----

        [Fact]
        public void ForSourceSync_SignatureDropsAnInput_RemovesIt()
        {
            const string code = "private void RunScript(double x, ref object a)\n{\n  a = x;\n}";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("CSharpComponent", code, new[] { "x", "y" }, new[] { "a" }));

            Assert.Equal(new[] { "y" }, r.Inputs);
            Assert.Empty(r.Outputs);
        }

        [Fact]
        public void ForSourceSync_SignatureRenamesAnOutput_RemovesTheOldName()
        {
            const string code = "private void RunScript(double x, ref object result) { }";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("CSharpComponent", code, new[] { "x" }, new[] { "a" }));

            Assert.Equal(new[] { "a" }, r.Outputs);
        }

        [Fact]
        public void ForSourceSync_SignatureMatchesThePorts_RemovesNothing()
        {
            const string code = "private void RunScript(double x, double y, ref object a) { a = x + y; }";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("CSharpComponent", code, new[] { "x", "y" }, new[] { "a" }));

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSourceSync_SignatureAddsAPort_RemovesNothing()
        {
            const string code = "private void RunScript(double x, double y, double z, ref object a) { }";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("CSharpComponent", code, new[] { "x", "y" }, new[] { "a" }));

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSourceSync_PythonComponent_RemovesNothing()
        {
            // Python ports are managed by zooming, not derived from the source, so set never syncs them.
            const string code = "def RunScript(x):\n    return x";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("Python3Component", code, new[] { "x", "y" }, new[] { "a" }));

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSourceSync_PythonInTheUnifiedScriptComponent_RemovesNothing()
        {
            // The unified Script component's type name names no language. Python there must not
            // read as a C# signature with no typed parameters, which would remove every input.
            const string code = "#! python 3\ndef RunScript(self, x, y):\n    return x";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("ScriptComponent", code, new[] { "x", "y" }, new[] { "a" }));

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSourceSync_PythonDirectiveWithoutDef_RemovesNothing()
        {
            // The source as it will be stored keeps the component's language line, so a Python body
            // that calls RunScript without defining it is still Python, not an untyped C# signature.
            const string code = "#! python 3\nresult = RunScript(x, y)";
            var r = PortRemoval.ForSourceSync(SignatureSync.For("ScriptComponent", code, new[] { "x", "y" }, new[] { "a" }));

            Assert.True(r.IsEmpty);
        }

        [Fact]
        public void ForSourceSync_NoSignature_RemovesNothing()
        {
            // Top-level statements declare no ports, so the sync leaves them alone.
            var r = PortRemoval.ForSourceSync(SignatureSync.For("CSharpComponent", "a = x + 1;", new[] { "x", "y" }, new[] { "a" }));

            Assert.True(r.IsEmpty);
        }

        // ---- the refusal text ----

        [Fact]
        public void Describe_OneSide_NamesThePort()
        {
            var r = PortRemoval.ForRemove(new[] { "D0", "D1" }, true, 1, Always);

            Assert.Equal("input 'D1'", r.Describe());
        }

        [Fact]
        public void Describe_BothSides_NamesEveryPort()
        {
            var r = PortRemoval.ForSync(new[] { "a", "b" }, Array.Empty<string>(), new[] { "c" }, Array.Empty<string>());

            Assert.Equal("inputs 'a', 'b' and output 'c'", r.Describe());
        }

        [Fact]
        public void Refusal_SaysWhatWouldBeRemoved_ThenCarriesTheResolverError()
        {
            var r = PortRemoval.ForRemove(new[] { "D0", "D1" }, true, 1, Always);
            const string resolverError = "'Merge' is the default nickname of 1a2b3c4d Merge. Pass id '1a2b3c4d'";

            var text = r.Refusal(resolverError);

            Assert.StartsWith("This call removes input 'D1', and every wire on it, so nothing was changed.", text);
            Assert.EndsWith(resolverError, text);
        }

        [Fact]
        public void Refusal_SeveralPorts_SpeaksOfThem()
        {
            var r = PortRemoval.ForSetCount(new[] { "a", "b", "c" }, true, 1, Always);

            Assert.Contains("every wire on them", r.Refusal("x"));
        }

        [Fact]
        public void None_IsEmpty()
        {
            Assert.True(PortRemoval.None.IsEmpty);
            Assert.Equal(string.Empty, PortRemoval.None.Describe());
        }
    }
}
