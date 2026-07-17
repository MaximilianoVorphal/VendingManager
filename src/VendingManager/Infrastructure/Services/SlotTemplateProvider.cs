using VendingManager.Core.Entities;

namespace VendingManager.Infrastructure.Services;

public static class SlotTemplateProvider
{
    public static List<string> GetSlotNumbers(MaquinaTipo tipo)
    {
        return tipo switch
        {
            MaquinaTipo.Snack => GetSnackSlots(),
            MaquinaTipo.CafeSnack => GetCafeSnackSlots(),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
        };
    }

    private static List<string> GetSnackSlots()
    {
        var odd1To9 = Enumerable.Range(1, 9).Where(n => n % 2 == 1).Select(n => n.ToString());
        var sequential10To60 = Enumerable.Range(10, 51).Select(n => n.ToString());
        return odd1To9.Concat(sequential10To60).ToList();
    }

    private static List<string> GetCafeSnackSlots()
    {
        var coffee1To14 = Enumerable.Range(1, 14).Select(n => n.ToString());
        var odd101To159 = Enumerable.Range(101, 59).Where(n => n % 2 == 1).Select(n => n.ToString());
        return coffee1To14.Concat(odd101To159).ToList();
    }
}
