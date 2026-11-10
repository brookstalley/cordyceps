using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core.Canvas
{
    /// <summary>The one health classification <c>status</c> and <c>outline</c> share.</summary>
    public enum HealthStatus
    {
        Ok,
        Error,
        Warning,
        Disconnected
    }

    /// <summary>What the DISCONNECTED rule needs to know about one input.</summary>
    public readonly struct HealthInput
    {
        public HealthInput(string name, bool optional, int sourceCount, int volatileCount)
        {
            Name = name ?? string.Empty;
            Optional = optional;
            SourceCount = sourceCount;
            VolatileCount = volatileCount;
        }

        public string Name { get; }
        public bool Optional { get; }

        /// <summary>Grasshopper's raw source count (a wire from anything, infrastructure included, counts).</summary>
        public int SourceCount { get; }

        public int VolatileCount { get; }
    }

    /// <summary>One object's health: its status, the messages behind it, and the inputs it is missing.</summary>
    public sealed class HealthResult
    {
        public HealthResult(HealthStatus status, IReadOnlyList<string> messages, IReadOnlyList<string> missingInputs)
        {
            Status = status;
            Messages = messages ?? Array.Empty<string>();
            MissingInputs = missingInputs ?? Array.Empty<string>();
        }

        public HealthStatus Status { get; }

        /// <summary>Every message at the level that set the status (errors for ERROR, warnings for WARNING).</summary>
        public IReadOnlyList<string> Messages { get; }

        /// <summary>The names of required inputs with no source and no data (DISCONNECTED only).</summary>
        public IReadOnlyList<string> MissingInputs { get; }

        /// <summary><c>OK</c>, <c>ERROR</c>, <c>WARNING</c> or <c>DISCONNECTED</c>, as <c>status</c> prints it.</summary>
        public string StatusText => ObjectHealth.StatusText(Status);

        /// <summary>
        /// The message as <c>status</c> prints it: the messages joined with <c>"; "</c>,
        /// <c>Missing: A, B</c> for DISCONNECTED, or empty for OK.
        /// </summary>
        public string Message => Status switch
        {
            HealthStatus.Error => string.Join("; ", Messages),
            HealthStatus.Warning => string.Join("; ", Messages),
            HealthStatus.Disconnected => $"Missing: {string.Join(", ", MissingInputs)}",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Host-free health rule, in this order: an Error-level object is ERROR; a Warning-level object
    /// is WARNING; otherwise a component with any input that is required, has no source and holds no
    /// data is DISCONNECTED, listing those inputs; else OK. Remarks never change a status. A
    /// parameter object has no inputs in this sense, so only the ERROR and WARNING levels apply.
    /// </summary>
    public static class ObjectHealth
    {
        /// <summary>
        /// The name a status entry shows for an object: its nickname, or its type name when the
        /// nickname is empty (a slider or panel nobody has named can carry none). Never empty for
        /// an object that has a type name.
        /// </summary>
        public static string LabelFor(string nickName, string typeName) =>
            string.IsNullOrEmpty(nickName) ? (typeName ?? string.Empty) : nickName;

        public static HealthResult Classify(
            MessageLevel level,
            IEnumerable<string> errors,
            IEnumerable<string> warnings,
            IEnumerable<HealthInput> inputs,
            bool isComponent)
        {
            if (level == MessageLevel.Error)
                return new HealthResult(HealthStatus.Error, (errors ?? Enumerable.Empty<string>()).ToList(), null);
            if (level == MessageLevel.Warning)
                return new HealthResult(HealthStatus.Warning, (warnings ?? Enumerable.Empty<string>()).ToList(), null);

            if (isComponent && inputs != null)
            {
                var missing = inputs
                    .Where(IsMissing)
                    .Select(i => i.Name)
                    .ToList();
                if (missing.Count > 0)
                    return new HealthResult(HealthStatus.Disconnected, null, missing);
            }

            return new HealthResult(HealthStatus.Ok, null, null);
        }

        /// <summary>
        /// The missing-input rule on its own: a required input with no source and no data. Used by
        /// <see cref="Classify(MessageLevel, IEnumerable{string}, IEnumerable{string}, IEnumerable{HealthInput}, bool)"/>
        /// and by <c>gh_inspect(action='disconnected')</c>, which lists such inputs whatever the
        /// component's message level, so the two cannot disagree on what "missing" means.
        /// </summary>
        public static bool IsMissing(HealthInput input) =>
            !input.Optional && input.SourceCount == 0 && input.VolatileCount == 0;

        /// <summary>
        /// Whether an object is one that <c>status</c> lists and outline counts in its problems: a
        /// canvas object that is not a group and has solve state (components and parameter objects:
        /// sliders, toggles, value lists, panels, free parameters). Scribbles, sketches and groups
        /// have no health. Infrastructure is not a canvas object and is left out before this is asked.
        /// <c>status</c> asks it of live objects and outline of snapshot nodes, so the two read the
        /// same population.
        /// </summary>
        public static bool IsSubject(bool isGroup, bool hasSolveState) => !isGroup && hasSolveState;

        /// <summary><see cref="IsSubject(bool, bool)"/> for a snapshot node (a node is never a group).</summary>
        public static bool IsSubject(CanvasNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            return IsSubject(isGroup: false, node.HasSolveState);
        }

        /// <summary>The health of a snapshot node (components get the DISCONNECTED check; everything else does not).</summary>
        public static HealthResult Classify(CanvasNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.Detail != CanvasDetail.Full)
                throw new InvalidOperationException(
                    "ObjectHealth.Classify(CanvasNode) needs Full detail, but this node was read at Names detail.");
            // An object with no solve state (a scribble, a sketch) is never unhealthy.
            if (!node.HasSolveState) return new HealthResult(HealthStatus.Ok, null, null);
            return Classify(
                node.MessageLevel,
                node.Errors,
                node.Warnings,
                node.Inputs.Select(p => new HealthInput(p.Name, p.Optional, p.SourceCount, p.VolatileCount)),
                node.Kind == NodeKind.Component);
        }

        public static string StatusText(HealthStatus status) => status switch
        {
            HealthStatus.Error => "ERROR",
            HealthStatus.Warning => "WARNING",
            HealthStatus.Disconnected => "DISCONNECTED",
            _ => "OK"
        };
    }
}
