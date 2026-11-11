using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace Cordyceps.Core
{
    /// <summary>
    /// The Grasshopper adapter for script components (C# and Python): recognise one, read its
    /// stored source, and rebuild its program after a change, reporting the outcome in the
    /// vocabulary <see cref="ScriptProgram"/> defines.
    /// </summary>
    /// <remarks>
    /// Shared by every tool that can change a script component — <c>gh_script</c> writes source and
    /// reshapes ports, and <c>gh_canvas(action='zoomable')</c> reshapes ports too — so they rebuild
    /// through one gate and answer the rebuild question in the same words. Host-coupled (it logs
    /// through <see cref="DebugLog"/> and takes Grasshopper types), so it is not linked into the test
    /// project; the decisions it applies live in <see cref="ScriptConfigurePlan"/> and
    /// <see cref="ScriptProgram"/>, which are. All calls must run on the UI thread.
    /// </remarks>
    public static class ScriptComponents
    {
        /// <summary>
        /// True when <paramref name="component"/> is a script component Cordyceps can work with:
        /// its type name says so, or it exposes a source member Cordyceps can write.
        /// </summary>
        public static bool IsScriptComponent(IGH_DocumentObject component)
        {
            if (component == null) return false;
            var typeName = component.GetType().Name;
            if (typeName.Contains("Script") || typeName.Contains("Python") || typeName.Contains("CSharp"))
                return true;
            // Last resort: anything Cordyceps can actually write source to counts. This probes the
            // Code-property fallback as well as SetSource, so a script component that exposes only
            // Code (and whose type name gives nothing away) isn't rejected here before the write
            // cascade ever gets a chance to run.
            return ScriptSourceWriter.CanWrite(component);
        }

        /// <summary>
        /// The source text stored on <paramref name="component"/>, or <c>null</c> when none of the
        /// known source members can be read.
        /// </summary>
        public static string ReadSource(IGH_DocumentObject component)
        {
            var tryGetSourceMethod = component.GetType().GetMethod("TryGetSource");
            if (tryGetSourceMethod != null)
            {
                try
                {
                    var parameters = new object[] { null };
                    var result = tryGetSourceMethod.Invoke(component, parameters);
                    if (result is bool success && success)
                        return parameters[0] as string;
                }
                catch (Exception ex) { DebugLog.Debug($"ReadSource: TryGetSource() invoke failed: {ex.Message}"); }
            }

            // Best-effort probe cascade: try each known source-access member and fall through to the
            // next when it's absent/throws. Debug-logged (off by default) so a probe miss is traceable
            // without normal-run noise.
            try { dynamic sc = component; return sc.Source; }
            catch (Exception ex) { DebugLog.Debug($"ReadSource: dynamic .Source unavailable: {ex.Message}"); }
            try { dynamic sc = component; return sc.ScriptSource; }
            catch (Exception ex) { DebugLog.Debug($"ReadSource: dynamic .ScriptSource unavailable: {ex.Message}"); }
            try { dynamic sc = component; return sc.Code; }
            catch (Exception ex) { DebugLog.Debug($"ReadSource: dynamic .Code unavailable: {ex.Message}"); }

            var sourceProp = component.GetType().GetProperty("Source")
                ?? component.GetType().GetProperty("ScriptSource")
                ?? component.GetType().GetProperty("Code");
            if (sourceProp != null)
            {
                try { return sourceProp.GetValue(component) as string; }
                catch (Exception ex) { DebugLog.Debug($"ReadSource: {sourceProp.Name} getter failed: {ex.Message}"); }
            }

            return null;
        }

        /// <summary>
        /// The rebuild half of a response from any call that may have changed a script component:
        /// rebuild when the call changed its ports or its source, and name the reason when it did
        /// not. Returns the fields to merge into the response.
        /// </summary>
        /// <remarks>
        /// The gate for every route that reshapes a script's ports (<c>configure</c> and
        /// <c>zoomable</c>), so a way of changing one cannot slip past unrebuilt <em>and</em>
        /// unreported, as a gate reading "was source written?" lets a parameter-only change do.
        /// <c>set</c> always writes source, so it calls <see cref="RebuildAndVerify"/> directly. The decision itself lives in
        /// <see cref="ScriptConfigurePlan"/>, where it is unit-testable.
        /// </remarks>
        /// <param name="parametersChanged">True when the call actually reshaped the component's ports.</param>
        /// <param name="writtenSource">
        /// The source this call wrote, or <c>null</c> when it wrote none. A parameter-only rebuild
        /// is checked against the text the component already stores instead: after a rebuild the
        /// two must agree, so it is the right expectation to hold the running program to.
        /// </param>
        /// <param name="sourceWriteFailed">True when the call tried to write source and the write threw.</param>
        public static Dictionary<string, object> RebuildForChange(
            IGH_DocumentObject component, bool parametersChanged, string writtenSource, bool sourceWriteFailed = false)
        {
            var plan = ScriptConfigurePlan.For(parametersChanged, writtenSource != null);
            if (!plan.RebuildRequired)
                return ScriptProgram.DescribeUnchanged(plan.SkipReason);

            if (writtenSource != null)
                return RebuildAndVerify(component, writtenSource);

            // The params moved but the source write threw, so the component was rebuilt from the
            // text it held BEFORE the failed write. Report no verification rather than a 'verified'
            // about source the caller never sent — that field is read as "the new code is what will
            // run", which would be a false confirmation.
            if (sourceWriteFailed)
            {
                return RebuildAndVerify(component, expectedSource: null, noExpectationReason:
                    "The source write failed, so the component was rebuilt from the text it already "
                    + "held. Verifying against that would say nothing about the code you sent — see "
                    + "codeSet and sourceError.");
            }

            return RebuildAndVerify(component, ReadSource(component));
        }

        /// <summary>
        /// Ask <paramref name="component"/> to rebuild its program, then check the result by reading
        /// back the program it will actually run and comparing it with
        /// <paramref name="expectedSource"/>. Returns the fields to merge into the tool response.
        /// </summary>
        /// <remarks>
        /// <para>Writing source stores text; it does not recompile. Rhino builds a <c>Code</c> from
        /// the script and keeps running that one, so a component whose source was replaced goes on
        /// executing its previous program until something invalidates the build. Every
        /// source-changing path inside Rhino pairs the store with a rebuild; this is ours. The build
        /// also reads the component's parameters to shape the program's inputs and outputs, so a
        /// call that only reshaped those parameters needs this just as much.</para>
        /// <para>A component with no rebuild hooks is reported, not failed — Rhino 7's GhPython and
        /// other <c>Code</c>-property components rebuild from their own source member instead. A
        /// hook that <em>threw</em> is the opposite fact and gets its own key and a warning in the
        /// log, because such a component is probably still running its previous program.</para>
        /// </remarks>
        /// <param name="expectedSource">
        /// The text the running program should be built from; <c>null</c> when the caller has none
        /// to offer, which is reported rather than compared against.
        /// </param>
        /// <param name="noExpectationReason">
        /// What to report when <paramref name="expectedSource"/> is <c>null</c> and the caller knows
        /// why better than this does. Omitted, the reason is that the component has no readable
        /// stored source.
        /// </param>
        public static Dictionary<string, object> RebuildAndVerify(
            IGH_DocumentObject component, string expectedSource, string noExpectationReason = null)
        {
            var typeName = component.GetType().Name;

            var rebuild = ScriptProgram.Rebuild(component);
            LogProbes(rebuild.Probes);
            if (rebuild.Failed)
                DebugLog.Warn($"[RebuildAndVerify] rebuild hook failed on {typeName} — {rebuild.Reason}");
            else if (!rebuild.Rebuilt)
                DebugLog.Info($"[RebuildAndVerify] no rebuild hook on {typeName} — {rebuild.Reason}");

            // A null expectation still reads the program — it just does not judge it.
            var comparison = ScriptProgram.CompareRunning(component, expectedSource);
            LogProbes(comparison.Probes);

            if (expectedSource == null)
            {
                // Nothing to hold the program against, so no verdict is reported. The program
                // itself is a separate fact and the most useful one such a response can carry, so
                // hand it over unjudged rather than dropping it with the verdict.
                var uncomparable = ScriptProgram.NotCompared(noExpectationReason
                    ?? $"'{typeName}' has no readable stored source to compare its running program against.");
                DebugLog.Info($"[RebuildAndVerify] nothing to verify the rebuild against on {typeName} — {uncomparable.Reason}");

                var fields = ScriptProgram.DescribeWrite(rebuild, uncomparable);
                if (comparison.Readable)
                    fields["runningSource"] = comparison.RunningSource;
                return fields;
            }

            if (!comparison.Readable)
                DebugLog.Info($"[RebuildAndVerify] could not verify the write on {typeName} — {comparison.Reason}");

            return ScriptProgram.DescribeWrite(rebuild, comparison);
        }

        /// <summary>
        /// Send a probe trace to the debug log. The reflection cascades cannot log themselves —
        /// they are linked into the unit-test project, where the Rhino logging host does not exist.
        /// </summary>
        public static void LogProbes(IReadOnlyList<string> probes)
        {
            foreach (var probe in probes)
                DebugLog.Debug($"ScriptProgram: {probe}");
        }
    }
}
