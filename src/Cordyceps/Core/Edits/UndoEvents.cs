// Portions of this file are adapted from Hyperceps (src/Cordyceps/Tools/Batch/BatchOps.cs,
// ApplyContext; BatchCanvas.cs, LiveInputs and LiveOutputs), under the MIT License:
//
//   Copyright (c) 2026 Brooks Talley
//   Copyright (c) 2026 Yibin Yang
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software
//   and associated documentation files (the "Software"), to deal in the Software without
//   restriction, including without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or
//   substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
//   BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//   NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
//   DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Grasshopper.Kernel.Undo.Actions;

namespace Cordyceps.Core.Edits
{
    /// <summary>
    /// Grasshopper's own undo action for each <see cref="EditKind"/>, made through
    /// <c>GH_Document.UndoUtil</c> exactly as Grasshopper's editor makes them. Each
    /// <c>Create*Event</c> returns a one-action record; the action is taken out of it so a whole
    /// transaction becomes one record.
    /// </summary>
    internal static class UndoEvents
    {
        /// <summary>Capture the undo action of <paramref name="kind"/> for <paramref name="target"/> as it is now.</summary>
        public static IGH_UndoAction Create(GH_Document doc, EditKind kind, IGH_DocumentObject target)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (target == null) throw new ArgumentNullException(nameof(target));
            var util = doc.UndoUtil;
            var name = EditReplies.KindName(kind);

            switch (kind)
            {
                case EditKind.Wires:
                    return Single(util.CreateWireEvent(name, AsParam(target, kind)));
                case EditKind.Generic:
                    return Single(util.CreateGenericObjectEvent(name, target));
                case EditKind.NickName:
                    return Single(util.CreateNickNameEvent(name, target));
                case EditKind.Pivot:
                    return Single(util.CreatePivotEvent(name, target));
                case EditKind.Preview:
                    return Single(util.CreatePreviewEvent(name, AsActive(target, kind)));
                case EditKind.Enabled:
                    return Single(util.CreateEnabledEvent(name, AsActive(target, kind)));
                case EditKind.DataModifiers:
                    // UndoUtil has no event for these; this is the action Grasshopper's own param
                    // menu records for flatten, graft, reverse and simplify.
                    return new GH_DataModificationAction(AsParam(target, kind));
                case EditKind.Add:
                    return Single(util.CreateAddObjectEvent(name, target));
                case EditKind.Remove:
                    return Single(util.CreateRemoveObjectEvent(name, target));
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "No undo event for this kind.");
            }
        }

        /// <summary>The inputs of an object: a component's input ports, or a free-floating param itself.</summary>
        public static IEnumerable<IGH_Param> Inputs(IGH_DocumentObject obj)
        {
            if (obj is IGH_Component component) return component.Params.Input.ToList();
            if (obj is IGH_Param param) return new[] { param };
            return Array.Empty<IGH_Param>();
        }

        /// <summary>The outputs of an object: a component's output ports, or a free-floating param itself.</summary>
        public static IEnumerable<IGH_Param> Outputs(IGH_DocumentObject obj)
        {
            if (obj is IGH_Component component) return component.Params.Output.ToList();
            if (obj is IGH_Param param) return new[] { param };
            return Array.Empty<IGH_Param>();
        }

        private static IGH_UndoAction Single(GH_UndoRecord record)
        {
            if (record == null || record.ActionCount != 1)
                throw new InvalidOperationException($"Grasshopper returned {(record == null ? "no undo record" : $"{record.ActionCount} undo actions")} where one was expected.");
            return record.Actions[0];
        }

        private static IGH_Param AsParam(IGH_DocumentObject target, EditKind kind)
            => target as IGH_Param
               ?? throw new ArgumentException($"A {EditReplies.KindName(kind)} change applies to a parameter, not to '{target.NickName}'.", nameof(target));

        private static IGH_ActiveObject AsActive(IGH_DocumentObject target, EditKind kind)
            => target as IGH_ActiveObject
               ?? throw new ArgumentException($"A {EditReplies.KindName(kind)} change applies to a component or parameter, not to '{target.NickName}'.", nameof(target));
    }
}
