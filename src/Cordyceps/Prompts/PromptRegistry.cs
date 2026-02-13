using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Cordyceps.Prompts
{
    /// <summary>
    /// Registry for MCP prompts. Provides workflow templates that guide
    /// multi-step operations in Grasshopper.
    /// </summary>
    public class PromptRegistry
    {
        private static PromptRegistry _instance;
        private static readonly object _lock = new object();

        private readonly Dictionary<string, PromptDefinition> _prompts = new Dictionary<string, PromptDefinition>();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static PromptRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PromptRegistry();
                            _instance.Initialize();
                        }
                    }
                }
                return _instance;
            }
        }

        private PromptRegistry() { }

        /// <summary>
        /// Initialize with built-in prompts
        /// </summary>
        private void Initialize()
        {
            RegisterPrompt(new PromptDefinition
            {
                Name = "create_parametric_geometry",
                Description = "Guide for creating parametric geometry with input sliders",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "geometry_type", Description = "Type of geometry to create (circle, box, curve, surface)", Required = true },
                    new PromptArgument { Name = "parameter_count", Description = "Number of input parameters to create", Required = false }
                },
                Template = LoadEmbeddedPrompt("Knowledge.Prompts.CreateParametricGeometry.md")
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "debug_data_mismatch",
                Description = "Diagnostic workflow for data tree mismatch errors",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "component_id", Description = "ID of the component with errors", Required = false }
                },
                Template = LoadEmbeddedPrompt("Knowledge.Prompts.DebugDataMismatch.md")
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "setup_script_component",
                Description = "Step-by-step guide for configuring a C# or Python script component",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "language", Description = "Script language: 'csharp' or 'python'", Required = true },
                    new PromptArgument { Name = "purpose", Description = "Brief description of what the script should do", Required = false }
                },
                Template = LoadEmbeddedPrompt("Knowledge.Prompts.SetupScriptComponent.md")
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "optimize_canvas",
                Description = "Workflow for analyzing and optimizing Grasshopper canvas performance",
                Arguments = new List<PromptArgument>(),
                Template = LoadEmbeddedPrompt("Knowledge.Prompts.OptimizeCanvas.md")
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "plan_definition",
                Description = "Step-by-step planning workflow for building a Grasshopper definition",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "goal", Description = "What the definition should accomplish", Required = true }
                },
                Template = LoadEmbeddedPrompt("Knowledge.Prompts.PlanDefinition.md")
            });
        }

        /// <summary>
        /// Load a prompt template from an embedded resource
        /// </summary>
        private string LoadEmbeddedPrompt(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames();
            var matchingName = names.FirstOrDefault(n => n.EndsWith(resourceName));

            if (matchingName == null)
            {
                Core.DebugLog.Warn($"Prompt template not found: {resourceName}");
                return $"Template '{resourceName}' not found.";
            }

            using (var stream = assembly.GetManifestResourceStream(matchingName))
            {
                if (stream == null) return $"Template '{resourceName}' not found.";
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Register a prompt
        /// </summary>
        public void RegisterPrompt(PromptDefinition prompt)
        {
            _prompts[prompt.Name] = prompt;
        }

        /// <summary>
        /// List all available prompts
        /// </summary>
        public List<object> ListPrompts()
        {
            return _prompts.Values.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                arguments = p.Arguments?.Select(a => new
                {
                    name = a.Name,
                    description = a.Description,
                    required = a.Required
                }).ToList()
            }).ToList<object>();
        }

        /// <summary>
        /// Get a prompt by name with arguments filled in
        /// </summary>
        public PromptResult GetPrompt(string name, Dictionary<string, string> arguments)
        {
            if (!_prompts.TryGetValue(name, out var prompt))
            {
                return null;
            }

            var result = prompt.Template;

            // Substitute arguments
            if (arguments != null)
            {
                foreach (var arg in arguments)
                {
                    result = result.Replace($"{{{arg.Key}}}", arg.Value);
                }
            }

            // Replace any remaining placeholders with defaults or empty
            if (prompt.Arguments != null)
            {
                foreach (var arg in prompt.Arguments)
                {
                    result = result.Replace($"{{{arg.Name}}}", arg.Name);
                }
            }

            return new PromptResult
            {
                Name = prompt.Name,
                Description = prompt.Description,
                Content = result
            };
        }
    }

    /// <summary>
    /// Definition of an MCP prompt
    /// </summary>
    public class PromptDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<PromptArgument> Arguments { get; set; }
        public string Template { get; set; }
    }

    /// <summary>
    /// Argument for a prompt
    /// </summary>
    public class PromptArgument
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
    }

    /// <summary>
    /// Result of getting a prompt
    /// </summary>
    public class PromptResult
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Content { get; set; }
    }
}
