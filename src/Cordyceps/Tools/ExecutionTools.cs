using System;
using System.Collections.Generic;
using System.ComponentModel;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Rhino;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Execution and scripting operations
    /// </summary>
    [McpServerToolType]
    public class ExecutionTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;
        private static readonly Dictionary<string, string> MacroStore = new Dictionary<string, string>();

        public ExecutionTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Trigger a recompute of the Grasshopper document")]
        public string ExecutePreview()
        {
            _server?.RecordCommand("execute_preview");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                doc.NewSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    message = "Solution recomputed"
                });
            });
        }

        [McpServerTool, Description("Execute a Rhino command script")]
        public string ExecuteScript(
            [Description("Rhino command script to execute")] string script)
        {
            _server?.RecordCommand("execute_script");

            if (string.IsNullOrEmpty(script))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Script is required" });
            }

            bool result = RhinoApp.RunScript(script, false);

            return JsonConvert.SerializeObject(new
            {
                success = result,
                script = script
            });
        }

        [McpServerTool, Description("Store a named Rhino command macro for later execution")]
        public string CreateMacro(
            [Description("Name for the macro")] string name,
            [Description("Rhino command script to store")] string macro)
        {
            _server?.RecordCommand("create_macro");

            if (string.IsNullOrEmpty(name))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Macro name is required" });
            }

            if (string.IsNullOrEmpty(macro))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Macro content is required" });
            }

            MacroStore[name] = macro;

            return JsonConvert.SerializeObject(new
            {
                success = true,
                name = name,
                stored = true,
                macroCount = MacroStore.Count
            });
        }

        [McpServerTool, Description("Run a stored macro by name, or run a provided macro directly")]
        public string RunMacro(
            [Description("Name of stored macro to run")] string name = null,
            [Description("Direct macro to run (if not using stored macro)")] string macro = null)
        {
            _server?.RecordCommand("run_macro");

            string macroToRun = macro;

            // If name provided, look up stored macro
            if (!string.IsNullOrEmpty(name))
            {
                if (!MacroStore.TryGetValue(name, out macroToRun))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Macro not found: {name}" });
                }
            }

            if (string.IsNullOrEmpty(macroToRun))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "No macro provided" });
            }

            bool result = RhinoApp.RunScript(macroToRun, false);

            return JsonConvert.SerializeObject(new
            {
                success = result,
                name = name,
                macro = macroToRun
            });
        }

        [McpServerTool, Description("List all stored macros")]
        public string ListMacros()
        {
            _server?.RecordCommand("list_macros");

            var macros = new List<object>();
            foreach (var kvp in MacroStore)
            {
                macros.Add(new
                {
                    name = kvp.Key,
                    macro = kvp.Value
                });
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = macros.Count,
                macros
            });
        }

        [McpServerTool, Description("Delete a stored macro")]
        public string DeleteMacro(
            [Description("Name of macro to delete")] string name)
        {
            _server?.RecordCommand("delete_macro");

            if (string.IsNullOrEmpty(name))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Macro name is required" });
            }

            if (!MacroStore.ContainsKey(name))
            {
                return JsonConvert.SerializeObject(new { success = false, error = $"Macro not found: {name}" });
            }

            MacroStore.Remove(name);

            return JsonConvert.SerializeObject(new
            {
                success = true,
                name = name,
                deleted = true
            });
        }

        [McpServerTool, Description("Run a Python script in Rhino")]
        public string RunGHPython(
            [Description("Python script to execute")] string script)
        {
            _server?.RecordCommand("run_gh_python");

            if (string.IsNullOrEmpty(script))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Python script is required" });
            }

            try
            {
                var py = Rhino.Runtime.PythonScript.Create();
                if (py == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Python scripting is not available" });
                }

                py.ExecuteScript(script);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    executed = true
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { success = false, error = $"Python execution failed: {ex.Message}" });
            }
        }
    }
}
