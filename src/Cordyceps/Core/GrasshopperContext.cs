using System;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino;

namespace Cordyceps.Core
{
    /// <summary>
    /// Provides thread-safe access to Grasshopper documents.
    /// All Grasshopper operations must run on the UI thread.
    /// Uses a semaphore to prevent concurrent document modifications from
    /// multiple HTTP requests.
    /// </summary>
    public class GrasshopperContext
    {
        private const int DEFAULT_SOLUTION_DELAY_MS = 10;

        /// <summary>
        /// Mutex to ensure only one request modifies the document at a time.
        /// Prevents race conditions when multiple HTTP requests arrive concurrently.
        /// </summary>
        private static readonly SemaphoreSlim _documentMutex = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Execute an action on the Rhino UI thread and return the result.
        /// Acquires document mutex to prevent concurrent modifications.
        /// </summary>
        public T ExecuteOnUiThread<T>(Func<T> action)
        {
            _documentMutex.Wait();
            try
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
            finally
            {
                _documentMutex.Release();
            }
        }

        /// <summary>
        /// Execute an action on the Rhino UI thread (no return value).
        /// Acquires document mutex to prevent concurrent modifications.
        /// </summary>
        public void ExecuteOnUiThread(Action action)
        {
            _documentMutex.Wait();
            try
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
            finally
            {
                _documentMutex.Release();
            }
        }

        /// <summary>
        /// Execute an async action on the UI thread.
        /// Acquires document mutex to prevent concurrent modifications.
        /// </summary>
        public async Task<T> ExecuteOnUiThreadAsync<T>(Func<T> action)
        {
            await _documentMutex.WaitAsync();
            try
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
            finally
            {
                _documentMutex.Release();
            }
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
