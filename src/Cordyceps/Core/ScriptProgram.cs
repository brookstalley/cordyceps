using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cordyceps.Core
{
    /// <summary>
    /// Outcome of a rebuild request: whether the component was asked to rebuild, why not when it
    /// was not, and the probe trace the caller can log.
    /// </summary>
    public sealed class ScriptRebuildResult
    {
        internal ScriptRebuildResult(bool rebuilt, string reason, List<string> probes)
        {
            Rebuilt = rebuilt;
            Reason = reason;
            Probes = probes ?? new List<string>();
        }

        /// <summary>True only when the component's expire-and-rebuild hooks both ran.</summary>
        public bool Rebuilt { get; }

        /// <summary>
        /// Why no rebuild happened — an absent hook, or the exception one threw. <c>null</c> when
        /// <see cref="Rebuilt"/> is true. Absent hooks are a normal outcome, not a failure: a
        /// component that has none recompiles off its own source member instead.
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
    /// versions doesn't silently turn every rebuild into a no-op. Reflection (over
    /// <see cref="object"/>, like <see cref="ScriptSourceWriter"/>) rather than <c>dynamic</c>, so
    /// "absent" is a decision this can report instead of a thrown binder exception — and so the
    /// whole thing is unit-testable against fakes with no Grasshopper types in the room.</para>
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
        /// True when <paramref name="component"/> exposes the hooks <see cref="Rebuild"/> needs.
        /// Lets a caller decide whether a rebuild is even on the table before disturbing component
        /// state it would otherwise have to restore.
        /// </summary>
        public static bool CanRebuild(object component)
        {
            if (component == null) return false;
            return FindScriptObjectInterface(component.GetType(), new List<string>()) != null;
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
                return new ScriptRebuildResult(false, "Cannot rebuild: no component was supplied.", probes);

            var typeName = component.GetType().Name;
            var scriptObject = FindScriptObjectInterface(component.GetType(), probes);
            if (scriptObject == null)
            {
                return new ScriptRebuildResult(
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
                return new ScriptRebuildResult(false, expireError, probes);

            var reBuildError = InvokeHook(component, reBuild, typeName, ReBuildMemberName, probes);
            if (reBuildError != null)
                return new ScriptRebuildResult(false, reBuildError, probes);

            return new ScriptRebuildResult(true, null, probes);
        }

        /// <summary>
        /// Read the source of the program <paramref name="component"/> will actually run, which is
        /// the built code's text — not the text stored on the script object.
        /// </summary>
        /// <remarks>
        /// False when the component has never built, which is the honest answer rather than a
        /// defect: until then there is no running program to report. Callers must omit the field
        /// they would have filled rather than substituting the stored source, or they recreate the
        /// false confirmation this exists to remove.
        /// </remarks>
        public static bool TryReadRunningSource(object component, out string source)
        {
            source = null;
            if (component == null) return false;

            var scriptObject = FindScriptObjectInterface(component.GetType(), new List<string>());
            if (scriptObject == null) return false;

            var tryGetCode = FindTryGetCodeMethod(scriptObject);
            if (tryGetCode == null) return false;

            object code;
            try
            {
                var arguments = new object[] { null };
                var result = tryGetCode.Invoke(component, arguments);
                if (!(result is bool succeeded) || !succeeded) return false;
                code = arguments[0];
            }
            catch (TargetInvocationException) { return false; }
            catch (TargetException) { return false; }
            catch (MethodAccessException) { return false; }
            catch (ArgumentException) { return false; }

            if (code == null) return false;

            return TryReadCodeText(code, out source);
        }

        /// <summary>
        /// The <c>Text</c> of a built code object. <c>Code</c> carries two of them — a public
        /// container property and an explicitly implemented <c>ICode.Text</c> that is a plain
        /// string — so only a string-typed one is accepted, whichever carries it.
        /// </summary>
        private static bool TryReadCodeText(object code, out string text)
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
                catch (TargetInvocationException) { }
                catch (TargetException) { }
                catch (MethodAccessException) { }
                catch (ArgumentException) { }
            }

            return false;
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
        /// The no-argument overload of <paramref name="name"/>. <c>ReBuild</c> is overloaded on
        /// build kind; the no-argument one is the "build it the way a solve would" overload.
        /// </summary>
        private static MethodInfo FindNoArgMethod(Type type, string name)
        {
            // GetMethods rather than GetMethod(name): GetMethod throws AmbiguousMatchException on
            // overloaded members, and an overloaded ReBuild must select a candidate, not fail.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != name) continue;
                if (method.GetParameters().Length == 0) return method;
            }

            return null;
        }

        /// <summary>The <c>bool TryGetCode(out object)</c> shape, whatever the out parameter's type.</summary>
        private static MethodInfo FindTryGetCodeMethod(Type type)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != TryGetCodeMemberName) continue;
                if (method.ReturnType != typeof(bool)) continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].IsOut) return method;
            }

            return null;
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
