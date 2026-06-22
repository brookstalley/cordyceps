using System;
using System.Runtime.ExceptionServices;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino;

namespace Cordyceps.Core
{
    /// <summary>
    /// Provides thread-safe access to Grasshopper documents.
    /// All Grasshopper operations must run on the UI thread.
    /// Uses a <see cref="DocumentLock"/> (a bounded mutex) to serialize document
    /// modifications from concurrent HTTP requests without letting a single slow or
    /// wedged operation block every subsequent request forever.
    /// </summary>
    public class GrasshopperContext
    {
        private const int DEFAULT_SOLUTION_DELAY_MS = 10;

        /// <summary>
        /// Maximum time to wait to acquire the document lock before failing fast with
        /// <see cref="DocumentBusyException"/>. Generous, so a legitimate long single
        /// operation doesn't trip it, but finite, so a wedged UI thread no longer makes
        /// every request hang indefinitely. RhinoCommon's <c>InvokeAndWait</c> cannot
        /// itself be cancelled (see <c>.prawduct/artifacts/api-notes-rhinocommon.md</c>),
        /// so this bounds the <em>waiters</em>, not the holder of a wedged operation.
        /// </summary>
        private const int DOCUMENT_LOCK_TIMEOUT_SECONDS = 120;

        /// <summary>
        /// Lock serializing document access. Static so a single shared Rhino document is
        /// serialized across all contexts (and any additional server instances).
        /// </summary>
        private static readonly DocumentLock _documentLock =
            new DocumentLock(TimeSpan.FromSeconds(DOCUMENT_LOCK_TIMEOUT_SECONDS));

        /// <summary>
        /// Execute an action on the Rhino UI thread and return the result.
        /// Serializes document access via the bounded <see cref="DocumentLock"/>.
        /// </summary>
        public T ExecuteOnUiThread<T>(Func<T> action)
        {
            // Re-entrancy guard: if we're already on the UI thread, run inline. Re-marshaling
            // via InvokeAndWait from the UI thread would deadlock, and acquiring the document
            // lock here could deadlock against a worker thread already blocked in InvokeAndWait.
            if (!RhinoApp.InvokeRequired)
                return action();

            return _documentLock.Run(() =>
            {
                T result = default;
                ExceptionDispatchInfo captured = null;

                RhinoApp.InvokeAndWait(new Action(() =>
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        captured = ExceptionDispatchInfo.Capture(ex);
                    }
                }));

                captured?.Throw();
                return result;
            });
        }

        /// <summary>
        /// Execute an action on the Rhino UI thread (no return value).
        /// Serializes document access via the bounded <see cref="DocumentLock"/>.
        /// </summary>
        public void ExecuteOnUiThread(Action action)
        {
            // Re-entrancy guard — see the generic overload.
            if (!RhinoApp.InvokeRequired)
            {
                action();
                return;
            }

            _documentLock.Run(() =>
            {
                ExceptionDispatchInfo captured = null;

                RhinoApp.InvokeAndWait(new Action(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        captured = ExceptionDispatchInfo.Capture(ex);
                    }
                }));

                captured?.Throw();
            });
        }

        /// <summary>
        /// Get the active Grasshopper document
        /// Must be called from UI thread
        /// </summary>
        public GH_Document GetActiveDocument()
        {
            return Instances.ActiveCanvas?.Document;
        }

        /// <summary>
        /// Recompute the solution after modifications
        /// Must be called from UI thread
        /// </summary>
        public void RecomputeSolution()
        {
            var doc = GetActiveDocument();
            if (doc != null)
            {
                doc.NewSolution(true);
            }
        }

        /// <summary>
        /// Schedule a solution recompute
        /// Can be called from any thread
        /// </summary>
        public void ScheduleRecompute()
        {
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                var doc = GetActiveDocument();
                if (doc != null)
                {
                    doc.ScheduleSolution(DEFAULT_SOLUTION_DELAY_MS);
                }
            }));
        }
    }
}
