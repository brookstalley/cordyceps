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
        private static readonly object MacroStoreLock = new object();

        public ExecutionTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Execute a Rhino command script")]
        public string RhinoExecuteScript(
            [Description("Rhino command script to execute")] string script)
        {
            _server?.RecordCommand("execute_script");

            if (!RequestValidator.ValidateRequired(script, "script", out var error))
                return ToolHelpers.ErrorResponse(error);

            bool result = RhinoApp.RunScript(script, false);

            return JsonConvert.SerializeObject(new
            {
                success = result,
                script = script
            });
        }

        [McpServerTool, Description("Store a named Rhino command macro for later execution")]
        public string RhinoCreateMacro(
            [Description("Name for the macro")] string name,
            [Description("Rhino command script to store")] string macro)
        {
            _server?.RecordCommand("create_macro");

            if (!RequestValidator.ValidateRequired(name, "name", out var error))
                return ToolHelpers.ErrorResponse(error);
            if (!RequestValidator.ValidateRequired(macro, "macro", out error))
                return ToolHelpers.ErrorResponse(error);

            int macroCount;
            lock (MacroStoreLock)
            {
                MacroStore[name] = macro;
                macroCount = MacroStore.Count;
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                name = name,
                stored = true,
                macroCount
            });
        }

        [McpServerTool, Description("Run a stored macro by name, or run a provided macro directly")]
        public string RhinoRunMacro(
            [Description("Name of stored macro to run")] string name = null,
            [Description("Direct macro to run (if not using stored macro)")] string macro = null)
        {
            _server?.RecordCommand("run_macro");

            string macroToRun = macro;

            // If name provided, look up stored macro
            if (!string.IsNullOrEmpty(name))
            {
                lock (MacroStoreLock)
                {
                    if (!MacroStore.TryGetValue(name, out macroToRun))
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = $"Macro not found: {name}" });
                    }
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
        public string RhinoListMacros()
        {
            _server?.RecordCommand("list_macros");

            var macros = new List<object>();
            lock (MacroStoreLock)
            {
                foreach (var kvp in MacroStore)
                {
                    macros.Add(new
                    {
                        name = kvp.Key,
                        macro = kvp.Value
                    });
                }
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = macros.Count,
                macros
            });
        }

        [McpServerTool, Description("Delete a stored macro")]
        public string RhinoDeleteMacro(
            [Description("Name of macro to delete")] string name)
        {
            _server?.RecordCommand("delete_macro");

            if (!RequestValidator.ValidateRequired(name, "name", out var error))
                return ToolHelpers.ErrorResponse(error);

            lock (MacroStoreLock)
            {
                if (!MacroStore.ContainsKey(name))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Macro not found: {name}" });
                }

                MacroStore.Remove(name);
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                name = name,
                deleted = true
            });
        }

        [McpServerTool, Description("Run a Python script in Rhino")]
        public string RhinoRunPython(
            [Description("Python script to execute")] string script)
        {
            _server?.RecordCommand("run_gh_python");

            if (!RequestValidator.ValidateRequired(script, "script", out var error))
                return ToolHelpers.ErrorResponse(error);

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
