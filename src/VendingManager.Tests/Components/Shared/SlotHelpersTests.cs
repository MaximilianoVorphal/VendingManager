namespace VendingManager.Tests.Components.Shared;

using Xunit;
using VendingManager.Web.Components.Shared;

// Local test helper structs with explicit properties
internal class TestSlotString { public string NumeroSlot { get; set; } public TestSlotString(string n) => NumeroSlot = n; }
internal class TestSlotInt { public int SlotSort { get; set; } public TestSlotInt(int s) => SlotSort = s; }

public class SlotHelpersTests
{
    // === ComputeShelfIndex(string) ===

    [Theory]
    [InlineData("1", 0)]
    [InlineData("10", 0)]
    [InlineData("11", 1)]
    [InlineData("20", 1)]
    [InlineData("21", 2)]
    [InlineData("30", 2)]
    [InlineData("31", 3)]
    [InlineData("100", 9)]
    public void ComputeShelfIndex_String_ReturnsCorrectShelf(string slotNumber, int expectedShelf)
    {
        var result = SlotHelpers.ComputeShelfIndex(slotNumber);
        Assert.Equal(expectedShelf, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void ComputeShelfIndex_String_InvalidInput_ReturnsZero(string slotNumber)
    {
        var result = SlotHelpers.ComputeShelfIndex(slotNumber);
        Assert.Equal(0, result);
    }

    // === ComputeShelfIndex(int) ===

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(30, 2)]
    [InlineData(31, 3)]
    [InlineData(100, 9)]
    public void ComputeShelfIndex_Int_ReturnsCorrectShelf(int slotNumber, int expectedShelf)
    {
        var result = SlotHelpers.ComputeShelfIndex(slotNumber);
        Assert.Equal(expectedShelf, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ComputeShelfIndex_Int_InvalidInput_ReturnsZero(int slotNumber)
    {
        var result = SlotHelpers.ComputeShelfIndex(slotNumber);
        Assert.Equal(0, result);
    }

    // === GroupByShelf with string selector ===

    [Fact]
    public void GroupByShelf_StringSelector_GroupsCorrectly()
    {
        var slots = new List<TestSlotString>
        {
            new("1"), new("5"), new("11"), new("15"), new("21")
        };

        var shelves = SlotHelpers.GroupByShelf(slots, s => s.NumeroSlot).ToList();

        Assert.Equal(3, shelves.Count);

        var shelf0 = shelves.First(g => g.Key == 0).ToList();
        Assert.Equal(2, shelf0.Count);
        Assert.All(shelf0, s => Assert.True(s.NumeroSlot is "1" or "5"));

        var shelf1 = shelves.First(g => g.Key == 1).ToList();
        Assert.Equal(2, shelf1.Count);
        Assert.All(shelf1, s => Assert.True(s.NumeroSlot is "11" or "15"));

        var shelf2 = shelves.First(g => g.Key == 2).ToList();
        Assert.Single(shelf2);
        Assert.Equal("21", shelf2[0].NumeroSlot);
    }

    [Fact]
    public void GroupByShelf_StringSelector_EmptyCollection_ReturnsEmpty()
    {
        var slots = new List<TestSlotString>();
        var shelves = SlotHelpers.GroupByShelf(slots, s => s.NumeroSlot).ToList();
        Assert.Empty(shelves);
    }

    [Fact]
    public void GroupByShelf_StringSelector_SingleSlot_ReturnsOneShelf()
    {
        var slots = new List<TestSlotString> { new("7") };
        var shelves = SlotHelpers.GroupByShelf(slots, s => s.NumeroSlot).ToList();
        Assert.Single(shelves);
        Assert.Equal(0, shelves[0].Key);
    }

    // === GroupByShelf with int selector ===

    [Fact]
    public void GroupByShelf_IntSelector_GroupsCorrectly()
    {
        var slots = new List<TestSlotInt>
        {
            new(1), new(9), new(12), new(19), new(22)
        };

        var shelves = SlotHelpers.GroupByShelf(slots, s => s.SlotSort).ToList();

        Assert.Equal(3, shelves.Count);
        Assert.Equal(2, shelves.First(g => g.Key == 0).Count());
        Assert.Equal(2, shelves.First(g => g.Key == 1).Count());
        Assert.Single(shelves.First(g => g.Key == 2));
    }
}