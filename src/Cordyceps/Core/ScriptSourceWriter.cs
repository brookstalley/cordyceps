using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cordyceps.Core
{
    /// <summary>
    /// Which member <see cref="ScriptSourceWriter"/> used (or would use) to write a script
    /// component's source.
    /// </summary>
    public enum ScriptSourceWriteMethod
    {
        /// <summary>No writable source member was found on the component.</summary>
        None,

        /// <summary>A single-argument <c>SetSource</c> method that accepts a string.</summary>
        SetSourceMethod,

        /// <summary>A public, writable <c>Code</c> property of type <see cref="string"/>.</summary>
        CodeProperty
    }

    /// <summary>
    /// Outcome of a script-source write attempt: whether it landed, which member carried it, an
    /// actionable error when it did not, and the probe trace the caller can log.
    /// </summary>
    public sealed class ScriptSourceWriteResult
    {
        internal ScriptSourceWriteResult(bool success, ScriptSourceWriteMethod method, string error, List<string> probes)
        {
            Success = success;
            Method = method;
            Error = error;
            Probes = probes ?? new List<string>();
        }

        /// <summary>True only when the source was actually written to the component.</summary>
        public bool Success { get; }

        /// <summary>
        /// The member that carried the write, or <see cref="ScriptSourceWriteMethod.None"/> when
        /// nothing was written. On failure this names the member that was attempted and failed,
        /// or <c>None</c> when no writable member exists at all.
        /// </summary>
        public ScriptSourceWriteMethod Method { get; }

        /// <summary>
        /// Caller-facing, actionable failure message; <c>null</c> when <see cref="Success"/> is true.
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// Ordered trace of what was probed and why each step was taken or skipped. Intended for
        /// debug logging by the host-side caller — this class cannot log itself, because it is
        /// linked into the unit-test project where the Rhino logging host does not exist.
        /// </summary>
        public IReadOnlyList<string> Probes { get; }
    }

    /// <summary>
    /// Host-free probe-and-write cascade for a script component's source, mirroring on the write
    /// side the defensive cascade <c>gh_script</c> already uses on the read side.
    ///
    /// <para>Not every script component exposes <c>SetSource(string)</c>. Rhino 7's GhPython
    /// component has no such method at all — it exposes a public writable <c>Code</c> property
    /// instead — and third-party script components are free to do the same. A single-pathed
    /// <c>SetSource</c> call therefore reads fine and fails opaquely on write. The cascade is:
    /// <list type="number">
    /// <item><c>SetSource</c> method taking one string-compatible argument.</item>
    /// <item>A public writable <c>Code</c> property of type <see cref="string"/>.</item>
    /// <item>Failure with a specific message naming the component type and what was probed.</item>
    /// </list></para>
    ///
    /// <para>The <c>Code</c> path carries a precondition. Components that expose their code as a
    /// visible input parameter guard the setter — GhPython's throws
    /// <c>InvalidOperationException("Cannot assign to code while code parameter exists")</c> unless
    /// its <c>HiddenCodeInput</c> property is true. That property is read <em>before</em> writing so
    /// the caller gets an instruction ("remove the code input parameter") instead of an opaque
    /// exception.</para>
    ///
    /// <para>Reflection, not <c>dynamic</c>: the probes must be able to report "absent" as a
    /// decision rather than as a thrown binder exception, and reflection over <see cref="object"/>
    /// keeps this file free of Grasshopper types so the whole cascade — including the writes — is
    /// unit-tested against fakes shaped like the real components.</para>
    /// </summary>
    public static class ScriptSourceWriter
    {
        private const string SetSourceMemberName = "SetSource";
        private const string CodeMemberName = "Code";
        private const string HiddenCodeInputMemberName = "HiddenCodeInput";

        /// <summary>Human-readable list of the members probed, used in the not-found error.</summary>
        private const string ProbedMembersDescription =
            "a SetSource(string) method, then a writable string Code property";

        /// <summary>
        /// True when <paramref name="component"/> exposes any member this writer can write source
        /// through. Used to recognize script components whose type name gives nothing away and
        /// which lack <c>SetSource</c> — without it, such a component would be rejected as "not a
        /// script component" before the <c>Code</c> fallback could ever run.
        /// </summary>
        public static bool CanWrite(object component)
        {
            if (component == null) return false;
            var type = component.GetType();
            return FindSetSourceMethod(type) != null || FindWritableCodeProperty(type) != null;
        }

        /// <summary>
        /// Write <paramref name="source"/> to <paramref name="component"/> through the first member
        /// of the cascade that is available and permitted. Never reports success without having
        /// written; never lets a host exception escape.
        /// </summary>
        /// <param name="component">The script component. Untyped so this stays host-free.</param>
        /// <param name="source">
        /// The final source body to store. The caller is responsible for preserving the language
        /// directive before calling — this method stores exactly what it is given.
        /// </param>
        public static ScriptSourceWriteResult Write(object component, string source)
        {
            var probes = new List<string>();

            if (component == null)
                return Failure(ScriptSourceWriteMethod.None, "Cannot set source: no component was supplied.", probes);

            var type = component.GetType();
            var typeName = type.Name;

            var setSource = FindSetSourceMethod(type);
            if (setSource != null)
            {
                probes.Add($"{typeName}.{SetSourceMemberName}({setSource.GetParameters()[0].ParameterType.Name}) found — using it");
                return InvokeSetSource(component, setSource, source, typeName, probes);
            }

            probes.Add($"{typeName} has no single-argument {SetSourceMemberName} accepting a string — falling back to the {CodeMemberName} property");

            var codeProperty = FindWritableCodeProperty(type);
            if (codeProperty == null)
            {
                probes.Add($"{typeName} has no writable string {CodeMemberName} property either");
                return Failure(
                    ScriptSourceWriteMethod.None,
                    $"Cannot set source on '{typeName}': no writable source member was found "
                        + $"(probed {ProbedMembersDescription}). The component may not be a script "
                        + "component, or its script API is not one Cordyceps can write to. "
                        + "Use gh_script(action='info') to confirm the component type.",
                    probes);
            }

            // Pre-check the guard the Code setter enforces, so a visible code input parameter
            // yields an instruction instead of the setter's opaque InvalidOperationException.
            if (TryReadHiddenCodeInput(component, type, probes, out bool hiddenCodeInput) && !hiddenCodeInput)
            {
                return Failure(
                    ScriptSourceWriteMethod.CodeProperty,
                    $"Cannot set source on '{typeName}': the component's code input parameter is "
                        + "visible, so its source is driven by that parameter and cannot be assigned "
                        + "directly. Disconnect and remove the code input parameter on the component "
                        + "(or feed the code through that parameter instead), then retry.",
                    probes);
            }

            probes.Add($"{typeName}.{CodeMemberName} is writable — using it");
            return SetCodeProperty(component, codeProperty, source, typeName, probes);
        }

        /// <summary>
        /// The one-argument <c>SetSource</c> overload a string can be passed to, or <c>null</c>.
        /// An exact <c>string</c> parameter wins; otherwise any parameter type a string is
        /// assignable to (e.g. <c>object</c>) is accepted, matching what a <c>dynamic</c> call
        /// would have bound to.
        /// </summary>
        private static MethodInfo FindSetSourceMethod(Type type)
        {
            MethodInfo assignable = null;

            // GetMethods rather than GetMethod(name, types): GetMethod throws AmbiguousMatchException
            // on overloaded members, and an overloaded SetSource must select a candidate, not fail.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != SetSourceMemberName) continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1) continue;

                var parameterType = parameters[0].ParameterType;
                if (parameterType == typeof(string))
                    return method;
                if (assignable == null && parameterType.IsAssignableFrom(typeof(string)))
                    assignable = method;
            }

            return assignable;
        }

        /// <summary>
        /// Look up a property by name, tolerating a derived type that shadows it with <c>new</c>.
        /// <see cref="Type.GetProperty(string, BindingFlags)"/> throws
        /// <see cref="AmbiguousMatchException"/> in that case; the most-derived declaration is the
        /// one an ordinary call would bind to, so walk the hierarchy and take it.
        /// </summary>
        private static PropertyInfo FindProperty(Type type, string name, BindingFlags flags)
        {
            try
            {
                return type.GetProperty(name, flags);
            }
            catch (AmbiguousMatchException)
            {
                for (var current = type; current != null; current = current.BaseType)
                {
                    var declared = current.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                    if (declared != null)
                        return declared;
                }
                return null;
            }
        }

        /// <summary>The public writable <c>string Code</c> property, or <c>null</c>.</summary>
        private static PropertyInfo FindWritableCodeProperty(Type type)
        {
            var property = FindProperty(type, CodeMemberName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
                return null;
            if (property.GetSetMethod() == null)
                return null;
            return property;
        }

        /// <summary>
        /// Read the component's <c>HiddenCodeInput</c> guard.
        /// </summary>
        /// <returns>
        /// True when the guard exists and was read. False when the component does not expose the
        /// concept at all, or when reading it threw — in both cases the write proceeds, because
        /// a component without the guard has no such precondition and a guard we cannot read is
        /// not evidence of one. If the setter does object, its exception is caught and surfaced
        /// on the write itself, so nothing is silently swallowed either way.
        /// </returns>
        private static bool TryReadHiddenCodeInput(object component, Type type, List<string> probes, out bool value)
        {
            value = false;

            var property = FindProperty(type, HiddenCodeInputMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null || !property.CanRead
                || (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?)))
            {
                probes.Add($"{type.Name} exposes no readable bool {HiddenCodeInputMemberName} — no code-input guard to check");
                return false;
            }

            object raw;
            try
            {
                raw = property.GetValue(component);
            }
            catch (TargetInvocationException ex)
            {
                probes.Add($"{type.Name}.{HiddenCodeInputMemberName} getter threw ({Describe(ex.InnerException ?? ex)}) — proceeding without the guard check");
                return false;
            }
            catch (TargetException ex)
            {
                probes.Add($"{type.Name}.{HiddenCodeInputMemberName} is not readable on this instance ({Describe(ex)}) — proceeding without the guard check");
                return false;
            }
            catch (MethodAccessException ex)
            {
                probes.Add($"{type.Name}.{HiddenCodeInputMemberName} getter is inaccessible ({Describe(ex)}) — proceeding without the guard check");
                return false;
            }

            if (!(raw is bool boolValue))
            {
                probes.Add($"{type.Name}.{HiddenCodeInputMemberName} returned no value — proceeding without the guard check");
                return false;
            }

            value = boolValue;
            probes.Add($"{type.Name}.{HiddenCodeInputMemberName} = {boolValue}");
            return true;
        }

        private static ScriptSourceWriteResult InvokeSetSource(
            object component, MethodInfo method, string source, string typeName, List<string> probes)
        {
            try
            {
                method.Invoke(component, new object[] { source });
                return new ScriptSourceWriteResult(true, ScriptSourceWriteMethod.SetSourceMethod, null, probes);
            }
            catch (TargetInvocationException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.SetSourceMethod, typeName, $"{SetSourceMemberName}()", ex.InnerException ?? ex, probes);
            }
            catch (TargetException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.SetSourceMethod, typeName, $"{SetSourceMemberName}()", ex, probes);
            }
            catch (MethodAccessException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.SetSourceMethod, typeName, $"{SetSourceMemberName}()", ex, probes);
            }
            catch (ArgumentException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.SetSourceMethod, typeName, $"{SetSourceMemberName}()", ex, probes);
            }
        }

        private static ScriptSourceWriteResult SetCodeProperty(
            object component, PropertyInfo property, string source, string typeName, List<string> probes)
        {
            try
            {
                property.SetValue(component, source);
                return new ScriptSourceWriteResult(true, ScriptSourceWriteMethod.CodeProperty, null, probes);
            }
            catch (TargetInvocationException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.CodeProperty, typeName, $"{CodeMemberName} property", ex.InnerException ?? ex, probes);
            }
            catch (TargetException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.CodeProperty, typeName, $"{CodeMemberName} property", ex, probes);
            }
            catch (MethodAccessException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.CodeProperty, typeName, $"{CodeMemberName} property", ex, probes);
            }
            catch (ArgumentException ex)
            {
                return WriteFailure(ScriptSourceWriteMethod.CodeProperty, typeName, $"{CodeMemberName} property", ex, probes);
            }
        }

        private static ScriptSourceWriteResult WriteFailure(
            ScriptSourceWriteMethod method, string typeName, string memberDescription, Exception ex, List<string> probes)
        {
            probes.Add($"{typeName} {memberDescription} threw: {Describe(ex)}");
            return Failure(
                method,
                $"Cannot set source on '{typeName}': its {memberDescription} rejected the write — {Describe(ex)}",
                probes);
        }

        private static ScriptSourceWriteResult Failure(ScriptSourceWriteMethod method, string error, List<string> probes)
            => new ScriptSourceWriteResult(false, method, error, probes);

        private static string Describe(Exception ex)
            => ex == null ? "unknown error" : $"{ex.GetType().Name}: {ex.Message}";
    }
}
