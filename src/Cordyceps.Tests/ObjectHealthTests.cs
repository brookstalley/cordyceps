using System;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// <see cref="ObjectHealth"/>, the one rule <c>status</c> and <c>outline</c> share: Error, then
    /// Warning, then DISCONNECTED (a required input with no source and no data), else OK. Remarks are
    /// ignored and parameter objects never read as DISCONNECTED. The message text is the one
    /// <c>status</c> prints, byte for byte.
    /// </summary>
    public class ObjectHealthTests
    {
        private static HealthInput Required(string name, int sources = 0, int data = 0) => new HealthInput(name, false, sources, data);
        private static HealthInput Optional(string name) => new HealthInput(name, true, 0, 0);

        [Fact]
        public void ErrorLevel_IsError_WithEveryErrorJoined()
        {
            var r = ObjectHealth.Classify(MessageLevel.Error, new[] { "bad A", "bad B" }, new[] { "warn" }, new[] { Required("X") }, true);
            Assert.Equal(HealthStatus.Error, r.Status);
            Assert.Equal("ERROR", r.StatusText);
            Assert.Equal(new[] { "bad A", "bad B" }, r.Messages);
            Assert.Equal("bad A; bad B", r.Message);
            Assert.Empty(r.MissingInputs);
        }

        [Fact]
        public void WarningLevel_IsWarning_AndOutranksDisconnected()
        {
            var r = ObjectHealth.Classify(MessageLevel.Warning, null, new[] { "1. Input parameter A failed to collect data" },
                new[] { Required("A") }, true);
            Assert.Equal(HealthStatus.Warning, r.Status);
            Assert.Equal("WARNING", r.StatusText);
            Assert.Equal("1. Input parameter A failed to collect data", r.Message);
            Assert.Empty(r.MissingInputs);
        }

        [Fact]
        public void RequiredInputWithNoSourceAndNoData_IsDisconnected_ListingItsName()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("A"), Required("B", sources: 1), Required("C") }, true);
            Assert.Equal(HealthStatus.Disconnected, r.Status);
            Assert.Equal("DISCONNECTED", r.StatusText);
            Assert.Equal(new[] { "A", "C" }, r.MissingInputs);
            Assert.Equal("Missing: A, C", r.Message);
        }

        [Fact]
        public void OptionalInputs_AreIgnored()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Optional("A"), Required("B", sources: 1) }, true);
            Assert.Equal(HealthStatus.Ok, r.Status);
            Assert.Equal("OK", r.StatusText);
            Assert.Equal(string.Empty, r.Message);
        }

        [Fact]
        public void InputWiredWithNoData_IsNotDisconnected_BecauseItHasASource()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("A", sources: 1, data: 0) }, true);
            Assert.Equal(HealthStatus.Ok, r.Status);
        }

        [Fact]
        public void InputWithStoredDataButNoSource_IsNotDisconnected()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("A", sources: 0, data: 1) }, true);
            Assert.Equal(HealthStatus.Ok, r.Status);
        }

        [Fact]
        public void Remarks_AreIgnored()
        {
            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(MessageLevel.Remark, null, null, new[] { Optional("A") }, true).Status);
            Assert.Equal(HealthStatus.Disconnected, ObjectHealth.Classify(MessageLevel.Remark, null, null, new[] { Required("A") }, true).Status);
        }

        [Fact]
        public void ParameterObject_GetsOnlyErrorAndWarning_NeverDisconnected()
        {
            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("Number") }, false).Status);
            Assert.Equal(HealthStatus.Warning, ObjectHealth.Classify(MessageLevel.Warning, null, new[] { "w" }, null, false).Status);
            Assert.Equal(HealthStatus.Error, ObjectHealth.Classify(MessageLevel.Error, new[] { "e" }, null, null, false).Status);
        }

        [Fact]
        public void SnapshotNode_UsesTheSameRule()
        {
            var component = new CanvasNode
            {
                Kind = NodeKind.Component,
                Inputs = new[]
                {
                    new CanvasPort { Name = "Radius", NickName = "R" },
                    new CanvasPort { Name = "Plane", NickName = "P", Optional = true }
                }
            };
            var r = ObjectHealth.Classify(component);
            Assert.Equal(HealthStatus.Disconnected, r.Status);
            Assert.Equal("Missing: Radius", r.Message); // by Name, as status prints it

            var param = new CanvasNode { Kind = NodeKind.Panel, Inputs = new[] { new CanvasPort { Name = "Panel" } } };
            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(param).Status);

            var failing = new CanvasNode { Kind = NodeKind.Slider, MessageLevel = MessageLevel.Error, Errors = new[] { "x", "y" } };
            Assert.Equal("x; y", ObjectHealth.Classify(failing).Message);
        }

        [Fact]
        public void NullMessageLists_AreTreatedAsEmpty()
        {
            var r = ObjectHealth.Classify(MessageLevel.Error, null, null, null, true);
            Assert.Equal(HealthStatus.Error, r.Status);
            Assert.Equal(string.Empty, r.Message);
            Assert.Throws<ArgumentNullException>(() => ObjectHealth.Classify((CanvasNode)null));
        }
    }
}
