using VendingManager.Core.Entities;
using VendingManager.Infrastructure.Services;

namespace VendingManager.Tests.Services;

public class SlotTemplateProviderTests
{
    [Fact]
    public void GetSlotNumbers_Snack_Returns56Slots()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.Snack);

        // Assert
        slots.Should().NotBeNull();
        slots.Should().HaveCount(56);
    }

    [Fact]
    public void GetSlotNumbers_Snack_FirstFiveAreOdd1to9()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.Snack);

        // Assert
        slots.Take(5).Should().Equal("1", "3", "5", "7", "9");
    }

    [Fact]
    public void GetSlotNumbers_Snack_RestIsSequential10to60()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.Snack);

        // Assert
        slots.Skip(5).Should().Equal(
            Enumerable.Range(10, 51).Select(n => n.ToString()));
    }

    [Fact]
    public void GetSlotNumbers_Snack_LastSlotIs60()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.Snack);

        // Assert
        slots.Last().Should().Be("60");
    }

    [Fact]
    public void GetSlotNumbers_CafeSnack_Returns44Slots()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.CafeSnack);

        // Assert
        slots.Should().NotBeNull();
        slots.Should().HaveCount(44);
    }

    [Fact]
    public void GetSlotNumbers_CafeSnack_First14AreSequential1to14()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.CafeSnack);

        // Assert
        slots.Take(14).Should().Equal(
            Enumerable.Range(1, 14).Select(n => n.ToString()));
    }

    [Fact]
    public void GetSlotNumbers_CafeSnack_Remaining30AreOdd101to159()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.CafeSnack);

        // Assert
        var oddRange = Enumerable.Range(101, 59)
            .Where(n => n % 2 == 1)
            .Select(n => n.ToString());
        slots.Skip(14).Should().Equal(oddRange);
    }

    [Fact]
    public void GetSlotNumbers_CafeSnack_LastSlotIs159()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.CafeSnack);

        // Assert
        slots.Last().Should().Be("159");
    }

    [Fact]
    public void GetSlotNumbers_CafeSnack_FirstSlotIs1()
    {
        // Act
        var slots = SlotTemplateProvider.GetSlotNumbers(MaquinaTipo.CafeSnack);

        // Assert
        slots.First().Should().Be("1");
    }
}
