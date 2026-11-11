using System;
using System.Collections.Generic;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="NoChange"/>: which calls change nothing, and so open no edit and return
    /// no <c>undo</c>.
    /// </summary>
    public class NoChangeTests
    {
        private static readonly Guid A = Guid.NewGuid();
        private static readonly Guid B = Guid.NewGuid();
        private static readonly Guid C = Guid.NewGuid();
        private const int Red = unchecked((int)0xFFFF0000);
        private const int Blue = unchecked((int)0xFF0000FF);

        // ---------------------------------------------------------------- group_add

        [Fact]
        public void GroupAdd_EveryIdAMember_NoColour_ChangesNothing()
        {
            Assert.True(NoChange.GroupAdd(new[] { A, B, C }, new[] { B, A }, Red, null));
        }

        [Fact]
        public void GroupAdd_TheColourItAlreadyHas_ChangesNothing()
        {
            Assert.True(NoChange.GroupAdd(new[] { A }, new[] { A }, Red, Red));
        }

        [Fact]
        public void GroupAdd_ANewMember_IsAChange()
        {
            Assert.False(NoChange.GroupAdd(new[] { A }, new[] { A, B }, Red, null));
        }

        [Fact]
        public void GroupAdd_ANewColour_IsAChange_EvenWhenEveryIdIsAMember()
        {
            Assert.False(NoChange.GroupAdd(new[] { A, B }, new[] { A }, Red, Blue));
        }

        [Fact]
        public void GroupAdd_ToAGroupWithNoMembers_IsAChange()
        {
            Assert.False(NoChange.GroupAdd(null, new[] { A }, Red, null));
        }

        // ---------------------------------------------------------------- the source

        [Theory]
        [InlineData("a = 1;", "a = 1;", true)]
        [InlineData("a = 1;", "a = 2;", false)]
        // Case and line endings are part of the text.
        [InlineData("A = 1;", "a = 1;", false)]
        [InlineData("a = 1;\r\n", "a = 1;\n", false)]
        // A source that cannot be read is never assumed to be the requested one.
        [InlineData(null, "a = 1;", false)]
        [InlineData("a = 1;", null, false)]
        public void SameSource_IsExactText(string stored, string requested, bool expected)
        {
            Assert.Equal(expected, NoChange.SameSource(stored, requested));
        }

        // ---------------------------------------------------------------- set (and configure with only code)

        private const string CSharp = "// #! csharp\nprivate void RunScript(double x, double y, ref object a) { a = x + y; }";

        [Fact]
        public void ScriptSourceSync_SameSource_PortsAsDeclared_ChangesNothing()
        {
            Assert.True(NoChange.ScriptSourceSync("CSharpComponent", CSharp, CSharp, syncsPorts: true,
                new[] { "x", "y" }, new[] { "a" }));
        }

        [Fact]
        public void ScriptSourceSync_DifferentSource_IsAChange()
        {
            Assert.False(NoChange.ScriptSourceSync("CSharpComponent", CSharp + " ", CSharp, syncsPorts: true,
                new[] { "x", "y" }, new[] { "a" }));
        }

        /// <summary>
        /// The same source over ports someone changed by hand: the sync after the write would put
        /// the declared ports back, which is a change.
        /// </summary>
        [Theory]
        [InlineData(new[] { "x" }, new[] { "a" })]
        [InlineData(new[] { "y", "x" }, new[] { "a" })]
        [InlineData(new[] { "x", "y" }, new string[0])]
        [InlineData(new[] { "x", "Y" }, new[] { "a" })]
        public void ScriptSourceSync_SameSource_PortsTheSyncWouldMove_IsAChange(string[] inputs, string[] outputs)
        {
            Assert.False(NoChange.ScriptSourceSync("CSharpComponent", CSharp, CSharp, syncsPorts: true, inputs, outputs));
        }

        [Fact]
        public void ScriptSourceSync_SameSource_OnAComponentThatCannotVaryItsPorts_ChangesNothing()
        {
            Assert.True(NoChange.ScriptSourceSync("CSharpComponent", CSharp, CSharp, syncsPorts: false,
                new[] { "anything" }, new string[0]));
        }

        [Fact]
        public void ScriptSourceSync_SameSource_WithNoSignature_LeavesThePortsAlone()
        {
            const string python = "#! python 3\na = x + y";
            Assert.True(NoChange.ScriptSourceSync("ScriptComponent", python, python, syncsPorts: true,
                new[] { "x", "y" }, new[] { "a" }));
        }

        // ---------------------------------------------------------------- configure with inputs/outputs

        private static readonly IReadOnlyList<(string Name, string Access)> XListY =
            new List<(string, string)> { ("x", "list"), ("y", "item") };

        private static List<ScriptParamDef> Defs(params (string name, string type, string access)[] defs)
        {
            var list = new List<ScriptParamDef>();
            foreach (var (name, type, access) in defs)
                list.Add(new ScriptParamDef { Name = name, Type = type, Access = access });
            return list;
        }

        [Fact]
        public void ScriptParameters_SameNames_NoTypeOrAccess_ChangesNothing()
        {
            Assert.True(NoChange.ScriptParameters(XListY, new[] { "a" },
                Defs(("x", null, null), ("y", null, null)), Defs(("a", null, null)), "src", null));
        }

        [Fact]
        public void ScriptParameters_TheAccessEachInputHas_ChangesNothing()
        {
            Assert.True(NoChange.ScriptParameters(XListY, new[] { "a" },
                Defs(("x", null, "LIST"), ("y", null, "item")), null, "src", null));
        }

        [Fact]
        public void ScriptParameters_AnOmittedSide_IsLeftAlone()
        {
            Assert.True(NoChange.ScriptParameters(XListY, new[] { "a", "b" },
                null, Defs(("a", null, null), ("b", null, null)), "src", null));
        }

        [Theory]
        // A rename, a reorder, a dropped port, a new port.
        [InlineData("x", "z")]
        [InlineData("y", "x")]
        [InlineData("x", null)]
        public void ScriptParameters_DifferentInputNames_IsAChange(string first, string second)
        {
            var defs = second == null ? Defs((first, null, null)) : Defs((first, null, null), (second, null, null));
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" }, defs, null, "src", null));
        }

        [Fact]
        public void ScriptParameters_ClearingASideThatHasPorts_IsAChange()
        {
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" }, null, Defs(), "src", null));
        }

        [Fact]
        public void ScriptParameters_ANewAccess_IsAChange()
        {
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" },
                Defs(("x", null, "tree"), ("y", null, null)), null, "src", null));
        }

        /// <summary>An unknown access word sets item, as configure applies it, so on a list input it is a change.</summary>
        [Fact]
        public void ScriptParameters_AnUnknownAccess_ReadsAsItem()
        {
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" },
                Defs(("x", null, "bogus"), ("y", null, null)), null, "src", null));
            Assert.True(NoChange.ScriptParameters(XListY, new[] { "a" },
                Defs(("x", null, null), ("y", null, "bogus")), null, "src", null));
        }

        /// <summary>A type hint cannot be compared without setting it, so any type counts as a change.</summary>
        [Fact]
        public void ScriptParameters_AnyType_IsAChange()
        {
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" },
                Defs(("x", "double", null), ("y", null, null)), null, "src", null));
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" },
                null, Defs(("a", "Point3d", null)), "src", null));
        }

        [Fact]
        public void ScriptParameters_WithCode_NeedsTheSameSource()
        {
            var same = Defs(("x", null, null), ("y", null, null));
            Assert.True(NoChange.ScriptParameters(XListY, new[] { "a" }, same, null, "a = x;", "a = x;"));
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" }, same, null, "a = x;", "a = y;"));
            Assert.False(NoChange.ScriptParameters(XListY, new[] { "a" }, same, null, null, "a = x;"));
        }

        [Theory]
        [InlineData("list", "list")]
        [InlineData("TREE", "tree")]
        [InlineData("item", "item")]
        [InlineData("anything", "item")]
        [InlineData(null, "item")]
        public void AccessOf_IsWhatConfigureApplies(string requested, string expected)
        {
            Assert.Equal(expected, NoChange.AccessOf(requested));
        }
    }
}
