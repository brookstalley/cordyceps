using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cordyceps.Core
{
    /// <summary>
    /// Outcome of a rebuild request: whether the component was asked to rebuild, whether a hook
    /// refused, and the probe trace the caller can log.
    /// </summary>
    public sealed class ScriptRebuildResult
    {
        internal ScriptRebuildResult(bool rebuilt, bool failed, string reason, List<string> probes)
        {
            Rebuilt = rebuilt;
            Failed = failed;
            Reason = reason;
            Probes = probes ?? new List<string>();
        }

        /// <summary>True only when the component's expire-and-rebuild hooks both ran.</summary>
        public bool Rebuilt { get; }

        /// <summary>
        /// True when the component <em>has</em> rebuild hooks and one of them threw — the opposite
        /// fact from having none, and the one worth acting on: such a component is probably still
        /// running its previous program. A component with no hooks is a normal outcome; it
        /// recompiles from its own source member instead.
        /// </summary>
        public bool Failed { get; }

        /// <summary>
        /// Why no rebuild happened — an absent hook, or the exception one threw. <c>null</c> when
        /// <see cref="Rebuilt"/> is true.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Ordered trace of what was probed and why each step was taken or skipped. Intended for
        /// debug logging by the host-side caller — this class cannot log itself, because it is
        /// linked into the unit-test project where the Rhino logging host does not exist.
        /// </summary>
        public IReadOnlyList<string> Probes { get; }
    }

    /// <summary>
    /// What the component's running program is, compared against the text a caller holds.
    /// </summary>
    public sealed class RunningSourceComparison
    {
        internal RunningSourceComparison(bool readable, bool diverged, string runningSource, string reason, List<string> probes)
        {
            Readable = readable;
            Diverged = diverged;
            RunningSource = runningSource;
            Reason = reason;
            Probes = probes ?? new List<string>();
        }

        /// <summary>
        /// True when the running program could actually be read. False is a real answer, not a
        /// defect — a component that has never built has no running program yet. Callers must omit
        /// the field they would have filled rather than substituting the stored source, or they
        /// recreate the false confirmation this exists to remove.
        /// </summary>
        public bool Readable { get; }

        /// <summary>True when the running program was read and differs from the expected text.</summary>
        public bool Diverged { get; }

        /// <summary>The running program's source; <c>null</c> unless <see cref="Readable"/>.</summary>
        public string RunningSource { get; }

        /// <summary>Why the running program could not be read; <c>null</c> when it could.</summary>
        public string Reason { get; }

        /// <summary>Ordered trace of what was probed, for the host-side caller to log.</summary>
        public IReadOnlyList<string> Probes { get; }
    }

    /// <summary>
    /// Host-free access to a script component's <em>running program</em>, as distinct from the
    /// source text stored on it.
    ///
    /// <para>Rhino 8 keeps two things: the script object's stored text, and the compiled
    /// <c>Code</c> built from it. Storing text does not rebuild the code — every source-changing
    /// path inside Rhino pairs the store with a rebuild or with an explicit
    /// <c>code.Text.Set(...)</c>. Writing source and stopping there leaves the component running
    /// its previous program with nothing reporting the divergence (issue #33), so <c>gh_script</c>
    /// asks for the rebuild itself and then reads the running program back to check its own work.</para>
    ///
    /// <para>Both operations go through <c>IScriptObject</c>, the public interface the component
    /// implements explicitly:
    /// <list type="bullet">
    /// <item><c>Expire()</c> drops the cached compile result; <c>ReBuild()</c> builds the current
    /// text. Neither expires or schedules a Grasshopper solution, which is what makes them safe
    /// inside clusters — <c>ExpireSolution(true)</c> and <c>NewSolution</c> there make the parent
    /// recreate the cluster and orphan its input hooks.</item>
    /// <item><c>TryGetCode(out Code)</c> yields the built code, whose <c>ICode.Text</c> is the
    /// program that will actually run.</item>
    /// </list></para>
    ///
    /// <para>Interfaces are matched by simple name <em>and</em> by the members this needs, never by
    /// assembly-qualified name: the shape is the contract, so a namespace move between Rhino
    /// versions doesn't silently turn every rebuild into a no-op. The member search walks base
    /// interfaces too, for the same reason — a member promoted to a base interface is the same
    /// shape. Reflection (over <see cref="object"/>, like <see cref="ScriptSourceWriter"/>) rather
    /// than <c>dynamic</c>, so "absent" is a decision this can report instead of a thrown binder
    /// exception — and so the whole thing is unit-testable against fakes with no Grasshopper types
    /// in the room.</para>
    /// </summary>
    public static class ScriptProgram
    {
        private const string ScriptObjectInterfaceName = "IScriptObject";
        private const string CodeInterfaceName = "ICode";
        private const string ExpireMemberName = "Expire";
        private const string ReBuildMemberName = "ReBuild";
        private const string TryGetCodeMemberName = "TryGetCode";
        private const string TextMemberName = "Text";

        /// <summary>Human-readable description of what a rebuildable component must expose.</summary>
        private const string RequiredHooksDescription =
            "an IScriptObject interface with no-argument Expire() and ReBuild() methods";

        /// <summary>
        /// Said wherever the stored source and the running program differ. Divergence is a fact to
        /// hand back, not a defect on its own: Rhino rewrites the <c>RunScript</c> signature of
        /// SDK-mode scripts as it builds them, so the two texts differ legitimately and often.
        /// </summary>
        public const string DivergenceNote =
            "The stored source and the program the component runs are not identical. Rhino rewrites "
            + "the RunScript signature of SDK-mode scripts when it builds them, so this is expected "
            + "for C# script components; runningSource is what executes.";

        /// <summary>
        /// The response fields describing a write: whether the rebuild ran, and whether the program
        /// the component will run was read back and matches what was written.
        /// </summary>
        /// <remarks>
        /// Every branch says something. An outcome that cannot be determined is reported as such
        /// rather than by omitting the field — a missing <c>verified</c> read as agreement is the
        /// false confirmation this whole path exists to remove. <c>rebuildFailed</c> and
        /// <c>rebuildSkipped</c> are deliberately different keys: a hook that threw means the
        /// component is probably still running its previous program, while having no hooks at all
        /// is the normal state of components that recompile on their own.
        /// </remarks>
        public static Dictionary<string, object> DescribeWrite(
            ScriptRebuildResult rebuild, RunningSourceComparison comparison)
        {
            var fields = new Dictionary<string, object> { ["rebuilt"] = rebuild.Rebuilt };

            if (!rebuild.Rebuilt)
            {
                if (rebuild.Failed)
                    fields["rebuildFailed"] = rebuild.Reason;
                else
                    fields["rebuildSkipped"] = rebuild.Reason;
            }

            if (comparison.Readable)
            {
                fields["verified"] = !comparison.Diverged;
                AddDivergence(fields, comparison);
            }
            else
            {
                fields["verificationSkipped"] = comparison.Reason;
            }

            return fields;
        }

        /// <summary>
        /// The response fields describing a read: how the running program relates to the stored
        /// source. <c>sourceDiverged</c> is present whenever the running program could be read, so
        /// its absence means "could not read" — reported by <c>runningSourceUnavailable</c> — and
        /// never has to stand in for "they agree".
        /// </summary>
        public static Dictionary<string, object> DescribeRead(RunningSourceComparison comparison)
        {
            var fields = new Dictionary<string, object>();

            if (comparison.Readable)
            {
                fields["sourceDiverged"] = comparison.Diverged;
                AddDivergence(fields, comparison);
            }
            else
            {
                fields["runningSourceUnavailable"] = comparison.Reason;
            }

            return fields;
        }

        /// <summary>
        /// The running program and the note explaining it, attached only when the texts differ —
        /// the same vocabulary at both surfaces, so an agent that learned <c>sourceDiverged</c>
        /// from one response finds it in the other.
        /// </summary>
        private static void AddDivergence(Dictionary<string, object> fields, RunningSourceComparison comparison)
        {
            if (!comparison.Diverged) return;

            fields["sourceDiverged"] = true;
            fields["runningSource"] = comparison.RunningSource;
            fields["divergenceNote"] = DivergenceNote;
        }

        /// <summary>
        /// Ask <paramref name="component"/> to drop its cached compile result and rebuild from the
        /// source it currently holds. Never throws; a component with no rebuild hooks comes back as
        /// <c>Rebuilt == false</c> with a reason rather than as an error.
        /// </summary>
        /// <param name="component">The script component. Untyped so this stays host-free.</param>
        public static ScriptRebuildResult Rebuild(object component)
        {
            var probes = new List<string>();

            if (component == null)
                return new ScriptRebuildResult(false, false, "Cannot rebuild: no component was supplied.", probes);

            var typeName = component.GetType().Name;
            var scriptObject = FindScriptObjectInterface(component.GetType(), probes);
            if (scriptObject == null)
            {
                return new ScriptRebuildResult(
                    false,
                    false,
                    $"'{typeName}' exposes no rebuild hook (probed for {RequiredHooksDescription}). "
                        + "Its source was stored; components of this shape rebuild from their own "
                        + "source member when they next solve.",
                    probes);
            }

            // Expire first: ReBuild on a component still holding a cached compile result can hand
            // back the previous program.
            var expire = FindNoArgMethod(scriptObject, ExpireMemberName);
            var reBuild = FindNoArgMethod(scriptObject, ReBuildMemberName);

            var expireError = InvokeHook(component, expire, typeName, ExpireMemberName, probes);
            if (expireError != null)
                return new ScriptRebuildResult(false, true, expireError, probes);

            var reBuildError = InvokeHook(component, reBuild, typeName, ReBuildMemberName, probes);
            if (reBuildError != null)
                return new ScriptRebuildResult(false, true, reBuildError, probes);

            return new ScriptRebuildResult(true, false, null, probes);
        }

        /// <summary>
        /// Read the program <paramref name="component"/> will actually run and compare it with
        /// <paramref name="expected"/> — the text the caller believes it wrote, or holds.
        /// </summary>
        /// <remarks>
        /// The single place the stored-vs-running question is answered, so every surface that
        /// reports it reports the same fact in the same words. Divergence is not on its own a
        /// defect: Rhino rewrites the <c>RunScript</c> signature of SDK-mode scripts as it builds
        /// them, so a legitimate write can come back changed.
        /// </remarks>
        public static RunningSourceComparison CompareRunning(object component, string expected)
        {
            var probes = new List<string>();

            if (component == null)
                return new RunningSourceComparison(false, false, null, "No component was supplied.", probes);

            var typeName = component.GetType().Name;
            var scriptObject = FindScriptObjectInterface(component.GetType(), probes);
            if (scriptObject == null)
            {
                return new RunningSourceComparison(
                    false, false, null,
                    $"'{typeName}' exposes no {ScriptObjectInterfaceName} interface to read the running program from.",
                    probes);
            }

            var tryGetCode = FindTryGetCodeMethod(scriptObject);
            if (tryGetCode == null)
            {
                probes.Add($"{typeName}'s {ScriptObjectInterfaceName} has no bool {TryGetCodeMemberName}(out ...) method");
                return new RunningSourceComparison(
                    false, false, null,
                    $"'{typeName}' exposes no {TryGetCodeMemberName}() to read the running program from.",
                    probes);
            }

            object code;
            try
            {
                var arguments = new object[] { null };
                var result = tryGetCode.Invoke(component, arguments);
                if (!(result is bool succeeded) || !succeeded)
                {
                    probes.Add($"{typeName}.{TryGetCodeMemberName}() reported no built code");
                    return new RunningSourceComparison(
                        false, false, null,
                        $"'{typeName}' has not built its script yet, so it has no running program to read.",
                        probes);
                }
                code = arguments[0];
            }
            catch (TargetInvocationException ex) { return UnreadableCode(typeName, ex.InnerException ?? ex, probes); }
            catch (TargetException ex) { return UnreadableCode(typeName, ex, probes); }
            catch (MethodAccessException ex) { return UnreadableCode(typeName, ex, probes); }
            catch (ArgumentException ex) { return UnreadableCode(typeName, ex, probes); }

            if (code == null)
            {
                probes.Add($"{typeName}.{TryGetCodeMemberName}() succeeded but handed back no code object");
                return new RunningSourceComparison(
                    false, false, null,
                    $"'{typeName}' reported a built script but produced no code object.",
                    probes);
            }

            if (!TryReadCodeText(code, probes, out var runningSource))
            {
                return new RunningSourceComparison(
                    false, false, null,
                    $"'{code.GetType().Name}' exposes no readable string {TextMemberName}, so the running program could not be read.",
                    probes);
            }

            bool diverged = !string.Equals(runningSource, expected, StringComparison.Ordinal);
            probes.Add($"running program read from {code.GetType().Name} — {(diverged ? "differs from" : "matches")} the expected source");
            return new RunningSourceComparison(true, diverged, runningSource, null, probes);
        }

        private static RunningSourceComparison UnreadableCode(string typeName, Exception ex, List<string> probes)
        {
            var description = $"{ex.GetType().Name}: {ex.Message}";
            probes.Add($"{typeName}.{TryGetCodeMemberName}() threw — {description}");
            return new RunningSourceComparison(
                false, false, null,
                $"'{typeName}'.{TryGetCodeMemberName}() failed ({description}), so the running program could not be read.",
                probes);
        }

        /// <summary>
        /// The <c>Text</c> of a built code object. <c>Code</c> carries two of them — a public
        /// container property and an explicitly implemented <c>ICode.Text</c> that is a plain
        /// string — so only a string-typed one is accepted, whichever carries it.
        /// </summary>
        private static bool TryReadCodeText(object code, List<string> probes, out string text)
        {
            text = null;
            var type = code.GetType();

            foreach (var candidate in FindStringTextGetters(type))
            {
                try
                {
                    if (candidate.Invoke(code, null) is string value)
                    {
                        text = value;
                        return true;
                    }
                }
                catch (TargetInvocationException ex) { ProbeTextFailure(probes, type, candidate, ex.InnerException ?? ex); }
                catch (TargetException ex) { ProbeTextFailure(probes, type, candidate, ex); }
                catch (MethodAccessException ex) { ProbeTextFailure(probes, type, candidate, ex); }
                catch (ArgumentException ex) { ProbeTextFailure(probes, type, candidate, ex); }
            }

            probes.Add($"{type.Name} exposes no readable string {TextMemberName}");
            return false;
        }

        private static void ProbeTextFailure(List<string> probes, Type type, MethodInfo getter, Exception ex)
        {
            probes.Add($"{type.Name}.{getter.DeclaringType?.Name}.{TextMemberName} getter threw — {ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        /// Getters for a string <c>Text</c>, interface declarations first so an explicitly
        /// implemented <c>ICode.Text</c> is found even though the type's own <c>Text</c> is a
        /// container of another type.
        /// </summary>
        private static IEnumerable<MethodInfo> FindStringTextGetters(Type type)
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (iface.Name != CodeInterfaceName) continue;
                var getter = FindStringPropertyGetter(iface);
                if (getter != null) yield return getter;
            }

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.Name == CodeInterfaceName) continue;
                var getter = FindStringPropertyGetter(iface);
                if (getter != null) yield return getter;
            }

            var own = FindStringPropertyGetter(type);
            if (own != null) yield return own;
        }

        /// <summary>The getter of a readable string <c>Text</c> property on one type, or null.</summary>
        private static MethodInfo FindStringPropertyGetter(Type type)
        {
            // GetProperties rather than GetProperty(name): GetProperty throws
            // AmbiguousMatchException when a derived type shadows the property with `new`.
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name != TextMemberName) continue;
                if (property.PropertyType != typeof(string)) continue;
                if (property.GetIndexParameters().Length != 0) continue;

                var getter = property.GetGetMethod();
                if (getter != null) return getter;
            }

            return null;
        }

        /// <summary>
        /// The component's script-object interface: matched by name and by carrying the hooks that
        /// make it the one meant, so an unrelated <c>IScriptObject</c> can't be mistaken for it.
        /// </summary>
        private static Type FindScriptObjectInterface(Type type, List<string> probes)
        {
            var sawName = false;

            foreach (var iface in type.GetInterfaces())
            {
                if (iface.Name != ScriptObjectInterfaceName) continue;
                sawName = true;

                if (FindNoArgMethod(iface, ExpireMemberName) == null) continue;
                if (FindNoArgMethod(iface, ReBuildMemberName) == null) continue;

                probes.Add($"{type.Name} implements {iface.Name} with {ExpireMemberName}() and {ReBuildMemberName}() — using it");
                return iface;
            }

            probes.Add(sawName
                ? $"{type.Name} implements {ScriptObjectInterfaceName}, but without no-argument {ExpireMemberName}() and {ReBuildMemberName}() methods"
                : $"{type.Name} implements no {ScriptObjectInterfaceName} — nothing to ask for a rebuild");
            return null;
        }

        /// <summary>
        /// The no-argument overload of <paramref name="name"/> on the interface or any it extends.
        /// <c>ReBuild</c> is overloaded on build kind; the no-argument one is the "build it the way
        /// a solve would" overload.
        /// </summary>
        private static MethodInfo FindNoArgMethod(Type type, string name)
        {
            // GetMethods rather than GetMethod(name): GetMethod throws AmbiguousMatchException on
            // overloaded members, and an overloaded ReBuild must select a candidate, not fail.
            foreach (var declaring in WithBaseInterfaces(type))
            {
                foreach (var method in declaring.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name != name) continue;
                    if (method.GetParameters().Length == 0) return method;
                }
            }

            return null;
        }

        /// <summary>The <c>bool TryGetCode(out object)</c> shape, whatever the out parameter's type.</summary>
        private static MethodInfo FindTryGetCodeMethod(Type type)
        {
            foreach (var declaring in WithBaseInterfaces(type))
            {
                foreach (var method in declaring.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name != TryGetCodeMemberName) continue;
                    if (method.ReturnType != typeof(bool)) continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].IsOut) return method;
                }
            }

            return null;
        }

        /// <summary>
        /// An interface followed by the interfaces it extends. <c>Type.GetMethods</c> on an
        /// interface returns only its own declarations, so a member promoted to a base interface
        /// would otherwise read as absent and silently turn every rebuild into a no-op.
        /// </summary>
        private static IEnumerable<Type> WithBaseInterfaces(Type type)
        {
            return new[] { type }.Concat(type.GetInterfaces());
        }

        /// <summary>
        /// Invoke one hook. Returns null on success, or a caller-facing reason on failure. The
        /// interface's own <see cref="MethodInfo"/> is invoked rather than the implementing type's,
        /// which is what dispatches to an explicit implementation.
        /// </summary>
        private static string InvokeHook(
            object component, MethodInfo method, string typeName, string memberName, List<string> probes)
        {
            if (method == null)
                return $"'{typeName}' has no no-argument {memberName}() on its {ScriptObjectInterfaceName} interface.";

            try
            {
                method.Invoke(component, null);
                probes.Add($"{typeName}.{memberName}() ran");
                return null;
            }
            catch (TargetInvocationException ex)
            {
                return Describe(typeName, memberName, ex.InnerException ?? ex, probes);
            }
            catch (TargetException ex)
            {
                return Describe(typeName, memberName, ex, probes);
            }
            catch (MethodAccessException ex)
            {
                return Describe(typeName, memberName, ex, probes);
            }
            catch (ArgumentException ex)
            {
                return Describe(typeName, memberName, ex, probes);
            }
        }

        private static string Describe(string typeName, string memberName, Exception ex, List<string> probes)
        {
            var description = $"{ex.GetType().Name}: {ex.Message}";
            probes.Add($"{typeName}.{memberName}() threw — {description}");
            return $"'{typeName}'.{memberName}() failed ({description}). The source was stored, but the "
                + "component may still be running its previous program — check it with "
                + "gh_inspect(action='status').";
        }
    }
}
