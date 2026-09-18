using System.Globalization;
using System.Windows.Data;
using WeighBridge.App.Converters;
using Xunit;

namespace WeighBridge.Tests.App;

public sealed class BooleanToRadioButtonConverterTests
{
    private readonly BooleanToRadioButtonConverter _converter = new();

    [Theory]
    [InlineData(true, "True", true)]
    [InlineData(false, "True", false)]
    [InlineData(true, "False", false)]
    [InlineData(false, "False", true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    public void Convert_ReturnsExpectedIsChecked(bool sourceValue, object parameter, bool expected)
    {
        var result = _converter.Convert(sourceValue, typeof(bool), parameter, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Convert_NonBooleanSource_ReturnsFalse()
    {
        var result = _converter.Convert("not a bool", typeof(bool), "True", CultureInfo.InvariantCulture);
        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData(true, "True", true)]
    [InlineData(true, "False", false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void ConvertBack_WhenChecked_PushesTargetValue(bool isChecked, object parameter, bool expected)
    {
        var result = _converter.ConvertBack(isChecked, typeof(bool), parameter, CultureInfo.InvariantCulture);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false, "True")]
    [InlineData(false, "False")]
    [InlineData(false, null)]
    public void ConvertBack_WhenUnchecked_ReturnsBindingDoNothing(bool isChecked, object? parameter)
    {
        // CRITICAL: When a radio button is unchecked, it MUST return Binding.DoNothing
        // so WPF does not clobber the ViewModel property during group changes or tab switching!
        var result = _converter.ConvertBack(isChecked, typeof(bool), parameter, CultureInfo.InvariantCulture);
        Assert.Same(Binding.DoNothing, result);
    }

    [Fact]
    public void ConvertBack_NonBooleanValue_ReturnsBindingDoNothing()
    {
        var result = _converter.ConvertBack("not a bool", typeof(bool), "True", CultureInfo.InvariantCulture);
        Assert.Same(Binding.DoNothing, result);
    }
}
