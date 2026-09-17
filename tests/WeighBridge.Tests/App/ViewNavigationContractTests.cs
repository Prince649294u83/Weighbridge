using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace WeighBridge.Tests.App;

public class ViewNavigationContractTests
{
    private static readonly string IconsPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Resources\Icons.xaml"));

    private static readonly string TypographyPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Styles\Typography.xaml"));

    private static readonly string ViewsDirectory =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Views"));

    [Fact]
    public void AppResources_ContainRequiredIconsAndTypography()
    {
        Assert.True(File.Exists(IconsPath), $"Icons.xaml missing at {IconsPath}");
        Assert.True(File.Exists(TypographyPath), $"Typography.xaml missing at {TypographyPath}");

        var iconsContent = File.ReadAllText(IconsPath);
        var typoContent = File.ReadAllText(TypographyPath);

        // Assert critical icons that were missing
        Assert.Contains("Icon.Print", iconsContent);
        Assert.Contains("Icon.Document", iconsContent);
        Assert.Contains("Icon.Email", iconsContent);
        Assert.Contains("Icon.Search", iconsContent);
        Assert.Contains("Icon.Save", iconsContent);
        Assert.Contains("Icon.Refresh", iconsContent);

        // Assert critical typography styles
        Assert.Contains("Text.H2", typoContent);
        Assert.Contains("Text.H3", typoContent);
        Assert.Contains("Text.Icon", typoContent);
    }

    [Fact]
    public void AllViews_UseProperTextIconStyle_AndNotUnstyledContentControl()
    {
        var xamlFiles = Directory.GetFiles(ViewsDirectory, "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(xamlFiles);

        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            // No view should embed an unstyled ContentControl with Icon static resource
            Assert.DoesNotContain("<ContentControl Content=\"{StaticResource Icon.", content);
        }
    }
}
