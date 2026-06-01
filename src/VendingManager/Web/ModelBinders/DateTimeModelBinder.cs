using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VendingManager.Web.ModelBinders;

/// <summary>
/// Model binder for DateTime that accepts both ISO 8601 (yyyy-MM-dd) and
/// current-culture formats (dd/MM/yyyy for Chilean culture).
///
/// This is needed because the Chilean culture forces dd/MM/yyyy via
/// DefaultThreadCurrentCulture, but the Blazor client sends ISO dates.
/// </summary>
public sealed class DateTimeModelBinder : IModelBinder
{
    private static readonly string[] IsoFormats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"];
    private static readonly string[] IsoNullableFormats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm", ""];

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        var rawValue = valueProviderResult.FirstValue;

        var isNullable = bindingContext.ModelMetadata.IsNullableValueType;
        var formats = isNullable ? IsoNullableFormats : IsoFormats;
        var targetType = isNullable ? typeof(DateTime?) : typeof(DateTime);

        // 1. Attempt strict ISO 8601 parsing (what the Blazor client sends)
        if (TryParseExact(rawValue, formats, out var isoDate) && isoDate.HasValue)
        {
            bindingContext.Result = ModelBindingResult.Success(
                isNullable ? isoDate : isoDate!.Value);
            return Task.CompletedTask;
        }

        // 2. Attempt current-culture parsing (Chilean dd/MM/yyyy)
        if (TryParseWithCurrentCulture(rawValue, out var cultureDate) && cultureDate.HasValue)
        {
            bindingContext.Result = ModelBindingResult.Success(
                isNullable ? cultureDate : cultureDate!.Value);
            return Task.CompletedTask;
        }

        // 3. Nullable with empty/missing value → null
        if (isNullable && string.IsNullOrWhiteSpace(rawValue))
        {
            bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.AddModelError(
            bindingContext.ModelName,
            $"The value '{rawValue}' is not a valid date. Use yyyy-MM-dd or dd/MM/yyyy.");
        return Task.CompletedTask;
    }

    private static bool TryParseExact(string? value, string[] formats, out DateTime? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            result = date;
            return true;
        }

        return false;
    }

    private static bool TryParseWithCurrentCulture(string? value, out DateTime? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
        {
            result = date;
            return true;
        }

        return false;
    }
}
