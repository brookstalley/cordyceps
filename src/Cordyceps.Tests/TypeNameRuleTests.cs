// Partly adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.Naming;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// The strict component-type rule, against a small catalog shaped like a real install: Merge
    /// with a deprecated twin, Line and Circle as both a component and a parameter, Addition with
    /// deprecated versions, a plugin that ships its own Panel, and the pinned built-ins.
    /// </summary>
    public class TypeNameRuleTests
    {
        private static readonly Guid MergeGuid = Guid.Parse("3cadddef-1e2b-4c09-9390-0e8f78f7609f");
        private static readonly Guid MergeOldGuid = Guid.Parse("86f1e1b1-0000-4000-8000-000000000001");
        private static readonly Guid LineGuid = Guid.Parse("4c4e56eb-2f04-43f9-95a3-cc46a14f495a");
        private static readonly Guid LineParamGuid = Guid.Parse("8529dbdf-9b6f-42e9-8e1f-c7a2bde56a70");
        private static readonly Guid CircleGuid = Guid.Parse("807b86e3-be8d-4970-92b5-f8cdcb45b06b");
        private static readonly Guid CircleParamGuid = Guid.Parse("d1028c72-ff86-4057-9eb0-36c687a4d98c");
        private static readonly Guid CullPatternGuid = Guid.Parse("00000000-0000-4000-8000-0000000000c1");
        private static readonly Guid CullIndexGuid = Guid.Parse("00000000-0000-4000-8000-0000000000c2");
        private static readonly Guid PanelGuid = TypeNameRule.Pinned["Panel"];
        private static readonly Guid PluginPanelGuid = Guid.Parse("00000000-0000-4000-8000-0000000000a1");
        private static readonly Guid SliderGuid = TypeNameRule.Pinned["Number Slider"];
        // The GUID "Python 3 Script" is pinned to is Rhino 8's unified Script component, which an
        // install names "Script" (docs/upstream-rhino-scriptcomponent-languagespec.md); the dedicated
        // Python 3 Script component is a different type with its own GUID.
        private static readonly Guid ScriptGuid = TypeNameRule.Pinned["Python 3 Script"];
        private static readonly Guid DedicatedPythonGuid = Guid.Parse("719467e6-7cf5-4848-99b0-c5dd57e5442c");
        private static readonly Guid XyPlaneGuid = Guid.Parse("00000000-0000-4000-8000-0000000000b1");
        private static readonly Guid TwinAGuid = Guid.Parse("00000000-0000-4000-8000-0000000000d1");
        private static readonly Guid TwinBGuid = Guid.Parse("00000000-0000-4000-8000-0000000000d2");

        private static TypeCatalog Catalog() => new TypeCatalog(new[]
        {
            new TypeCandidate { Name = "Merge", Category = "Sets", SubCategory = "Tree", Guid = MergeGuid, Plugin = "Grasshopper" },
            new TypeCandidate { Name = "Merge", Category = "Sets", SubCategory = "Tree", Guid = MergeOldGuid, Deprecated = true, UpgradeTo = "Merge" },
            new TypeCandidate { Name = "Line", Category = "Curve", SubCategory = "Primitive", Guid = LineGuid, Plugin = "Grasshopper" },
            new TypeCandidate { Name = "Line", Category = "Params", SubCategory = "Geometry", Guid = LineParamGuid, Plugin = "Grasshopper" },
            new TypeCandidate { Name = "Circle", Category = "Curve", SubCategory = "Primitive", Guid = CircleGuid },
            new TypeCandidate { Name = "Circle", Category = "Params", SubCategory = "Geometry", Guid = CircleParamGuid },
            new TypeCandidate { Name = "Addition", Category = "Maths", SubCategory = "Operators", Guid = Guid.NewGuid() },
            new TypeCandidate { Name = "Addition", Category = "Vector", SubCategory = "Vector", Guid = Guid.NewGuid(), Deprecated = true },
            new TypeCandidate { Name = "Addition", Category = "Display", SubCategory = "Colour", Guid = Guid.NewGuid(), Deprecated = true },
            new TypeCandidate { Name = "Cull Pattern", Category = "Sets", SubCategory = "Sequence", Guid = CullPatternGuid },
            new TypeCandidate { Name = "Cull Index", Category = "Sets", SubCategory = "Sequence", Guid = CullIndexGuid },
            new TypeCandidate { Name = "Old Thing", Category = "Sets", SubCategory = "List", Guid = Guid.NewGuid(), Deprecated = true },
            new TypeCandidate { Name = "Curve", Category = "Params", SubCategory = "Geometry", Guid = Guid.NewGuid() },
            new TypeCandidate { Name = "Panel", Category = "Params", SubCategory = "Input", Guid = PanelGuid, Plugin = "Grasshopper" },
            new TypeCandidate { Name = "Panel", Category = "Ladybug", SubCategory = "Display", Guid = PluginPanelGuid, Plugin = "Ladybug" },
            new TypeCandidate { Name = "Number Slider", Category = "Params", SubCategory = "Input", Guid = SliderGuid },
            new TypeCandidate { Name = "Script", Category = "Maths", SubCategory = "Script", Guid = ScriptGuid },
            new TypeCandidate { Name = "Python 3 Script", Category = "Maths", SubCategory = "Script", Guid = DedicatedPythonGuid },
            new TypeCandidate { Name = "XY Plane", Category = "Vector", SubCategory = "Plane", Guid = XyPlaneGuid },
            new TypeCandidate { Name = "Twin", Category = "Alpha", SubCategory = "Same", Guid = TwinAGuid, Plugin = "A" },
            new TypeCandidate { Name = "Twin", Category = "Alpha", SubCategory = "Same", Guid = TwinBGuid, Plugin = "B" },
        });

        // ---- What resolves ------------------------------------------------------------------

        [Theory]
        [InlineData("Merge")]
        [InlineData("merge")]
        [InlineData("  MERGE  ")]
        [InlineData("Sets/Merge")]
        [InlineData("sets/tree/merge")]
        [InlineData("Merge (Sets)")]
        [InlineData("3cadddef-1e2b-4c09-9390-0e8f78f7609f")]
        public void Resolve_Merge_ResolvesToTheCurrentOne(string text)
        {
            var r = TypeNameRule.Resolve(text, Catalog());

            Assert.Equal(TypeResolutionKind.Resolved, r.Kind);
            Assert.Equal(MergeGuid, r.Match.Guid);
            Assert.False(r.UsedDeprecated);
        }

        [Fact]
        public void Resolve_AGuid_SelectsThatTypeEvenWhenDeprecated()
        {
            var r = TypeNameRule.Resolve(MergeOldGuid.ToString(), Catalog());

            Assert.True(r.IsResolved);
            Assert.Equal(MergeOldGuid, r.Match.Guid);
            Assert.True(r.UsedDeprecated);
        }

        [Fact]
        public void Resolve_DeprecatedIgnoredWhenACurrentOneExists()
        {
            var r = TypeNameRule.Resolve("Addition", Catalog());

            Assert.True(r.IsResolved);
            Assert.Equal("Maths", r.Match.Category);
        }

        [Fact]
        public void Resolve_OnlyDeprecatedMatch_IsUsedAndFlagged()
        {
            var r = TypeNameRule.Resolve("Old Thing", Catalog());

            Assert.True(r.IsResolved);
            Assert.True(r.UsedDeprecated);
        }

        [Theory]
        [InlineData("slider", "Number Slider")]
        [InlineData("plane", "XY Plane")]
        public void Resolve_AnAlias_IsAnExactWholeTextRewrite(string text, string expected)
        {
            var r = TypeNameRule.Resolve(text, Catalog());

            Assert.True(r.IsResolved);
            Assert.Equal(expected, r.Match.Name);
            Assert.Equal(expected, r.AliasedTo);
        }

        [Theory]
        [InlineData("python")]
        [InlineData("PYTHON")]
        [InlineData("Python 3 Script")]
        public void Resolve_Python_IsPinnedToTheUnifiedScriptComponent(string text)
        {
            // As before this rule existed: "Python 3 Script" and its aliases create the unified
            // Script component (named "Script"), not the dedicated Python 3 Script component.
            var r = TypeNameRule.Resolve(text, Catalog());

            Assert.True(r.IsResolved);
            Assert.Equal(ScriptGuid, r.Match.Guid);
            Assert.Equal("Script", r.Match.Name);
        }

        [Fact]
        public void Resolve_TheDedicatedPythonComponent_IsReachedByCategory()
        {
            var r = TypeNameRule.Resolve("Maths/Python 3 Script", Catalog());

            Assert.True(r.IsResolved);
            Assert.Equal(DedicatedPythonGuid, r.Match.Guid);
        }

        [Fact]
        public void Resolve_AnAliasIsNotAPattern()
        {
            // "pythons" is not "python": an alias rewrites only the exact text.
            var r = TypeNameRule.Resolve("pythons", Catalog());

            Assert.False(r.IsResolved);
            Assert.Null(r.AliasedTo);
        }

        [Fact]
        public void Resolve_APinnedName_MeansTheBuiltInEvenWhenAPluginSharesIt()
        {
            var r = TypeNameRule.Resolve("panel", Catalog());

            Assert.True(r.IsResolved);
            Assert.Equal(PanelGuid, r.Match.Guid);

            // The plugin's Panel is still reachable, by the qualified form.
            Assert.Equal(PluginPanelGuid, TypeNameRule.Resolve("Ladybug/Panel", Catalog()).Match.Guid);
        }

        [Fact]
        public void Resolve_APinNotInstalled_FallsBackToTheOrdinaryRule()
        {
            var catalog = new TypeCatalog(new[]
            {
                new TypeCandidate { Name = "Stream Filter", Category = "Sets", SubCategory = "Tree", Guid = TwinAGuid }
            });

            var r = TypeNameRule.Resolve("filter", catalog);

            Assert.True(r.IsResolved);
            Assert.Equal(TwinAGuid, r.Match.Guid);
        }

        [Fact]
        public void Resolve_ATypeWhoseOwnNameContainsASlash_IsFoundByItsWholeName()
        {
            var id = Guid.NewGuid();
            var catalog = new TypeCatalog(new[]
            {
                new TypeCandidate { Name = "Point/Vector", Category = "Vector", SubCategory = "Point", Guid = id }
            });

            Assert.Equal(id, TypeNameRule.Resolve("Point/Vector", catalog).Match.Guid);
        }

        // ---- What does not resolve -----------------------------------------------------------

        [Fact]
        public void Resolve_Line_IsAmbiguousAndListsBothWithAWorkingSpelling()
        {
            var r = TypeNameRule.Resolve("Line", Catalog());

            Assert.Equal(TypeResolutionKind.Ambiguous, r.Kind);
            Assert.Null(r.Match);
            Assert.Equal(2, r.Candidates.Count);
            var component = r.Candidates.Single(c => c.Type.Guid == LineGuid);
            var param = r.Candidates.Single(c => c.Type.Guid == LineParamGuid);
            Assert.Equal("Curve/Line", component.Use);
            Assert.Equal("Params/Line", param.Use);
            // The component is listed first: it is what a caller usually means.
            Assert.Equal(LineGuid, r.Candidates[0].Type.Guid);

            // Each offered spelling really does resolve to its candidate.
            foreach (var c in r.Candidates)
                Assert.Equal(c.Type.Guid, TypeNameRule.Resolve(c.Use, Catalog()).Match.Guid);
        }

        [Fact]
        public void Resolve_AnAliasThatLandsOnAnAmbiguousName_IsAmbiguousToo()
        {
            // No shipped alias lands on an ambiguous name in a stock install (see
            // EveryAliasAndPin_ResolvesToExactlyOneType), so a plugin twin of an alias target
            // stands in for one: the rewrite is never a licence to pick between the two.
            var catalog = new TypeCatalog(new[]
            {
                new TypeCandidate { Name = "XY Plane", Category = "Vector", SubCategory = "Plane", Guid = TwinAGuid },
                new TypeCandidate { Name = "XY Plane", Category = "Plugin", SubCategory = "Plane", Guid = TwinBGuid },
            });

            var r = TypeNameRule.Resolve("plane", catalog);

            Assert.Equal(TypeResolutionKind.Ambiguous, r.Kind);
            Assert.Equal("XY Plane", r.AliasedTo);
        }

        // ---- Every shipped alias and pin, against a stock-shaped catalog ----------------------

        // Modelled on a Rhino 8 install (names, categories and GUIDs read from one on 2026-11-09),
        // including the names a component and a parameter share. Each alias maps to the type it has
        // always created.
        private static readonly (string Name, string Category, string SubCategory, string Guid, bool Deprecated)[] StockTypes =
        {
            ("C# Script", "Maths", "Script", "b6ba1144-02d6-4a2d-b53c-ec62e290eeb7", false),
            ("C# Script", "Maths", "Script", "a9a8ebd2-fff5-4c44-a8f5-739736d129ba", true),
            ("Script", "Maths", "Script", "c9b2d725-6f87-4b07-af90-bd9aefef68eb", false),
            ("Python 3 Script", "Maths", "Script", "719467e6-7cf5-4848-99b0-c5dd57e5442c", false),
            ("Stream Filter", "Sets", "Tree", "eeafc956-268e-461d-8e73-ee05c6f72c01", false),
            ("Stream Filter", "Sets", "Tree", "3e5582a1-901a-4f7c-b58d-f5d7e3166124", true),
            ("Panel", "Params", "Input", "59e0b89a-e487-49f8-bab8-b5bab16be14c", false),
            ("Number Slider", "Params", "Input", "57da07bd-ecab-415d-9d86-af36d7073abc", false),
            ("XY Plane", "Vector", "Plane", "17b7152b-d30d-4d50-b9ef-c9fe25576fc2", false),
            ("XZ Plane", "Vector", "Plane", "8cc3a196-f6a0-49ea-9ed9-0cb343a3ae64", false),
            ("YZ Plane", "Vector", "Plane", "fad344bc-09b1-4855-a2e6-437ef5715fe3", false),
            ("Box", "Params", "Geometry", "c9482db6-bea9-448d-98ff-fed6d69a8efc", false),
            ("Rectangle", "Params", "Geometry", "abf9c670-5462-4cd8-acb3-f1ab0256dbf3", false),
            ("Rectangle", "Curve", "Primitive", "d93100b6-d50b-40b2-831a-814659dc38e3", false),
            ("Rectangle", "Curve", "Primitive", "0ca0a214-396c-44ea-b22f-d3a1757c32d6", true),
            ("Circle", "Curve", "Primitive", "807b86e3-be8d-4970-92b5-f8cdcb45b06b", false),
            ("Circle", "Params", "Geometry", "d1028c72-ff86-4057-9eb0-36c687a4d98c", false),
            ("Cylinder", "Surface", "Primitive", "0373008a-80ee-45be-887d-ab5a244afc29", false),
            ("Point", "Params", "Geometry", "fbac3e32-f100-4292-8692-77240a42fd1a", false),
            ("Construct Point", "Vector", "Point", "3581f42a-9592-4549-bd6b-1c0fc39d067b", false),
            ("Line", "Curve", "Primitive", "4c4e56eb-2f04-43f9-95a3-cc46a14f495a", false),
            ("Line", "Params", "Geometry", "8529dbdf-9b6f-42e9-8e1f-c7a2bde56a70", false),
            ("Curve", "Params", "Geometry", "d5967b9f-e8ee-436b-a8ad-29fdcecf32d5", false),
            ("Number", "Params", "Primitive", "3e8ca6be-fda8-4aaf-b5c0-3c54c8bb7312", false),
            ("Integer", "Params", "Primitive", "2e3ab970-8545-46bb-836c-1c11e5610bce", false),
            ("Boolean", "Params", "Primitive", "cb95db89-6165-43b6-9c41-5702bc5bf137", false),
            ("Text", "Params", "Primitive", "3ede854e-c753-40eb-84cb-b48008f14fd4", false),
        };

        private static TypeCatalog StockCatalog() => new TypeCatalog(StockTypes.Select(t => new TypeCandidate
        {
            Name = t.Name, Category = t.Category, SubCategory = t.SubCategory, Guid = Guid.Parse(t.Guid), Deprecated = t.Deprecated
        }));

        /// <summary>What each shipped alias has always created: the name and category of the type.</summary>
        private static readonly Dictionary<string, (string Name, string Category)> AliasMeans =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "c#", ("C# Script", "Maths") }, { "csharp", ("C# Script", "Maths") }, { "csharp script", ("C# Script", "Maths") },
                { "python", ("Script", "Maths") }, { "python script", ("Script", "Maths") }, { "ghpython", ("Script", "Maths") },
                { "plane", ("XY Plane", "Vector") }, { "xyplane", ("XY Plane", "Vector") }, { "xy", ("XY Plane", "Vector") },
                { "xzplane", ("XZ Plane", "Vector") }, { "xz", ("XZ Plane", "Vector") },
                { "yzplane", ("YZ Plane", "Vector") }, { "yz", ("YZ Plane", "Vector") },
                { "cube", ("Box", "Params") }, { "rect", ("Rectangle", "Params") }, { "circ", ("Circle", "Params") },
                { "cyl", ("Cylinder", "Surface") }, { "pt", ("Point", "Params") }, { "ln", ("Line", "Params") },
                { "crv", ("Curve", "Params") },
                { "slider", ("Number Slider", "Params") }, { "numberslider", ("Number Slider", "Params") },
                { "num", ("Number", "Params") }, { "int", ("Integer", "Params") }, { "bool", ("Boolean", "Params") },
                { "str", ("Text", "Params") }, { "string", ("Text", "Params") },
                { "streamfilter", ("Stream Filter", "Sets") }, { "filter", ("Stream Filter", "Sets") },
            };

        [Fact]
        public void EveryAliasAndPin_ResolvesToExactlyOneType()
        {
            var catalog = StockCatalog();
            var failures = new List<string>();

            foreach (var alias in TypeNameRule.Aliases)
            {
                var r = TypeNameRule.Resolve(alias.Key, catalog);
                if (!r.IsResolved)
                {
                    failures.Add($"alias '{alias.Key}' -> '{alias.Value}' is {r.Kind}");
                    continue;
                }
                if (r.UsedDeprecated)
                    failures.Add($"alias '{alias.Key}' -> '{alias.Value}' lands on a deprecated type");
                if (!AliasMeans.TryGetValue(alias.Key, out var expected))
                    failures.Add($"alias '{alias.Key}' has no expected type in this test: add one");
                else if (r.Match.Name != expected.Name || r.Match.Category != expected.Category)
                    failures.Add($"alias '{alias.Key}' created {r.Match.PathText}, expected {expected.Category}/…/{expected.Name}");
            }

            foreach (var pin in TypeNameRule.Pinned)
            {
                var r = TypeNameRule.Resolve(pin.Key, catalog);
                if (!r.IsResolved)
                    failures.Add($"pinned '{pin.Key}' is {r.Kind}");
            }

            Assert.Empty(failures);
        }

        [Theory]
        [InlineData("Rectangle")]
        [InlineData("Circle")]
        [InlineData("Line")]
        public void StockCatalog_HasTheComponentParameterTwins(string name)
        {
            // Guards the test above: if the fixture lost a twin, an unqualified alias target would
            // pass there without being safe on a real install.
            Assert.Equal(TypeResolutionKind.Ambiguous, TypeNameRule.Resolve(name, StockCatalog()).Kind);
        }

        [Fact]
        public void Resolve_TwinsInTheSameSubcategory_AreOfferedByGuid()
        {
            var r = TypeNameRule.Resolve("Twin", Catalog());

            Assert.Equal(TypeResolutionKind.Ambiguous, r.Kind);
            Assert.Contains(r.Candidates, c => c.Use == TwinAGuid.ToString("D"));
            Assert.Contains(r.Candidates, c => c.Use == TwinBGuid.ToString("D"));
        }

        [Fact]
        public void Resolve_NeverLoose_APartialNameIsOnlyASuggestion()
        {
            // The old registry created the shortest name containing the text. That type is now the
            // first suggestion, and nothing is resolved.
            var r = TypeNameRule.Resolve("Cull", Catalog());

            Assert.Equal(TypeResolutionKind.NotFound, r.Kind);
            Assert.Null(r.Match);
            Assert.Equal(new[] { "Cull Index", "Cull Pattern" }, r.Suggestions.Select(s => s.Type.Name).ToArray());
        }

        [Fact]
        public void Resolve_ATypo_SuggestsTheNearName()
        {
            var r = TypeNameRule.Resolve("Cull Patern", Catalog());

            Assert.Equal(TypeResolutionKind.NotFound, r.Kind);
            Assert.Equal("Cull Pattern", r.Suggestions[0].Type.Name);
        }

        [Fact]
        public void Resolve_Suggestions_DropADeprecatedTwinOfACurrentType()
        {
            var r = TypeNameRule.Resolve("Merg", Catalog());

            Assert.Single(r.Suggestions);
            Assert.Equal(MergeGuid, r.Suggestions[0].Type.Guid);
            Assert.Equal("Merge", r.Suggestions[0].Use);
        }

        [Fact]
        public void Resolve_AnUninstalledCategory_IsReported()
        {
            var r = TypeNameRule.Resolve("Kangaroo2/Goals-Pt/Anchor", Catalog());

            Assert.Equal(TypeResolutionKind.CategoryMissing, r.Kind);
            Assert.Equal("Kangaroo2", r.Category);
        }

        [Fact]
        public void Resolve_AnInstalledCategoryWithoutThatName_IsNotFound()
        {
            var r = TypeNameRule.Resolve("Sets/Line", Catalog());

            Assert.Equal(TypeResolutionKind.NotFound, r.Kind);
        }

        [Fact]
        public void Resolve_AnUnknownGuid_IsNotInstalledAndCarriesTheGuid()
        {
            var id = Guid.NewGuid();
            var r = TypeNameRule.Resolve(id.ToString(), Catalog());

            Assert.Equal(TypeResolutionKind.GuidNotInstalled, r.Kind);
            Assert.Equal(id, r.Guid);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_NoText_IsMissing(string text)
        {
            Assert.Equal(TypeResolutionKind.Missing, TypeNameRule.Resolve(text, Catalog()).Kind);
        }

        [Theory]
        [InlineData("/Merge")]
        [InlineData("Sets//Merge")]
        [InlineData("Merge ()")]
        public void Resolve_AMalformedQualifiedForm_IsNotFoundRatherThanGuessed(string text)
        {
            Assert.False(TypeNameRule.Resolve(text, Catalog()).IsResolved);
        }

        // ---- What the caller is told ---------------------------------------------------------

        [Fact]
        public void Explain_Ambiguous_ListsEveryCandidateAndHowToWriteIt()
        {
            var message = TypeNameRule.Explain(TypeNameRule.Resolve("Line", Catalog()));

            Assert.Contains("'Line' matches 2 component types. Nothing was created.", message);
            Assert.Contains($"Curve/Primitive/Line (component, Grasshopper, {LineGuid:D}), write 'Curve/Line'", message);
            Assert.Contains($"Params/Geometry/Line (parameter, Grasshopper, {LineParamGuid:D}), write 'Params/Line'", message);
            Assert.Contains("as 'type'", message);
        }

        [Fact]
        public void Explain_NotFound_SaysNothingWasCreatedAndOffersSuggestions()
        {
            var message = TypeNameRule.Explain(TypeNameRule.Resolve("Cull", Catalog()));

            Assert.Contains("No component type is named 'Cull'", message);
            Assert.Contains("never partially", message);
            Assert.Contains("Nothing was created.", message);
            Assert.Contains("Did you mean 'Cull Index' (Sets/Sequence/Cull Index (component", message);
            Assert.Contains("gh_canvas(action='search'", message);
        }

        [Fact]
        public void Explain_UsesTheCallersArgumentAndConsequence()
        {
            var message = TypeNameRule.Explain(TypeNameRule.Resolve("", Catalog()), argName: "component");

            Assert.StartsWith("'component' is required", message);
            Assert.Contains("No documentation", TypeNameRule.Explain(TypeNameRule.Resolve("Cull", Catalog()),
                consequence: "No documentation was returned."));
        }

        [Fact]
        public void Explain_Resolved_IsNull()
        {
            Assert.Null(TypeNameRule.Explain(TypeNameRule.Resolve("Merge", Catalog())));
        }

        [Fact]
        public void ErrorPayload_Ambiguous_CarriesStructuredCandidates()
        {
            var payload = TypeNameRule.ErrorPayload(TypeNameRule.Resolve("Line", Catalog()));

            Assert.Equal(false, payload["success"]);
            Assert.Equal("ambiguous_name", payload["code"]);
            Assert.Equal(2, payload["matchCount"]);
            var matches = (List<Dictionary<string, object>>)payload["matches"];
            Assert.Contains(matches, m => (string)m["use"] == "Curve/Line" && (string)m["role"] == "component"
                                          && (string)m["plugin"] == "Grasshopper" && (string)m["guid"] == LineGuid.ToString("D"));
            Assert.False(payload.ContainsKey("suggestions"));
        }

        [Fact]
        public void ErrorPayload_NotFound_CarriesSuggestions()
        {
            var payload = TypeNameRule.ErrorPayload(TypeNameRule.Resolve("Cull", Catalog()));

            Assert.Equal("unknown_type", payload["code"]);
            var suggestions = (List<Dictionary<string, object>>)payload["suggestions"];
            Assert.Equal("Cull Index", suggestions[0]["use"]);
        }

        [Theory]
        [InlineData("Params", "Input", "input")]
        [InlineData("Params", "Geometry", "parameter")]
        [InlineData("Curve", "Primitive", "component")]
        public void RoleOf_FollowsTheCategory(string category, string sub, string role)
        {
            Assert.Equal(role, TypeCandidate.RoleOf(category, sub));
        }
    }
}
