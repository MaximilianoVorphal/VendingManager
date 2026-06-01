using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;
using VendingManager.Web.ModelBinders;
using Xunit;

namespace VendingManager.Tests.Components;

public class DateTimeModelBinderTests
{
    private static readonly Type BinderType = typeof(DateTimeModelBinder);

    // ===== TryParseExact tests (private method via reflection) =====

    [Theory]
    [InlineData("2026-05-02", true, "2026-05-02")]
    [InlineData("2026-05-02T14:30:00", true, "2026-05-02T14:30:00")]
    [InlineData("2026-05-02T14:30", true, "2026-05-02T14:30")]
    public void TryParseExact_ValidISO_ParsesCorrectDateTime(string input, bool expectedSuccess, string expectedDate)
    {
        var (success, result) = InvokeTryParseExact(input);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
            Assert.Equal(DateTime.Parse(expectedDate), result);
    }

    [Theory]
    [InlineData("02/05/2026")] // Chilean dd/MM/yyyy — not ISO
    [InlineData("not-a-date")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseExact_NonISOOrInvalid_ReturnsFalse(string input)
    {
        var (success, _) = InvokeTryParseExact(input);
        Assert.False(success);
    }

    // ===== TryParseWithCurrentCulture tests (private method via reflection) =====

    [Fact]
    public void TryParseWithCurrentCulture_ChileanDate_ParsesCorrectDateTime()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-CL");
        try
        {
            var (success, result) = InvokeTryParseWithCurrentCulture("02/05/2026");
            Assert.True(success);
            Assert.Equal(new DateTime(2026, 5, 2), result);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public void TryParseWithCurrentCulture_ISODate_ParsesCorrectDateTime()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-CL");
        try
        {
            var (success, result) = InvokeTryParseWithCurrentCulture("2026-05-02");
            Assert.True(success);
            Assert.Equal(new DateTime(2026, 5, 2), result);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseWithCurrentCulture_InvalidOrEmpty_ReturnsFalse(string input)
    {
        var (success, _) = InvokeTryParseWithCurrentCulture(input);
        Assert.False(success);
    }

    // ===== BindModelAsync tests via Moq =====

    [Fact]
    public async Task BindModelAsync_ISOdateOnly_SetsCorrectDateTime()
    {
        await RunBindAndAssert("inicio", "2026-05-02", typeof(DateTime),
            result => Assert.Equal(new DateTime(2026, 5, 2), result));
    }

    [Fact]
    public async Task BindModelAsync_ISOdatetimeWithTime_SetsCorrectDateTime()
    {
        await RunBindAndAssert("fecha", "2026-05-02T14:30:00", typeof(DateTime),
            result => Assert.Equal(new DateTime(2026, 5, 2, 14, 30, 0), result));
    }

    [Fact]
    public async Task BindModelAsync_ChileanDate_SetsCorrectDateTime()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-CL");
        try
        {
            await RunBindAndAssert("inicio", "02/05/2026", typeof(DateTime),
                result => Assert.Equal(new DateTime(2026, 5, 2), result));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public async Task BindModelAsync_InvalidFormat_AddsModelStateError()
    {
        var modelState = new ModelStateDictionary();
        var binder = new DateTimeModelBinder();
        var ctx = CreateContext("fecha", "not-a-date", typeof(DateTime), modelState);

        await binder.BindModelAsync(ctx.Object);

        Assert.False(ctx.Object.Result.IsModelSet);
        Assert.NotEmpty(modelState);
        Assert.Contains("not-a-date", modelState["fecha"]!.Errors[0]!.ErrorMessage);
    }

    [Fact]
    public async Task BindModelAsync_NonNullableEmptyString_AddsModelStateError()
    {
        var modelState = new ModelStateDictionary();
        var binder = new DateTimeModelBinder();
        var ctx = CreateContext("inicio", "", typeof(DateTime), modelState);

        await binder.BindModelAsync(ctx.Object);

        Assert.False(ctx.Object.Result.IsModelSet);
        Assert.NotEmpty(modelState);
    }

    [Fact]
    public async Task BindModelAsync_ISOPriorityOverAmbiguousChilean()
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-CL");
        try
        {
            // "2026-01-02" → Jan 2 (ISO wins because checked first)
            await RunBindAndAssert("inicio", "2026-01-02", typeof(DateTime),
                result => Assert.Equal(new DateTime(2026, 1, 2), result));
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Fact]
    public async Task BindModelAsync_NullableDateTime_ValidISODate_ReturnsDateTime()
    {
        await RunBindAndAssertNullable("fecha", "2026-05-02", typeof(DateTime?),
            result => Assert.Equal(new DateTime(2026, 5, 2), result));
    }

    [Fact]
    public async Task BindModelAsync_NullableDateTime_EmptyValue_ReturnsNull()
    {
        var modelState = new ModelStateDictionary();
        var binder = new DateTimeModelBinder();
        var ctx = CreateContext("fecha", "", typeof(DateTime?), modelState);

        await binder.BindModelAsync(ctx.Object);

        Assert.True(ctx.Object.Result.IsModelSet);
        Assert.Null(ctx.Object.Result.Model);
    }

    [Fact]
    public async Task BindModelAsync_NullableDateTime_NoValue_ReturnsEarly()
    {
        var modelState = new ModelStateDictionary();
        var binder = new DateTimeModelBinder();
        var ctx = CreateContextNoValue("fecha", typeof(DateTime?), modelState);

        await binder.BindModelAsync(ctx.Object);

        Assert.False(ctx.Object.Result.IsModelSet);
    }

    // ===== Moq helpers =====

    private static Task RunBindAndAssert(
        string modelName, string rawValue, Type modelType, Action<object?> assert)
    {
        var modelState = new ModelStateDictionary();
        var binder = new DateTimeModelBinder();
        var ctx = CreateContext(modelName, rawValue, modelType, modelState);

        binder.BindModelAsync(ctx.Object).GetAwaiter().GetResult();

        Assert.True(ctx.Object.Result.IsModelSet);
        assert(ctx.Object.Result.Model);
        return Task.CompletedTask;
    }

    private static Task RunBindAndAssertNullable(
        string modelName, string rawValue, Type modelType, Action<object?> assert)
    {
        var modelState = new ModelStateDictionary();
        var binder = new DateTimeModelBinder();
        var ctx = CreateContext(modelName, rawValue, modelType, modelState);

        binder.BindModelAsync(ctx.Object).GetAwaiter().GetResult();

        Assert.True(ctx.Object.Result.IsModelSet);
        assert(ctx.Object.Result.Model);
        return Task.CompletedTask;
    }

    private static Mock<ModelBindingContext> CreateContext(
        string modelName, string? rawValue, Type modelType, ModelStateDictionary modelState)
    {
        var valueProvider = new Mock<IValueProvider>();
        valueProvider.Setup(v => v.GetValue(modelName))
            .Returns(new ValueProviderResult(rawValue));

        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType);

        var ctx = new Mock<ModelBindingContext>();
        ctx.SetupAllProperties();
        ctx.Setup(c => c.ModelName).Returns(modelName);
        ctx.Setup(c => c.ModelMetadata).Returns(metadata);
        ctx.Setup(c => c.ValueProvider).Returns(valueProvider.Object);
        ctx.Object.ModelState = modelState;
        ctx.Object.Result = ModelBindingResult.Failed();

        return ctx;
    }

    private static Mock<ModelBindingContext> CreateContextNoValue(
        string modelName, Type modelType, ModelStateDictionary modelState)
    {
        var valueProvider = new Mock<IValueProvider>();
        valueProvider.Setup(v => v.GetValue(modelName))
            .Returns(ValueProviderResult.None);

        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType);

        var ctx = new Mock<ModelBindingContext>();
        ctx.SetupAllProperties();
        ctx.Setup(c => c.ModelName).Returns(modelName);
        ctx.Setup(c => c.ModelMetadata).Returns(metadata);
        ctx.Setup(c => c.ValueProvider).Returns(valueProvider.Object);
        ctx.Object.ModelState = modelState;
        ctx.Object.Result = ModelBindingResult.Failed();

        return ctx;
    }

    // ===== Reflection helpers =====

    private static (bool Success, DateTime? Result) InvokeTryParseExact(string? value)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm" };
        var method = BinderType.GetMethod("TryParseExact", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TryParseExact not found");

        DateTime? outResult = null;
        var parameters = new object[] { value!, formats, outResult! };
        var success = (bool)method.Invoke(null, parameters)!;
        outResult = (DateTime?)parameters[2];
        return (success, outResult);
    }

    private static (bool Success, DateTime? Result) InvokeTryParseWithCurrentCulture(string? value)
    {
        var method = BinderType.GetMethod("TryParseWithCurrentCulture", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TryParseWithCurrentCulture not found");

        DateTime? outResult = null;
        var parameters = new object[] { value!, outResult! };
        var success = (bool)method.Invoke(null, parameters)!;
        outResult = (DateTime?)parameters[1];
        return (success, outResult);
    }
}
