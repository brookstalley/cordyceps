using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Contract tests for <see cref="ScriptConfigurePlan"/> — the decision
    /// <c>gh_script(action='configure')</c> makes about whether a script component's compiled
    /// program is still valid after the call.
    ///
    /// <para>These test the decision, not the rebuild: the rebuild itself needs a live
    /// Grasshopper component and is queued for operator verification. The parameter-only shape is
    /// the anchor case: a gate reading "was source written?" lets a call that reshapes only the
    /// inputs leave the component running a program compiled against parameters it no longer has,
    /// with a response that says nothing about it.</para>
    /// </summary>
    public class ScriptConfigurePlanTests
    {
        [Fact]
        public void For_ParametersOnly_RequiresTheRebuild()
        {
            // The anchor case. A build reads the component's parameters to shape the program's
            // inputs and outputs, so moving them invalidates the program just as writing code does.
            var plan = ScriptConfigurePlan.For(parametersChanged: true, sourceWritten: false);

            Assert.True(plan.RebuildRequired);
            Assert.Null(plan.SkipReason);
        }

        [Fact]
        public void For_ParametersWithCode_RequiresTheRebuild()
        {
            var plan = ScriptConfigurePlan.For(parametersChanged: true, sourceWritten: true);

            Assert.True(plan.RebuildRequired);
            Assert.Null(plan.SkipReason);
        }

        [Fact]
        public void For_CodeOnly_RequiresTheRebuild()
        {
            var plan = ScriptConfigurePlan.For(parametersChanged: false, sourceWritten: true);

            Assert.True(plan.RebuildRequired);
            Assert.Null(plan.SkipReason);
        }

        [Fact]
        public void For_NothingChanged_StatesTheReasonInsteadOfReturningNothing()
        {
            var plan = ScriptConfigurePlan.For(parametersChanged: false, sourceWritten: false);

            Assert.False(plan.RebuildRequired);
            Assert.Equal(ScriptConfigurePlan.NothingChangedReason, plan.SkipReason);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void For_EveryRequestShape_AnswersTheRebuildQuestionOneWayOrTheOther(
            bool parametersChanged, bool sourceWritten)
        {
            // The whole contract in one line: no shape may produce a plan carrying neither a
            // rebuild nor a stated reason for its absence. Silence is what the caller could not
            // interpret.
            var plan = ScriptConfigurePlan.For(parametersChanged, sourceWritten);

            Assert.True(plan.RebuildRequired || !string.IsNullOrWhiteSpace(plan.SkipReason));
            // And never both: a reason alongside a rebuild would be two answers to one question.
            Assert.Equal(plan.RebuildRequired, plan.SkipReason == null);
        }
    }
}
