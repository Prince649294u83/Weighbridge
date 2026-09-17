using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Configuration;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.Services;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Settings;

public sealed class SettingsSlice1FoundationTests
{
    [Fact]
    public void SettingsWorkingCopy_IsIndependentFromLiveOptions_UntilSave()
    {
        var printerOptions = new PrinterOptions { CopyCount = 2, PaperSize = "A4" };
        var weighmentOptions = new WeighmentOptions { MinimumCharges = 0m };
        var vm = CreateViewModel(printerOptions: printerOptions, weighmentOptions: weighmentOptions);

        vm.CopyCount = 5;
        vm.PaperSize = "Half A4 / A5";
        vm.MinimumCharges = 250m;

        Assert.Equal(2, printerOptions.CopyCount);
        Assert.Equal("A4", printerOptions.PaperSize);
        Assert.Equal(0m, weighmentOptions.MinimumCharges);
    }

    [Fact]
    public void Settings_Discard_DoesNotPersist_AndRestoresWorkingValues()
    {
        var printerOptions = new PrinterOptions { CopyCount = 2, PaperSize = "A4" };
        var vm = CreateViewModel(printerOptions: printerOptions);

        vm.CopyCount = 6;
        vm.PaperSize = "Half A4 / A5";
        vm.DiscardConfigurationCommand.Execute(null);

        Assert.Equal(2, vm.CopyCount);
        Assert.Equal("A4", vm.PaperSize);
        Assert.Equal(2, printerOptions.CopyCount);
    }

    [Fact]
    public async Task Settings_Save_PersistsEmailSmsRecipientsAndStopBits()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, "{}");
        File.WriteAllText(data.Paths.UserPreferencesFile, "{}");
        var configuration = BuildConfiguration(data);
        var vm = CreateViewModel(data: data, configuration: configuration);

        vm.EmailEnabled = true;
        vm.IsEmailBothEntry = true;
        vm.EmailPdf = false;
        vm.EmailSenderName = "Terminal Mail";
        vm.EmailSenderId = "terminal@example.com";
        vm.EmailPassword = "new-secret";
        vm.EmailSmtpServer = "smtp.example.com";
        vm.NewRecipientEmail = "yard@example.com";
        vm.AddRecipientCommand.Execute(null);
        vm.IsSmsWhatsAppApi = true;
        vm.IsSmsFinalEntry = true;
        vm.SmsNumbers = "+919876543210";
        vm.WhatsAppToken = "token-123";
        vm.StopBits = "Two";

        await ((WeighBridge.Core.Mvvm.AsyncRelayCommand)vm.SaveConfigurationCommand).ExecuteAsync();

        var root = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!;
        Assert.True(root["Email"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal("Email Both Entry", root["Email"]!["Frequency"]!.GetValue<string>());
        Assert.False(root["Email"]!["PdfEnabled"]!.GetValue<bool>());
        Assert.Equal("Terminal Mail", root["Email"]!["SenderName"]!.GetValue<string>());
        Assert.Equal("terminal@example.com", root["Email"]!["SenderEmail"]!.GetValue<string>());
        Assert.StartsWith(SecretProtector.Prefix, root["Email"]!["Password"]!.GetValue<string>());
        Assert.Equal("smtp.example.com", root["Email"]!["SmtpServer"]!.GetValue<string>());
        Assert.Equal("yard@example.com", root["Email"]!["Recipients"]![0]!.GetValue<string>());
        Assert.True(root["Sms"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal("HttpGateway", root["Sms"]!["Provider"]!.GetValue<string>());
        Assert.Equal("SMS only Final Entry", root["Sms"]!["MessageFrequency"]!.GetValue<string>());
        Assert.Equal("+919876543210", root["Sms"]!["DefaultRecipient"]!.GetValue<string>());
        Assert.Equal("token-123", root["Sms"]!["WhatsAppToken"]!.GetValue<string>());
        Assert.Equal("Two", root["Hardware"]!["WeightIndicator"]!["StopBits"]!.GetValue<string>());

        configuration.Reload();
        var restarted = CreateViewModel(data: data, configuration: configuration);
        Assert.True(restarted.EmailEnabled);
        Assert.Equal("Email Both Entry", restarted.EmailFrequency);
        Assert.False(restarted.EmailPdf);
        Assert.Equal("terminal@example.com", restarted.EmailSenderId);
        Assert.Equal(string.Empty, restarted.EmailPassword);
        Assert.Contains("yard@example.com", restarted.RecipientEmails);
        Assert.Equal("WhatsApp API", restarted.SmsService);
        Assert.Equal("SMS only Final Entry", restarted.SmsFrequency);
        Assert.Equal("Two", restarted.StopBits);
    }

    [Fact]
    public void Settings_RecipientAddRejectsInvalidAndDuplicate_AndDeleteUsesSelection()
    {
        var vm = CreateViewModel();

        vm.NewRecipientEmail = "not-an-email";
        vm.AddRecipientCommand.Execute(null);
        Assert.Empty(vm.RecipientEmails);

        vm.NewRecipientEmail = "yard@example.com";
        vm.AddRecipientCommand.Execute(null);
        vm.NewRecipientEmail = "YARD@example.com";
        vm.AddRecipientCommand.Execute(null);
        Assert.Single(vm.RecipientEmails);

        vm.SelectedRecipientEmail = "yard@example.com";
        vm.DeleteRecipientCommand.Execute(vm.SelectedRecipientEmail);
        Assert.Empty(vm.RecipientEmails);
    }

    [Fact]
    public async Task Settings_InvalidConfiguration_DoesNotOverwritePreviousConfiguration()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, """{ "Printer": { "CopyCount": 2 } }""");
        File.WriteAllText(data.Paths.UserPreferencesFile, "{}");
        var before = File.ReadAllText(data.Paths.ConfigurationFile);
        var vm = CreateViewModel(data: data, configuration: BuildConfiguration(data));

        vm.CopyCount = 0;
        await ((WeighBridge.Core.Mvvm.AsyncRelayCommand)vm.SaveConfigurationCommand).ExecuteAsync();

        Assert.Equal(before, File.ReadAllText(data.Paths.ConfigurationFile));
    }

    private static IConfigurationRoot BuildConfiguration(TempDataRoot data)
        => new ConfigurationBuilder()
            .AddJsonFile(data.Paths.ConfigurationFile, optional: false, reloadOnChange: false)
            .Build();

    private static SettingsViewModel CreateViewModel(
        TempDataRoot? data = null,
        IConfiguration? configuration = null,
        PrinterOptions? printerOptions = null,
        WeighmentOptions? weighmentOptions = null)
    {
        var ownedData = data is null ? new TempDataRoot() : null;
        data ??= ownedData!;
        data.Paths.EnsureCreated();
        if (!File.Exists(data.Paths.ConfigurationFile))
        {
            File.WriteAllText(data.Paths.ConfigurationFile, "{}");
        }
        if (!File.Exists(data.Paths.UserPreferencesFile))
        {
            File.WriteAllText(data.Paths.UserPreferencesFile, "{}");
        }

        return new SettingsViewModel(
            new SettingsService(data.Paths, NullLogger<SettingsService>.Instance),
            new StubThemeService(),
            new StubDialogService(),
            new JsonConfigurationWriter(data.Paths, NullLogger<JsonConfigurationWriter>.Instance, configuration),
            new StubPortScanner(),
            new StubWeightIndicator(),
            new StubPermissions(),
            Options.Create(new HardwareOptions()),
            Options.Create(printerOptions ?? new PrinterOptions()),
            Options.Create(new ReportingOptions()),
            Options.Create(new DatabaseOptions()),
            Options.Create(weighmentOptions ?? new WeighmentOptions()),
            NullLogger<SettingsViewModel>.Instance,
            Options.Create(new CompanyOptions()),
            Options.Create(new SmsOptions()),
            configuration);
    }
}
