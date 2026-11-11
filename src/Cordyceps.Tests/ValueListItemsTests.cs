using System.Linq;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Contract tests for <see cref="ValueListItems"/>: every value-list item is checked before the
    /// list is touched, so one bad item refuses the call instead of leaving the list emptied by a
    /// clear that the failing add never refilled; and the selection survives a replace by name.
    /// </summary>
    public class ValueListItemsTests
    {
        [Fact]
        public void TryParse_ValidArray_ReturnsItemsInOrder()
        {
            Assert.True(ValueListItems.TryParse("[{\"name\":\"A\",\"value\":\"1\"},{\"name\":\"B\",\"value\":\"2\"}]", out var items, out var error));
            Assert.Null(error);
            Assert.Equal(new[] { "A", "B" }, items.Select(i => i.Name));
            Assert.Equal(new[] { "1", "2" }, items.Select(i => i.Expression));
        }

        [Fact]
        public void TryParse_MissingFields_TakeTheHistoricalDefaults()
        {
            Assert.True(ValueListItems.TryParse("[{\"value\":\"5\"},{\"name\":\"B\"},{}]", out var items, out _));
            Assert.Equal(new[] { ValueListItems.DefaultName, "B", ValueListItems.DefaultName }, items.Select(i => i.Name));
            Assert.Equal(new[] { "5", ValueListItems.DefaultExpression, ValueListItems.DefaultExpression }, items.Select(i => i.Expression));
        }

        [Fact]
        public void TryParse_NumbersAndBooleans_AreReadAsInvariantText()
        {
            Assert.True(ValueListItems.TryParse("[{\"name\":1,\"value\":2.5},{\"name\":\"T\",\"value\":true}]", out var items, out _));
            Assert.Equal("1", items[0].Name);
            Assert.Equal("2.5", items[0].Expression);
            Assert.Equal("True", items[1].Expression);
        }

        [Fact]
        public void TryParse_DateShapedText_StaysExactlyAsWritten()
        {
            Assert.True(ValueListItems.TryParse("[{\"name\":\"2026-01-01\",\"value\":\"\\\"2026-01-01T00:00:00\\\"\"}]", out var items, out _));
            Assert.Equal("2026-01-01", items[0].Name);
            Assert.Equal("\"2026-01-01T00:00:00\"", items[0].Expression);
        }

        // Each input is one bad item placed AFTER a good one, so a parser that applied items as it
        // read them would already have accepted item 0 before reaching the failure.
        [Theory]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},null]", "item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},\"B\"]", "item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},[1]]", "item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},{\"name\":null}]", "Item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},{\"value\":null}]", "Item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},{\"name\":{\"x\":1}}]", "Item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},{\"value\":[1,2]}]", "Item 1")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},{\"name\":\"B\",\"expression\":\"2\"}]", "unknown key 'expression'")]
        [InlineData("[{\"name\":\"A\",\"value\":\"1\"},{\"Name\":\"B\"}]", "unknown key 'Name'")]
        public void TryParse_OneBadItemAnywhere_RefusesTheWholeList(string json, string expectedInError)
        {
            Assert.False(ValueListItems.TryParse(json, out var items, out var error));
            Assert.Null(items);
            Assert.Contains(expectedInError, error);
        }

        [Fact]
        public void TryParse_DuplicateKey_IsRefused()
        {
            Assert.False(ValueListItems.TryParse("[{\"name\":\"A\",\"name\":\"B\"}]", out var items, out var error));
            Assert.Null(items);
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("{\"name\":\"A\"}")]
        [InlineData("null")]
        [InlineData("not json")]
        [InlineData("[{\"name\":\"A\"}] trailing")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void TryParse_NotANonEmptyArray_IsRefused(string json)
        {
            Assert.False(ValueListItems.TryParse(json, out var items, out var error));
            Assert.Null(items);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void Selection_SingleMode_KeepsThePreviousItemByName()
        {
            var selected = ValueListItems.SelectionAfterReplace(new[] { "B" }, new[] { "A", "B", "C" }, multiSelect: false);
            Assert.Equal(new[] { 1 }, selected);
        }

        [Fact]
        public void Selection_SingleMode_PreviousItemGone_SelectsTheFirst()
        {
            var selected = ValueListItems.SelectionAfterReplace(new[] { "Z" }, new[] { "A", "B" }, multiSelect: false);
            Assert.Equal(new[] { 0 }, selected);
        }

        [Fact]
        public void Selection_NothingSelectedBefore_SelectsTheFirst()
        {
            Assert.Equal(new[] { 0 }, ValueListItems.SelectionAfterReplace(new string[0], new[] { "A", "B" }, multiSelect: false));
            Assert.Equal(new[] { 0 }, ValueListItems.SelectionAfterReplace(null, new[] { "A", "B" }, multiSelect: true));
        }

        [Fact]
        public void Selection_MatchesNamesExactly()
        {
            var selected = ValueListItems.SelectionAfterReplace(new[] { "b" }, new[] { "A", "B" }, multiSelect: false);
            Assert.Equal(new[] { 0 }, selected);
        }

        [Fact]
        public void Selection_SingleMode_DuplicateNames_SelectsOnlyTheFirstMatch()
        {
            var selected = ValueListItems.SelectionAfterReplace(new[] { "B" }, new[] { "A", "B", "B" }, multiSelect: false);
            Assert.Equal(new[] { 1 }, selected);
        }

        [Fact]
        public void Selection_SingleMode_UsesTheFirstPreviouslySelectedItem()
        {
            // A single-selection list shows its first selected item, so that is the one kept.
            var selected = ValueListItems.SelectionAfterReplace(new[] { "C", "A" }, new[] { "A", "B", "C" }, multiSelect: false);
            Assert.Equal(new[] { 2 }, selected);
        }

        [Fact]
        public void Selection_CheckList_KeepsEverySurvivingItem()
        {
            var selected = ValueListItems.SelectionAfterReplace(new[] { "A", "C", "Gone" }, new[] { "A", "B", "C" }, multiSelect: true);
            Assert.Equal(new[] { 0, 2 }, selected);
        }

        [Fact]
        public void Selection_CheckList_NoneSurvive_SelectsTheFirst()
        {
            var selected = ValueListItems.SelectionAfterReplace(new[] { "Gone" }, new[] { "A", "B" }, multiSelect: true);
            Assert.Equal(new[] { 0 }, selected);
        }

        [Fact]
        public void Selection_NoNewItems_IsEmpty()
        {
            Assert.Empty(ValueListItems.SelectionAfterReplace(new[] { "A" }, new string[0], multiSelect: false));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reported_IsTheFirstSelectedItem(bool checkList)
        {
            Assert.Equal(1, ValueListItems.ReportedIndex(new[] { false, true, true }, checkList));
        }

        [Fact]
        public void Reported_DropdownWithNothingSelected_IsItemZero()
        {
            // A dropdown, sequence or cycle with nothing selected outputs item 0, so that is its value.
            Assert.Equal(0, ValueListItems.ReportedIndex(new[] { false, false }, checkList: false));
        }

        [Fact]
        public void Reported_CheckListWithNothingChecked_IsNone()
        {
            Assert.Equal(-1, ValueListItems.ReportedIndex(new[] { false, false }, checkList: true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reported_EmptyList_IsNone(bool checkList)
        {
            Assert.Equal(-1, ValueListItems.ReportedIndex(new bool[0], checkList));
        }
    }
}
