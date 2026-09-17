using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Services.Messaging;
using Xunit;

namespace WeighBridge.Tests.Messaging;

public sealed class SmtpEmailServiceTests
{
    [Fact]
    public async Task SendEmailAsync_WhenDisabled_ReturnsFalseImmediately()
    {
        var options = new EmailOptions
        {
            Enabled = false,
            SmtpServer = "smtp.example.com",
            SenderId = "test@example.com"
        };

        var monitor = new TestOptionsMonitor<EmailOptions>(options);
        var service = new SmtpEmailService(monitor, NullLogger<SmtpEmailService>.Instance);

        var result = await service.SendEmailAsync("Test Subject", "Body content", ["recipient@example.com"]);

        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WhenNoRecipients_ReturnsFalse()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            SmtpServer = "smtp.example.com",
            SenderId = "test@example.com"
        };

        var monitor = new TestOptionsMonitor<EmailOptions>(options);
        var service = new SmtpEmailService(monitor, NullLogger<SmtpEmailService>.Instance);

        var result = await service.SendEmailAsync("Test Subject", "Body content", []);

        Assert.False(result);
    }

    [Fact]
    public async Task SendEmailAsync_WhenSmtpServerEmpty_ReturnsFalse()
    {
        var options = new EmailOptions
        {
            Enabled = true,
            SmtpServer = string.Empty,
            SenderId = "test@example.com"
        };

        var monitor = new TestOptionsMonitor<EmailOptions>(options);
        var service = new SmtpEmailService(monitor, NullLogger<SmtpEmailService>.Instance);

        var result = await service.SendEmailAsync("Test Subject", "Body content", ["recipient@example.com"]);

        Assert.False(result);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenSmtpServerEmpty_ReturnsFalse()
    {
        var options = new EmailOptions
        {
            SmtpServer = string.Empty
        };

        var monitor = new TestOptionsMonitor<EmailOptions>(options);
        var service = new SmtpEmailService(monitor, NullLogger<SmtpEmailService>.Instance);

        var result = await service.TestConnectionAsync();

        Assert.False(result);
    }

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
