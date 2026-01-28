using System;
using System.Threading.Tasks;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino;

namespace Cordyceps.Core
{
    /// <summary>
    /// Provides thread-safe access to Grasshopper documents.
    /// All Grasshopper operations must run on the UI thread.
    /// </summary>
    public class GrasshopperContext
    {
        private const int DEFAULT_SOLUTION_DELAY_MS = 10;

        /// <summary>
        /// Execute an action on the Rhino UI thread and return the result
        /// </summary>
        public T ExecuteOnUiThread<T>(Func<T> action)
        {
            T result = default;
            Exception exception = null;

            RhinoApp.InvokeAndWait(new Action(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }));

            if (exception != null)
            {
                throw exception;
            }

            return result;
        }

        /// <summary>
        /// Execute an action on the Rhino UI thread (no return value)
        /// </summary>
        public void ExecuteOnUiThread(Action action)
        {
            Exception exception = null;

            RhinoApp.InvokeAndWait(new Action(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }));

            if (exception != null)
            {
                throw exception;
            }
        }

        /// <summary>
        /// Execute an async action on the UI thread
        /// </summary>
        public async Task<T> ExecuteOnUiThreadAsync<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                try
                {
                    var result = action();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }));

            return await tcs.Task;
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
