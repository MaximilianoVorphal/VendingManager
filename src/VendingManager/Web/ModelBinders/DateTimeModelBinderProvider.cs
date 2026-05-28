using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VendingManager.Web.ModelBinders;

/// <summary>
/// Registers <see cref="DateTimeModelBinder"/> for all DateTime and DateTime? parameters.
/// Inserted at position 0 so it takes priority over the default SimpleTypeModelBinder.
/// </summary>
public sealed class DateTimeModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var modelType = context.Metadata.UnderlyingOrModelType;

        if (modelType == typeof(DateTime) || modelType == typeof(DateTime?))
        {
            return new DateTimeModelBinder();
        }

        return null;
    }
}
