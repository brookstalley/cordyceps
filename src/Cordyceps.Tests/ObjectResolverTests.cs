using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// The ambiguity matrix for <see cref="ObjectResolver"/>: every reading (GUID, GUID prefix,
    /// nickname, group name) on its own, every way two readings collide, both scopes, and the
    /// display names that must always resolve back. Nothing here is ever guessed: each failing case
    /// pins the whole message, because agents act on its candidate ids.
    /// </summary>
    public class ObjectResolverTests
    {
        private static readonly Guid A = TestCanvas.Id(1), B = TestCanvas.Id(2), C = TestCanvas.Id(3);
        private static readonly Guid Hexy = Guid.Parse("abcdef01-2345-4678-9abc-def012345678");
        private static readonly Guid Posts = TestCanvas.Id(0x10);
        private static readonly Guid Unnamed = TestCanvas.Id(0x11);
        private static readonly Guid Bridge = TestCanvas.Id(0xb0b);

        /// <summary>A small canvas: COUNT, a shared default "Pt", a hex-id object, a POSTS group, an unnamed group, the bridge.</summary>
        private static CanvasSnapshot Canvas() => TestCanvas.Snap(
            new[]
            {
                TestCanvas.Node(A, "COUNT", "Series"),
                TestCanvas.Node(B, "Pt", "Point", isDefault: true),
                TestCanvas.Node(C, "Pt", "Point", isDefault: true),
                TestCanvas.Node(Hexy, "Circle", "Circle", isDefault: true),
                TestCanvas.Node(Bridge, "MCP", "Cordyceps")
            },
            new[] { TestCanvas.Group(Posts, "POSTS", new[] { A }), TestCanvas.Group(Unnamed, "", new[] { B }) },
            infra: new[] { Bridge });

        private static Resolution Obj(IObjectIndex index, string text) => ObjectResolver.Resolve(index, text, ResolveScope.Objects);
        private static Resolution Grp(IObjectIndex index, string text) => ObjectResolver.Resolve(index, text, ResolveScope.Groups);

        private static void AssertResolved(Resolution r, Guid id, MatchedBy by)
        {
            Assert.True(r.IsResolved, r.Error);
            Assert.Equal(id, r.Entry.Id);
            Assert.Equal(by, r.MatchedBy);
            Assert.Null(r.Error);
        }

        private static void AssertFailed(Resolution r, string message)
        {
            Assert.False(r.IsResolved);
            Assert.Null(r.Entry);
            Assert.Equal(message, r.Error);
        }

        // ---- full GUID ----

        [Fact]
        public void FullGuid_Present_Resolves()
        {
            AssertResolved(Obj(Canvas(), Hexy.ToString()), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void FullGuid_WithBraces_Resolves()
        {
            AssertResolved(Obj(Canvas(), "{" + Hexy + "}"), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void FullGuid_Uppercase_Resolves()
        {
            AssertResolved(Obj(Canvas(), Hexy.ToString().ToUpperInvariant()), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void FullGuid_Absent_GivesTodaysText()
        {
            var absent = Guid.Parse("12345678-0000-4000-8000-000000000000");
            AssertFailed(Obj(Canvas(), absent.ToString().ToUpperInvariant()), $"Component not found: {absent:D}");
        }

        [Fact]
        public void FullGuid_OfInfrastructure_GivesTheSameTextAsAbsent()
        {
            AssertFailed(Obj(Canvas(), Bridge.ToString()), $"Component not found: {Bridge:D}");
        }

        [Fact]
        public void FullGuid_IsReadOnlyAsAGuid_ANicknameEqualToItIsIgnored()
        {
            var absent = Guid.Parse("12345678-0000-4000-8000-000000000000");
            var snap = TestCanvas.Snap(new[]
            {
                TestCanvas.Node(A, B.ToString()),       // A's nickname is B's GUID text
                TestCanvas.Node(B, "b"),
                TestCanvas.Node(C, absent.ToString())   // C's nickname is a GUID nobody has
            });

            AssertResolved(Obj(snap, B.ToString()), B, MatchedBy.Guid);
            AssertFailed(Obj(snap, absent.ToString()), $"Component not found: {absent:D}");
        }

        // ---- nicknames ----

        [Fact]
        public void Nickname_Unique_Resolves()
        {
            AssertResolved(Obj(Canvas(), "COUNT"), A, MatchedBy.Nickname);
        }

        [Fact]
        public void Nickname_CaseDiffers_IsNotFound_AndSuggestsTheRightCase()
        {
            AssertFailed(Obj(Canvas(), "count"), "No object named 'count'. Did you mean 'COUNT'?");
        }

        [Fact]
        public void Nickname_Untrimmed_IsNotFound_AndSuggestsTheTrimmedName()
        {
            AssertFailed(Obj(Canvas(), " COUNT"), "No object named ' COUNT'. Did you mean 'COUNT'?");
        }

        [Fact]
        public void Nickname_SharedByTwo_ListsBothWithIdsAndTypes()
        {
            AssertFailed(Obj(Canvas(), "Pt"),
                "'Pt' is the nickname of 2 objects: 00000002 Point, 00000003 Point; name one by its id");
        }

        [Fact]
        public void Nickname_SharedByTwelve_ListsTenThenCountsTheRest()
        {
            var nodes = Enumerable.Range(1, 12).Select(i => TestCanvas.Node(TestCanvas.Id(i), "Pt", "Point", isDefault: true));
            var listed = string.Join(", ", Enumerable.Range(1, 10).Select(i => $"{i:x8} Point"));
            AssertFailed(Obj(TestCanvas.Snap(nodes), "Pt"),
                $"'Pt' is the nickname of 12 objects: {listed}, and 2 more; name one by its id");
        }

        [Fact]
        public void Nickname_UniqueDefault_Resolves()
        {
            AssertResolved(Obj(Canvas(), "Circle"), Hexy, MatchedBy.Nickname);
        }

        [Fact]
        public void Nickname_OfInfrastructure_IsNotFound_WithTheSameTextAsAnUnknownName()
        {
            var withoutBridge = TestCanvas.Snap(new[] { TestCanvas.Node(A, "COUNT") });
            var withBridge = TestCanvas.Snap(new[] { TestCanvas.Node(A, "COUNT"), TestCanvas.Node(Bridge, "MCP") }, infra: new[] { Bridge });

            AssertFailed(Obj(withoutBridge, "MCP"), "No object named 'MCP'.");
            AssertFailed(Obj(withBridge, "MCP"), "No object named 'MCP'.");
        }

        [Fact]
        public void Infrastructure_StaysHidden_EvenWhenAnIndexForgetsToFilterIt()
        {
            var leaky = new LeakyIndex(Canvas(), new ObjectEntry(Bridge, "MCP", false, "Cordyceps", false));

            AssertFailed(Obj(leaky, "MCP"), "No object named 'MCP'.");
            AssertFailed(Obj(leaky, Bridge.ToString()), $"Component not found: {Bridge:D}");
            AssertFailed(Obj(leaky, "00000b0b"), "No object named '00000b0b'.");
            AssertFailed(Grp(leaky, Bridge.ToString()), $"Group not found: {Bridge}");
        }

        // ---- prefixes ----

        [Fact]
        public void Prefix_EightHex_Resolves()
        {
            AssertResolved(Obj(Canvas(), "abcdef01"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Prefix_Uppercase_Resolves()
        {
            AssertResolved(Obj(Canvas(), "ABCDEF01"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Prefix_WithAHyphenInItsDPosition_Resolves()
        {
            AssertResolved(Obj(Canvas(), "abcdef01-2345"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Prefix_WithAMisplacedHyphen_MatchesNothing()
        {
            AssertFailed(Obj(Canvas(), "abcdef0-12345"), "No object named 'abcdef0-12345'.");
        }

        [Theory]
        [InlineData("Add")]
        [InlineData("beef")]
        [InlineData("DEADBEE")]
        public void ShortHexLettersWithNoDigit_GetNoPrefixHint(string text)
        {
            // A mistyped nickname made only of the letters a-f is far likelier than a short prefix.
            AssertFailed(Obj(Canvas(), text), $"No object named '{text}'.");
        }

        [Fact]
        public void Prefix_SevenHex_GetsTheEightDigitHint()
        {
            AssertFailed(Obj(Canvas(), "abcdef0"), "No object named 'abcdef0'. A GUID prefix needs at least 8 hex digits.");
        }

        [Fact]
        public void Prefix_LongerThan35_IsNotAPrefix()
        {
            Assert.False(ObjectResolver.IsGuidPrefixShaped(new string('a', 36)));
            Assert.True(ObjectResolver.IsGuidPrefixShaped(new string('a', 35)));
            Assert.False(ObjectResolver.IsGuidPrefixShaped("abcdef0-"));   // 7 hex digits
            Assert.False(ObjectResolver.IsGuidPrefixShaped("abcdefg1"));   // not hex
        }

        [Fact]
        public void Prefix_SharedByTwo_ListsBoth_ByAnIdThatWorks()
        {
            var p = Guid.Parse("9ac1d04b-0000-4000-8000-000000000001");
            var q = Guid.Parse("9ac1d04b-0000-4000-8000-000000000002");
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(p, "p"), TestCanvas.Node(q, "q") });

            AssertFailed(Obj(snap, "9ac1d04b"), $"GUID prefix '9ac1d04b' matches 2 objects: {p:D} Addition, {q:D} Addition; give more characters");
            Assert.Equal(p.ToString("D"), new DisplayNames(snap).DisplayId(p));
        }

        [Fact]
        public void Prefix_SharedOnlyWithInfrastructure_ResolvesTheUserObject()
        {
            var user = Guid.Parse("9ac1d04b-0000-4000-8000-000000000001");
            var infra = Guid.Parse("9ac1d04b-0000-4000-8000-000000000002");
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(user, "u"), TestCanvas.Node(infra, "MCP") }, infra: new[] { infra });

            AssertResolved(Obj(snap, "9ac1d04b"), user, MatchedBy.GuidPrefix);
            Assert.Equal("9ac1d04b", new DisplayNames(snap).ShortId(user));
        }

        // ---- hex nicknames ----

        [Fact]
        public void HexNickname_NamingADifferentObjectThanItsPrefixReading_IsAnError()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "00000002"), TestCanvas.Node(B, "b", "Point") });

            AssertFailed(Obj(snap, "00000002"),
                $"'00000002' names two objects: nickname → 00000001 Addition; GUID prefix → {B:D} Point. Use a longer GUID prefix");
        }

        [Fact]
        public void HexNickname_NamingTheSameObjectAsItsPrefixReading_Resolves()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "00000001"), TestCanvas.Node(B, "b") });

            AssertResolved(Obj(snap, "00000001"), A, MatchedBy.Nickname);
        }

        // ---- destructive intent: a default nickname never selects what is removed ----

        private static Resolution DelObj(IObjectIndex index, string text) => ObjectResolver.Resolve(index, text, ResolveScope.Objects, ResolveIntent.Destructive);
        private static Resolution DelGrp(IObjectIndex index, string text) => ObjectResolver.Resolve(index, text, ResolveScope.Groups, ResolveIntent.Destructive);

        private static readonly Guid DefaultGroup = TestCanvas.Id(0x20);
        private static readonly Guid DefaultGroup2 = TestCanvas.Id(0x21);

        [Fact]
        public void Destructive_UniqueDefaultNickname_IsRefused_NamingTheIdAndTheRename()
        {
            AssertFailed(DelObj(Canvas(), "Circle"),
                "'Circle' is the default nickname of abcdef01 Circle, which every new Circle gets, so it never selects what this action removes. "
                + "Pass id 'abcdef01', or name it first with gh_canvas(action='rename', id='abcdef01', nickname='NEW') and pass that name");
        }

        [Fact]
        public void Ordinary_UniqueDefaultNickname_StillResolves()
        {
            AssertResolved(ObjectResolver.Resolve(Canvas(), "Circle", ResolveScope.Objects, ResolveIntent.Ordinary), Hexy, MatchedBy.Nickname);
        }

        [Fact]
        public void Destructive_SetNicknameGuidAndPrefix_ResolveAsBefore()
        {
            AssertResolved(DelObj(Canvas(), "COUNT"), A, MatchedBy.Nickname);
            AssertResolved(DelObj(Canvas(), Hexy.ToString()), Hexy, MatchedBy.Guid);
            AssertResolved(DelObj(Canvas(), "abcdef01"), Hexy, MatchedBy.GuidPrefix);
            AssertResolved(DelGrp(Canvas(), "POSTS"), Posts, MatchedBy.GroupName);
        }

        [Fact]
        public void Destructive_RefusalIdItPrints_ResolvesUnderTheSameIntent()
        {
            AssertResolved(DelObj(Canvas(), "abcdef01"), Hexy, MatchedBy.GuidPrefix);
            AssertResolved(DelObj(Canvas(), "00000002"), B, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Destructive_SharedDefaultNickname_SaysNoNameWorks_AndListsEveryId()
        {
            AssertFailed(DelObj(Canvas(), "Pt"),
                "'Pt' is the default nickname of 2 objects: 00000002 Point, 00000003 Point. "
                + "A default nickname never selects what this action removes; pass one of these ids");
        }

        [Fact]
        public void Destructive_NicknameSharedBySetAndDefault_KeepsTheOrdinaryAmbiguityText()
        {
            // Renaming the default one would leave the set nickname unique, and that would then work.
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "Pt", "Point"), TestCanvas.Node(B, "Pt", "Point", isDefault: true) });
            AssertFailed(DelObj(snap, "Pt"), "'Pt' is the nickname of 2 objects: 00000001 Point, 00000002 Point; name one by its id");
        }

        [Fact]
        public void Destructive_DefaultNicknameThatIsAlsoThePrefixOfTheSameObject_ResolvesByPrefix()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(Hexy, "abcdef01", "Point", isDefault: true), TestCanvas.Node(A, "a") });
            AssertResolved(DelObj(snap, "abcdef01"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Destructive_NotFound_NeverSuggestsADefaultNickname()
        {
            Assert.Equal("No object named 'Circl'. Did you mean 'Circle'?", Obj(Canvas(), "Circl").Error);
            AssertFailed(DelObj(Canvas(), "Circl"), "No object named 'Circl'.");
        }

        [Fact]
        public void DefaultGroupName_IsMarkedDefault_OnlyForTheExactText()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") },
                new[] { TestCanvas.Group(DefaultGroup, "Group"), TestCanvas.Group(DefaultGroup2, "group"), TestCanvas.Group(Posts, "POSTS") });
            Assert.True(snap.Find(DefaultGroup).IsDefaultNickName);
            Assert.False(snap.Find(DefaultGroup2).IsDefaultNickName);
            Assert.False(snap.Find(Posts).IsDefaultNickName);
        }

        [Fact]
        public void Destructive_UniqueDefaultGroupName_IsRefused_NamingTheIdAndTheGroupRename()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") }, new[] { TestCanvas.Group(DefaultGroup, "Group", new[] { A }) });

            AssertResolved(Grp(snap, "Group"), DefaultGroup, MatchedBy.GroupName);
            AssertFailed(DelGrp(snap, "Group"),
                "'Group' is the default name of group 00000020, which every new group gets, so it never selects what this action removes. "
                + "Pass id '00000020', or name the group first with gh_canvas(action='group_rename', id='00000020', name='NEW') and pass that name");
            AssertResolved(DelGrp(snap, "00000020"), DefaultGroup, MatchedBy.GuidPrefix);
            AssertResolved(DelGrp(snap, DefaultGroup.ToString()), DefaultGroup, MatchedBy.Guid);
        }

        [Fact]
        public void Destructive_SharedDefaultGroupName_SaysNoNameWorks_AndListsEveryId()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") },
                new[] { TestCanvas.Group(DefaultGroup, "Group"), TestCanvas.Group(DefaultGroup2, "Group") });

            AssertFailed(DelGrp(snap, "Group"),
                "'Group' is the default name of 2 groups: 00000020 Group, 00000021 Group. "
                + "A default group name never selects what this action removes; pass one of these ids");
        }

        [Fact]
        public void Many_Destructive_RefusesOnlyTheDefaultEntry()
        {
            var results = ObjectResolver.ResolveMany(Canvas(), new[] { "COUNT", "Circle", "abcdef01" }, ResolveScope.Objects, ResolveIntent.Destructive);

            AssertResolved(results[0].Resolution, A, MatchedBy.Nickname);
            Assert.False(results[1].Resolution.IsResolved);
            Assert.StartsWith("'Circle' is the default nickname of abcdef01 Circle", results[1].Resolution.Error);
            AssertResolved(results[2].Resolution, Hexy, MatchedBy.GuidPrefix);
            Assert.Null(results[2].DuplicateOf);
        }

        // ---- unpinned outcomes ----

        private static readonly Guid P1 = Guid.Parse("abcdef01-1111-4111-8111-111111111111");
        private static readonly Guid P2 = Guid.Parse("abcdef01-2222-4222-8222-222222222222");
        private static readonly Guid Nick = TestCanvas.Id(0x40);

        [Fact]
        public void UniqueNickname_ThatIsAlsoAnAmbiguousPrefix_IsAnError_NotAResolution()
        {
            var snap = TestCanvas.Snap(new[]
            {
                TestCanvas.Node(Nick, "abcdef01", "Point"),
                TestCanvas.Node(P1, "p1"),
                TestCanvas.Node(P2, "p2")
            });

            var r = Obj(snap, "abcdef01");

            Assert.False(r.IsResolved);
            Assert.Null(r.Entry);
            // The ambiguous prefix is reported on its own: the unique nickname does not break the tie.
            AssertFailed(r, "GUID prefix 'abcdef01' matches 2 objects: abcdef01-1 Addition, abcdef01-2 Addition; give more characters");
        }

        [Fact]
        public void GroupsScope_PrefixLandingOnOneGroup_Resolves()
        {
            var g1 = Guid.Parse("abcdef01-1111-4111-8111-111111111111");
            var g2 = Guid.Parse("beadfeed-1111-4111-8111-111111111111");
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") }, new[] { TestCanvas.Group(g1, "ONE"), TestCanvas.Group(g2, "TWO") });

            AssertResolved(Grp(snap, "abcdef01"), g1, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void GroupsScope_PrefixLandingOnTwoGroups_IsTheAmbiguousPrefixError()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") },
                new[] { TestCanvas.Group(P1, "ONE"), TestCanvas.Group(P2, "TWO") });

            var r = Grp(snap, "abcdef01");

            Assert.False(r.IsResolved);
            AssertFailed(r, "GUID prefix 'abcdef01' matches 2 objects: abcdef01-1 Group, abcdef01-2 Group; give more characters");
        }

        [Fact]
        public void NFormGuid_32Hex_IsReadAsAGuid()
        {
            // Guid.TryParse accepts the 32-digit N form, so it is read only as a GUID.
            AssertResolved(Obj(Canvas(), Hexy.ToString("N")), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void GuidWithSurroundingWhitespace_IsTrimmedByGuidTryParse_AndReadAsAGuid()
        {
            // Guid.TryParse tolerates leading and trailing white space, unlike names, which are never trimmed.
            AssertResolved(Obj(Canvas(), "  " + Hexy.ToString() + " \t"), Hexy, MatchedBy.Guid);
        }

        // ---- Objects scope and groups ----

        [Fact]
        public void ObjectsScope_GroupName_IsNotFound_WithTheGroupHint_AndNoFallback()
        {
            AssertFailed(Obj(Canvas(), "POSTS"),
                "No object named 'POSTS'. 'POSTS' is a group; group actions take its name, and delete, move, rename, info, bounds and zoom take its id (00000010).");
        }

        [Fact]
        public void ObjectsScope_GroupGuidAndGroupPrefix_BothResolve()
        {
            AssertResolved(Obj(Canvas(), Posts.ToString()), Posts, MatchedBy.Guid);
            AssertResolved(Obj(Canvas(), "00000010"), Posts, MatchedBy.GuidPrefix);
        }

        // ---- Groups scope ----

        [Fact]
        public void GroupsScope_UniqueName_Resolves()
        {
            AssertResolved(Grp(Canvas(), "POSTS"), Posts, MatchedBy.GroupName);
        }

        [Fact]
        public void GroupsScope_CaseDiffers_IsNotFound_AndSuggests()
        {
            AssertFailed(Grp(Canvas(), "posts"), "No group named 'posts'. Did you mean 'POSTS'?");
        }

        [Fact]
        public void GroupsScope_TwoGroupsWithOneName_ListsBoth()
        {
            var g1 = TestCanvas.Id(0x20);
            var g2 = TestCanvas.Id(0x21);
            var snap = TestCanvas.Snap(Array.Empty<CanvasNode>(), new[] { TestCanvas.Group(g1, "WALLS"), TestCanvas.Group(g2, "WALLS") });

            AssertFailed(Grp(snap, "WALLS"), "'WALLS' is the name of 2 groups: 00000020 Group, 00000021 Group; name one by its id");
        }

        [Fact]
        public void GroupsScope_ComponentGuidAndPrefix_AreNotGroups()
        {
            AssertFailed(Grp(Canvas(), A.ToString()), $"Object is not a group: {A}");
            AssertFailed(Grp(Canvas(), "00000001"), "Object is not a group: 00000001");
        }

        [Fact]
        public void GroupsScope_MissingOrInfrastructureGuid_IsGroupNotFound()
        {
            var absent = Guid.Parse("12345678-0000-4000-8000-000000000000").ToString();
            AssertFailed(Grp(Canvas(), absent), $"Group not found: {absent}");
            AssertFailed(Grp(Canvas(), Bridge.ToString()), $"Group not found: {Bridge}");
        }

        [Fact]
        public void GroupsScope_ObjectNickname_GetsTheHint()
        {
            AssertFailed(Grp(Canvas(), "COUNT"), "No group named 'COUNT'. 'COUNT' is an object's nickname, not a group.");
        }

        [Fact]
        public void GroupsScope_UnnamedGroup_IsReachableByIdOnly()
        {
            var snap = Canvas();
            AssertResolved(Grp(snap, Unnamed.ToString()), Unnamed, MatchedBy.Guid);
            AssertResolved(Grp(snap, "00000011"), Unnamed, MatchedBy.GuidPrefix);
            AssertFailed(Grp(snap, ""), "Component ID is required");
            Assert.Equal("00000011", new DisplayNames(snap).DisplayName(Unnamed));
        }

        [Fact]
        public void GroupsScope_GroupNameAndAComponentPrefix_NamingDifferentObjects_IsAnError()
        {
            var g = TestCanvas.Id(0x20);
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") }, new[] { TestCanvas.Group(g, "00000001") });

            AssertFailed(Grp(snap, "00000001"),
                "'00000001' names two objects: group name → 00000020 Group; GUID prefix → 00000001 Addition. Use a longer GUID prefix");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void EmptyOrWhitespace_IsRequired_InBothScopes(string text)
        {
            AssertFailed(Obj(Canvas(), text), "Component ID is required");
            AssertFailed(Grp(Canvas(), text), "Component ID is required");
        }

        [Fact]
        public void NotFound_HintsAppearInOrder()
        {
            // "POSTZ" is close to the nickname POSTS (object) and is not a group: Did-you-mean only.
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "POSTS") }, new[] { TestCanvas.Group(Posts, "beef") });

            AssertFailed(Obj(snap, "POSTZ"), "No object named 'POSTZ'. Did you mean 'POSTS'?");
            // "beef" is a group and all hex letters, but has no decimal digit: the group hint only.
            AssertFailed(Obj(snap, "beef"),
                "No object named 'beef'. 'beef' is a group; group actions take its name, and delete, move, rename, info, bounds and zoom take its id (00000010).");
            // "bee4" is short hex with a digit. Suggestions come from object nicknames only, so the
            // group "beef" is not offered; the prefix hint stands alone.
            AssertFailed(Obj(snap, "bee4"),
                "No object named 'bee4'. A GUID prefix needs at least 8 hex digits.");
        }

        // ---- display names ----

        [Fact]
        public void Display_UsesNicknameOnlyWhenItIsNotDefaultAndResolves()
        {
            var snap = Canvas();
            var names = new DisplayNames(snap);

            Assert.Equal("COUNT", names.DisplayName(A));
            Assert.Equal("00000002", names.DisplayName(B));        // shared default
            Assert.Equal("abcdef01", names.DisplayName(Hexy));     // unique, but a default nickname
            Assert.Equal("POSTS", names.DisplayName(Posts));
            Assert.Equal("00000010", names.DisplayId(Posts));
        }

        [Fact]
        public void Display_FallsBackToTheFullGuid_WhenANicknameShadowsTheShortId()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "00000002"), TestCanvas.Node(B, "b") });
            var names = new DisplayNames(snap);

            Assert.Equal("00000002", names.ShortId(B));
            Assert.Equal(B.ToString("D"), names.DisplayId(B));
            Assert.Equal("b", names.DisplayName(B));
            Assert.Equal("00000001", names.DisplayName(A)); // its nickname is ambiguous with B's prefix
        }

        [Fact]
        public void Display_AGroupWithTheDefaultName_PrintsItsId()
        {
            // group_delete refuses the default group name even when one group carries it, so the
            // name outline prints for that group must be one group_delete accepts: its id.
            var g = new Guid("00000030-0000-0000-0000-000000000000");
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "COUNT") }, new[] { TestCanvas.Group(g, "Group") });
            var names = new DisplayNames(snap);

            Assert.Equal("00000030", names.DisplayName(g));
            Assert.True(ObjectResolver.Resolve(snap, names.DisplayName(g), ResolveScope.Groups, ResolveIntent.Destructive).IsResolved);
            Assert.False(ObjectResolver.Resolve(snap, "Group", ResolveScope.Groups, ResolveIntent.Destructive).IsResolved);
        }

        [Fact]
        public void Display_GroupShortId_MustResolveInBothScopes()
        {
            // Group G2's short id equals group G1's name: in the Groups scope it reads two ways.
            var g1 = TestCanvas.Id(0x20);
            var g2 = TestCanvas.Id(0x21);
            var snap = TestCanvas.Snap(Array.Empty<CanvasNode>(), new[] { TestCanvas.Group(g1, "00000021"), TestCanvas.Group(g2, "") });
            var names = new DisplayNames(snap);

            Assert.Equal(g2.ToString("D"), names.DisplayId(g2));
            Assert.Equal("00000020", names.DisplayName(g1)); // its name is ambiguous with g2's prefix
            AssertRoundTrips(snap, names);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        [InlineData(1234)]
        [InlineData(99991)]
        public void Display_EveryNameAndIdRoundTrips_OnAGeneratedCanvas(int seed)
        {
            var snap = Generate(new Random(seed));
            var names = new DisplayNames(snap);
            AssertRoundTrips(snap, names);

            // The fixture must reach the fallbacks, or the round trip proves little.
            var entries = snap.Entries.ToList();
            Assert.Contains(entries, e => names.ShortId(e.Id).Length > 8);
            Assert.Contains(entries, e => names.DisplayId(e.Id).Length == 36);
            Assert.Contains(entries, e => !e.IsGroup && names.DisplayName(e.Id) == e.NickName);
            Assert.Contains(entries, e => e.IsGroup && e.NickName.Length > 0 && names.DisplayName(e.Id) == e.NickName);
            Assert.Contains(entries, e => !e.IsGroup && e.NickName.Length > 0 && !e.IsDefaultNickName && names.DisplayName(e.Id) != e.NickName);
        }

        private static void AssertRoundTrips(CanvasSnapshot snap, DisplayNames names)
        {
            foreach (var e in snap.Entries)
            {
                var scope = e.IsGroup ? ResolveScope.Groups : ResolveScope.Objects;
                var name = names.DisplayName(e.Id);
                var id = names.DisplayId(e.Id);

                var byName = ObjectResolver.Resolve(snap, name, scope);
                Assert.True(byName.IsResolved && byName.Entry.Id == e.Id, $"DisplayName '{name}' of {e.Id} gave: {byName.Error ?? byName.Entry.Id.ToString()}");

                var byId = ObjectResolver.Resolve(snap, id, scope);
                Assert.True(byId.IsResolved && byId.Entry.Id == e.Id, $"DisplayId '{id}' of {e.Id} gave: {byId.Error ?? byId.Entry.Id.ToString()}");

                // A printed id works on object actions too, groups included.
                var byIdAsObject = ObjectResolver.Resolve(snap, id, ResolveScope.Objects);
                Assert.True(byIdAsObject.IsResolved && byIdAsObject.Entry.Id == e.Id, $"DisplayId '{id}' of {e.Id} in Objects scope gave: {byIdAsObject.Error}");
            }
        }

        /// <summary>
        /// A canvas built to collide: ids drawn from a few shared 8-character stems, nicknames from
        /// a small pool that includes hex text equal to those stems and to other objects' GUIDs,
        /// shared and default nicknames, groups named like nicknames or stems, and unnamed groups.
        /// </summary>
        private static CanvasSnapshot Generate(Random rng)
        {
            string Hex(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => "0123456789abcdef"[rng.Next(16)]));
            var stems = Enumerable.Range(0, 6).Select(_ => Hex(8)).ToList();

            Guid NewId()
            {
                // Half the ids share a stem, a third of those share the next block too, so short ids grow past 8 and 13.
                var head = rng.Next(2) == 0
                    ? stems[rng.Next(stems.Count)] + "-" + (rng.Next(3) == 0 ? "0000" : Hex(4))
                    : Hex(8) + "-" + Hex(4);
                return Guid.Parse($"{head}-4{Hex(3)}-8{Hex(3)}-{Hex(12)}");
            }

            var ids = new List<Guid>();
            var seen = new HashSet<Guid>();
            while (ids.Count < 120)
            {
                var id = NewId();
                if (seen.Add(id)) ids.Add(id);
            }

            var pool = new List<string> { "Pt", "Crv", "COUNT", "RADIUS", "POSTS", "", "Circle" };
            pool.AddRange(stems);
            pool.Add(stems[0].Substring(0, 7));
            pool.Add(ids[5].ToString());
            pool.Add(ids[6].ToString("D").Substring(0, 10));
            // Real short ids as nicknames, so some objects' short ids are shadowed.
            var prefixes = new GuidPrefixIndex(ids.Take(110));
            pool.Add(prefixes.Shortest(ids[10]));
            pool.Add(prefixes.Shortest(ids[11]));

            var nodes = new List<CanvasNode>();
            var groups = new List<CanvasGroup>();
            for (int i = 0; i < ids.Count; i++)
            {
                var nick = rng.Next(4) == 0 ? "unique" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) : pool[rng.Next(pool.Count)];
                if (i % 7 == 0)
                {
                    var members = ids.Where((_, k) => k < 100 && rng.Next(10) == 0).ToList();
                    groups.Add(TestCanvas.Group(ids[i], rng.Next(3) == 0 ? "" : nick, members));
                }
                else
                {
                    nodes.Add(TestCanvas.Node(ids[i], nick, isDefault: rng.Next(3) == 0));
                }
            }
            var infra = ids.Skip(110).Take(3).ToList();
            return TestCanvas.Snap(nodes, groups, infra);
        }

        // ---- display names: printable names (outline cuts a name out at the first separator) ----

        [Theory]
        [InlineData("Wall Height")]          // inner spaces are fine: the separators all carry more than a space
        [InlineData("f(x)")]                 // a parenthesis with no space before it
        [InlineData("a,b")]
        [InlineData("a;b")]
        [InlineData("a·b")]
        [InlineData("a←b")]
        [InlineData("x=y")]
        [InlineData("pts[0]")]               // an object name may hold ']' (only group names sit inside tags)
        [InlineData("a,")]                   // ',' then ', ' still cuts at the end of the name
        [InlineData("N+1")]
        [InlineData("COUNT")]
        public void Printable_AcceptsNamesThatCutCleanly(string name)
        {
            Assert.True(OutlineGrammar.IsPrintable(name, isGroup: false), name);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" lead")]
        [InlineData("trail ")]
        [InlineData("\u00a0nbsp")]
        [InlineData("two\nlines")]
        [InlineData("two\r\nlines")]
        [InlineData("tab\there")]
        [InlineData("line\u2028sep")]
        [InlineData("bell\u0007")]
        [InlineData("a · b")]
        [InlineData("a, b")]
        [InlineData("a; b")]
        [InlineData("a ← b")]
        [InlineData("a → b")]
        [InlineData("Width (m)")]
        [InlineData("a [b]")]
        [InlineData("a = b")]
        [InlineData("[A")]
        [InlineData("+3 more")]
        [InlineData("a ·")]                  // ' ·' then ' · ' would show the separator one character early
        [InlineData("a ←")]
        [InlineData("a →")]
        [InlineData("a =")]
        [InlineData("· a")]                  // ' · ' then '· a' reads the separator one character late
        [InlineData("← a")]
        public void Printable_RefusesNamesThatCannotBeCutOut(string name)
        {
            Assert.False(OutlineGrammar.IsPrintable(name, isGroup: false), name);
            Assert.False(OutlineGrammar.IsPrintable(name, isGroup: true), name);
        }

        [Fact]
        public void Printable_EverySeparatorAndLeadingMarkIsRefused()
        {
            foreach (var sep in OutlineGrammar.Separators)
                Assert.False(OutlineGrammar.IsPrintable("a" + sep + "b", isGroup: false), $"'{sep}'");
            foreach (var mark in OutlineGrammar.LeadingMarks)
                Assert.False(OutlineGrammar.IsPrintable(mark + "a", isGroup: false), $"'{mark}'");
        }

        [Fact]
        public void Printable_GroupNamesAlsoRefuseTheTagClose()
        {
            Assert.True(OutlineGrammar.IsPrintable("pts[0]", isGroup: false));
            Assert.False(OutlineGrammar.IsPrintable("pts[0]", isGroup: true));
            Assert.False(OutlineGrammar.IsPrintable("A]", isGroup: true));
        }

        [Fact]
        public void Display_AnUnprintableNickname_PrintsTheDisplayId_AndBothStillRoundTrip()
        {
            var g1 = TestCanvas.Id(0x20);
            var g2 = TestCanvas.Id(0x21);
            var g3 = TestCanvas.Id(0x22);
            var snap = TestCanvas.Snap(
                new[]
                {
                    TestCanvas.Node(A, "Wall Height", "Number Slider"),
                    TestCanvas.Node(B, "a, b"),
                    TestCanvas.Node(C, " COUNT"),
                    TestCanvas.Node(Posts, "two\nlines"),
                    TestCanvas.Node(Unnamed, "+1 more")
                },
                new[] { TestCanvas.Group(g1, "IN · OUT"), TestCanvas.Group(g2, "[x]"), TestCanvas.Group(g3, "tag]") });
            var names = new DisplayNames(snap);

            Assert.Equal("Wall Height", names.DisplayName(A));
            Assert.Equal("00000002", names.DisplayName(B));
            Assert.Equal("00000003", names.DisplayName(C));
            Assert.Equal("00000010", names.DisplayName(Posts));
            Assert.Equal("00000011", names.DisplayName(Unnamed));
            Assert.Equal("00000020", names.DisplayName(g1));
            Assert.Equal("00000021", names.DisplayName(g2));
            Assert.Equal("00000022", names.DisplayName(g3));
            // The nickname still resolves when passed in; it is only not printed.
            Assert.True(ObjectResolver.ResolvesTo(snap, "a, b", ResolveScope.Objects, B));
            AssertRoundTrips(snap, names);
        }

        // ---- many ids ----

        [Fact]
        public void Many_DuplicatesAreNoted_AndEveryEntryIsResolved()
        {
            var counting = new CountingIndex(Canvas());
            var texts = new[] { "COUNT", A.ToString(), "nope", "00000001", "abcdef01" };
            var results = ObjectResolver.ResolveMany(counting, texts, ResolveScope.Objects);

            Assert.Equal(texts.Length, results.Count);
            Assert.Equal(Enumerable.Range(0, texts.Length), results.Select(r => r.Index));
            Assert.Equal(texts, results.Select(r => r.Text));

            Assert.Null(results[0].DuplicateOf);
            Assert.Equal(0, results[1].DuplicateOf);
            Assert.Equal("same object as entry 0", results[1].Note);
            Assert.False(results[2].Resolution.IsResolved);
            Assert.Null(results[2].DuplicateOf);
            Assert.Equal(0, results[3].DuplicateOf);
            Assert.True(results[4].Resolution.IsResolved);
            Assert.Null(results[4].Note);
            Assert.All(results.Where(r => r.Resolution.IsResolved), r => Assert.Same(counting.Inner.Find(r.Resolution.Entry.Id), r.Resolution.Entry));
            Assert.True(counting.Calls > 0);
        }

        [Fact]
        public void Many_EmptyList_GivesNoResults()
        {
            Assert.Empty(ObjectResolver.ResolveMany(Canvas(), Array.Empty<string>(), ResolveScope.Objects));
        }

        // ---- index decorators (the shape a batch overlay takes) ----

        /// <summary>Adds one entry to every lookup without filtering infrastructure, as a careless overlay would.</summary>
        private sealed class LeakyIndex : IObjectIndex
        {
            private readonly IObjectIndex _inner;
            private readonly ObjectEntry _extra;

            public LeakyIndex(IObjectIndex inner, ObjectEntry extra)
            {
                _inner = inner;
                _extra = extra;
            }

            public ObjectEntry Find(Guid id) => id == _extra.Id ? _extra : _inner.Find(id);
            public IReadOnlyList<ObjectEntry> WithNickname(string n) => n == _extra.NickName ? _inner.WithNickname(n).Append(_extra).ToList() : _inner.WithNickname(n);
            public IReadOnlyList<ObjectEntry> WithGroupName(string n) => _inner.WithGroupName(n);
            public IReadOnlyList<ObjectEntry> WithPrefix(string p) =>
                _extra.Id.ToString("D").StartsWith(p.ToLowerInvariant(), StringComparison.Ordinal) ? _inner.WithPrefix(p).Append(_extra).ToList() : _inner.WithPrefix(p);
            public bool IsInfrastructure(Guid id) => _inner.IsInfrastructure(id);
            public IEnumerable<ObjectEntry> Entries => _inner.Entries.Append(_extra);
        }

        private sealed class CountingIndex : IObjectIndex
        {
            public CountingIndex(IObjectIndex inner) { Inner = inner; }
            public IObjectIndex Inner { get; }
            public int Calls { get; private set; }

            public ObjectEntry Find(Guid id) { Calls++; return Inner.Find(id); }
            public IReadOnlyList<ObjectEntry> WithNickname(string n) { Calls++; return Inner.WithNickname(n); }
            public IReadOnlyList<ObjectEntry> WithGroupName(string n) { Calls++; return Inner.WithGroupName(n); }
            public IReadOnlyList<ObjectEntry> WithPrefix(string p) { Calls++; return Inner.WithPrefix(p); }
            public bool IsInfrastructure(Guid id) => Inner.IsInfrastructure(id);
            public IEnumerable<ObjectEntry> Entries => Inner.Entries;
        }
    }
}
