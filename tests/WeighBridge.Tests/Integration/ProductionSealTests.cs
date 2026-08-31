using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Messaging;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Messaging;
using WeighBridge.Domain.Security;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Persistence.Auditing;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Services.Messaging;
using WeighBridge.Services.Security;
using WeighBridge.Services.Weighments;
using WeighBridge.Settings.Configuration;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Integration;

public sealed class ProductionSealTests
{
    private sealed class MockSmsProvider(string name) : ISmsProvider
    {
        public string Name => name;
        public List<(string Recipient, string Message)> SentMessages { get; } = [];

        public Task<SmsSendResult> SendAsync(string recipient, string message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((recipient, message));
            return Task.FromResult(SmsSendResult.Success("DELIVERED"));
        }
    }

    private static DbContextOptions<WeighBridgeDbContext> CreateMigratedDb(TempDataRoot root)
    {
        root.Paths.EnsureCreated();
        var dbPath = Path.Combine(root.Paths.DatabaseDirectory, "weighbridge.db");
        var options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var context = new WeighBridgeDbContext(options);
        context.Database.Migrate();
        return options;
    }

    [Fact]
    public async Task FullLifecycle_Weighment_To_Print_Sms_Audit_Succeeds_PostCommit()
    {
        using var root = new TempDataRoot();
        var dbOptions = CreateMigratedDb(root);
        var signedIn = new SignedInOperator { UserName = "operator1" };

        Func<IUnitOfWork> uowFactory = () => new UnitOfWork(new WeighBridgeDbContext(dbOptions), signedIn);

        // 1. Setup SMS Subsystem
        var smsOptions = Options.Create(new SmsOptions
        {
            Enabled = true,
            DefaultRecipient = "+919876543210",
            Provider = SmsProviderType.GsmModem
        });
        var smsProvider = new MockSmsProvider("GsmModem");
        var templateEngine = new SmsTemplateEngine();
        var recipientResolver = new SmsRecipientResolver(smsOptions);
        var smsProcessor = new SmsOutboxProcessor(
            uowFactory,
            templateEngine,
            recipientResolver,
            [smsProvider],
            smsOptions,
            NullLogger<SmsOutboxProcessor>.Instance);

        // 2. Setup Audit Store
        var auditStore = new DatabaseAuditStore(uowFactory, NullLogger<DatabaseAuditStore>.Instance);

        // 3. Execute Weighment Lifecycle
        await using var uow = uowFactory();
        var weighmentRepo = uow.Repository<Weighment>();

        var weighment = Weighment.Open("MH12XY9999", WeighmentMode.GrossFirst, "JSW Steel Ltd", "Coal", charges: 250m);
        await weighmentRepo.AddAsync(weighment);
        await uow.SaveChangesAsync();

        weighment.AssignSlipNumber();
        weighment.RecordFirstWeight(new WeightCapture(45000m, DateTime.UtcNow, WeightSource.Indicator));
        await uow.SaveChangesAsync();

        weighment.RecordSecondWeight(new WeightCapture(15000m, DateTime.UtcNow, WeightSource.Indicator));
        await uow.SaveChangesAsync();

        // 4. Invariant Assertion: Weighment is Completed in Database
        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
        Assert.Equal(30000m, weighment.NetWeightKg);

        // 5. Post-Commit Operations: Print Data, SMS Outbox, Audit Record
        var printData = WeighmentPrintDataFactory.Create(weighment, new CompanyOptions { CompanyName = "Metro Weigh" });
        Assert.NotNull(printData);
        Assert.Equal(weighment.SlipNumber, printData.SlipNumber);

        // Enqueue SMS
        var messageKey = await smsProcessor.QueueWeighmentSmsAsync(printData);
        Assert.NotNull(messageKey);

        // Process Outbox
        int processedCount = await smsProcessor.ProcessOutboxAsync();
        Assert.Equal(1, processedCount);
        Assert.Single(smsProvider.SentMessages);
        Assert.Contains(weighment.SlipNumber!, smsProvider.SentMessages[0].Message);

        // Write Audit Entry
        var auditRecord = new AuditRecord(
            DateTime.UtcNow,
            signedIn.UserName,
            "Weighments",
            AuditActions.WeighmentCompleted,
            AuditOutcomes.Success,
            "Weighment",
            weighment.SlipNumber,
            $"Gross={weighment.Gross?.Kilograms}, Tare={weighment.Tare?.Kilograms}, Net={weighment.NetWeightKg}",
            Guid.NewGuid().ToString("N"));
        await auditStore.WriteAsync(auditRecord);

        // 6. Verify Audit Table Persistence
        await using var verifyContext = new WeighBridgeDbContext(dbOptions);
        var auditEntries = await verifyContext.Set<AuditEntry>().ToListAsync();
        Assert.Contains(auditEntries, e => e.Action == AuditActions.WeighmentCompleted && e.EntityId == weighment.SlipNumber);
    }

    [Fact]
    public async Task CrossProcessCrashRecovery_RecoversStaleSms_AndDeliversSuccessfully()
    {
        using var root = new TempDataRoot();
        var dbOptions = CreateMigratedDb(root);
        var signedIn = new SignedInOperator { UserName = "operator1" };
        Func<IUnitOfWork> uowFactory = () => new UnitOfWork(new WeighBridgeDbContext(dbOptions), signedIn);

        // Process A: Create message and leave in Sending state (simulating sudden power loss / crash)
        await using (var uow = uowFactory())
        {
            var msg = SmsOutboxMessage.Create("WB-CRASH-SEAL:Completion", 99, "+919876543210", "Crash Test Content");
            msg.MarkSending();
            // Stale sending timestamp (10 minutes ago)
            var sendingProp = typeof(SmsOutboxMessage).GetProperty("SendingSinceUtc");
            sendingProp?.SetValue(msg, DateTime.UtcNow.AddMinutes(-10));
            await uow.Repository<SmsOutboxMessage>().AddAsync(msg);
            await uow.SaveChangesAsync();
        }

        // Process B: Starts up, initializes SMS processor and processes outbox
        var smsOptions = Options.Create(new SmsOptions
        {
            Enabled = true,
            DefaultRecipient = "+919876543210",
            Provider = SmsProviderType.GsmModem
        });
        var smsProvider = new MockSmsProvider("GsmModem");
        var templateEngine = new SmsTemplateEngine();
        var recipientResolver = new SmsRecipientResolver(smsOptions);
        var smsProcessor = new SmsOutboxProcessor(
            uowFactory,
            templateEngine,
            recipientResolver,
            [smsProvider],
            smsOptions,
            NullLogger<SmsOutboxProcessor>.Instance);

        int processed = await smsProcessor.ProcessOutboxAsync();

        // Assert: Stale crashed message was recovered and delivered
        Assert.Equal(1, processed);
        Assert.Single(smsProvider.SentMessages);
        Assert.Equal("Crash Test Content", smsProvider.SentMessages[0].Message);

        await using (var verifyContext = new WeighBridgeDbContext(dbOptions))
        {
            var delivered = await verifyContext.Set<SmsOutboxMessage>().SingleAsync(x => x.MessageKey == "WB-CRASH-SEAL:Completion");
            Assert.Equal(SmsOutboxStatus.Sent, delivered.Status);
            Assert.NotNull(delivered.SentAtUtc);
        }
    }
}
