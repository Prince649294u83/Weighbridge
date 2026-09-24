using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Messaging;
using WeighBridge.Core.Printing;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Messaging;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Messaging;
using Xunit;

namespace WeighBridge.Tests.Messaging;

public sealed class SmsOutboxTests
{
    private sealed class InMemoryRepository<T> : IRepository<T> where T : EntityBase, IAggregateRoot
    {
        public List<T> Items { get; } = [];
        private long _nextId = 1;

        public Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.ToList());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).ToList());
        }

        public Task<IReadOnlyList<T>> ListRecentAsync(Expression<Func<T, bool>>? predicate = null, int? take = null, CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            query = query.OrderByDescending(x => x.Id);
            if (take.HasValue) query = query.Take(take.Value);
            return Task.FromResult<IReadOnlyList<T>>(query.ToList());
        }

        public Task<IReadOnlyList<T>> QueryAsync(
            Expression<Func<T, bool>>? predicate,
            Func<IQueryable<T>, IQueryable<T>>? transform,
            CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            if (transform != null) query = transform(query);
            return Task.FromResult<IReadOnlyList<T>>(query.ToList());
        }

        public Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        {
            int count = predicate == null ? Items.Count : Items.AsQueryable().Count(predicate);
            return Task.FromResult(count);
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            var idProp = typeof(EntityBase).GetProperty("Id");
            idProp?.SetValue(entity, _nextId++);
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity) { Items.Remove(entity); }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly InMemoryRepository<SmsOutboxMessage> _messages;

        public TestUnitOfWork(InMemoryRepository<SmsOutboxMessage> messages)
        {
            _messages = messages;
        }

        public IRepository<TEntity> Repository<TEntity>() where TEntity : EntityBase, IAggregateRoot
        {
            if (typeof(TEntity) == typeof(SmsOutboxMessage))
            {
                return (IRepository<TEntity>)(object)_messages;
            }
            throw new NotSupportedException();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MockSmsProvider(string name, Func<string, string, SmsSendResult> sendHandler) : ISmsProvider
    {
        public string Name => name;

        public Task<SmsSendResult> SendAsync(string recipient, string message, CancellationToken cancellationToken = default)
            => Task.FromResult(sendHandler(recipient, message));
    }

    [Fact]
    public async Task QueueWeighmentSms_IsIdempotent_ForSameMessageKey()
    {
        var repo = new InMemoryRepository<SmsOutboxMessage>();
        var options = Options.Create(new SmsOptions { Enabled = true, DefaultRecipient = "+919876543210" });
        var templateEngine = new SmsTemplateEngine();
        var recipientResolver = new SmsRecipientResolver(options);
        var provider = new MockSmsProvider("GsmModem", (r, m) => SmsSendResult.Success());

        var processor = new SmsOutboxProcessor(
            () => new TestUnitOfWork(repo),
            templateEngine,
            recipientResolver,
            [provider],
            options,
            NullLogger<SmsOutboxProcessor>.Instance);

        var weighment = Weighment.Open("MH12AB1000", WeighmentMode.GrossFirst, "Party A");
        typeof(EntityBase).GetProperty("Id")?.SetValue(weighment, 100L);
        weighment.AssignSlipNumber();
        weighment.RecordFirstWeight(new WeightCapture(20000m, DateTime.UtcNow, WeightSource.Indicator));
        weighment.RecordSecondWeight(new WeightCapture(10000m, DateTime.UtcNow, WeightSource.Indicator));

        var printData = WeighmentPrintDataFactory.Create(weighment);

        // Act: Enqueue twice
        var key1 = await processor.QueueWeighmentSmsAsync(printData);
        var key2 = await processor.QueueWeighmentSmsAsync(printData);

        // Assert: Same key returned, exactly 1 message in outbox
        Assert.NotNull(key1);
        Assert.Equal(key1, key2);
        Assert.Single(repo.Items);
        Assert.Equal($"{printData.SlipNumber}:Completion", key1);
    }

    [Fact]
    public async Task ProcessOutbox_RecoversStaleSendingMessages_BackToPending()
    {
        var repo = new InMemoryRepository<SmsOutboxMessage>();
        var options = Options.Create(new SmsOptions { Enabled = true, Provider = SmsProviderType.GsmModem });
        var templateEngine = new SmsTemplateEngine();
        var recipientResolver = new SmsRecipientResolver(options);
        var provider = new MockSmsProvider("GsmModem", (r, m) => SmsSendResult.Success("OK"));

        var processor = new SmsOutboxProcessor(
            () => new TestUnitOfWork(repo),
            templateEngine,
            recipientResolver,
            [provider],
            options,
            NullLogger<SmsOutboxProcessor>.Instance);

        // Seed a message stuck in Sending since 10 minutes ago
        var stuckMessage = SmsOutboxMessage.Create("WB-CRASH-01:Completion", 1, "+919876543210", "Crash Test Body");
        stuckMessage.MarkSending();
        var sendingSinceProp = typeof(SmsOutboxMessage).GetProperty("SendingSinceUtc");
        sendingSinceProp?.SetValue(stuckMessage, DateTime.UtcNow.AddMinutes(-10));
        repo.Items.Add(stuckMessage);

        // Act: Run outbox processing
        int processed = await processor.ProcessOutboxAsync();

        // Assert: Stale message recovered and delivered successfully
        Assert.Equal(1, processed);
        Assert.Equal(SmsOutboxStatus.Sent, stuckMessage.Status);
        Assert.NotNull(stuckMessage.SentAtUtc);
    }

    [Fact]
    public async Task ProcessOutbox_HandlesTransientError_WithExponentialBackoff()
    {
        var repo = new InMemoryRepository<SmsOutboxMessage>();
        var options = Options.Create(new SmsOptions { Enabled = true, Provider = SmsProviderType.GsmModem, RetryDelaySeconds = 10, MaxRetries = 3 });
        var templateEngine = new SmsTemplateEngine();
        var recipientResolver = new SmsRecipientResolver(options);
        var provider = new MockSmsProvider("GsmModem", (r, m) => SmsSendResult.TransientFailure("Signal busy"));

        var processor = new SmsOutboxProcessor(
            () => new TestUnitOfWork(repo),
            templateEngine,
            recipientResolver,
            [provider],
            options,
            NullLogger<SmsOutboxProcessor>.Instance);

        var message = SmsOutboxMessage.Create("WB-FAIL-01:Completion", 1, "+919876543210", "Fail Test Body", maxAttempts: 3);
        repo.Items.Add(message);

        // Attempt 1
        await processor.ProcessOutboxAsync();
        Assert.Equal(SmsOutboxStatus.Failed, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.NotNull(message.NextAttemptUtc);

        // Simulate retry delay passed
        var nextAttemptProp = typeof(SmsOutboxMessage).GetProperty("NextAttemptUtc");
        nextAttemptProp?.SetValue(message, DateTime.UtcNow.AddSeconds(-1));

        // Attempt 2
        await processor.ProcessOutboxAsync();
        Assert.Equal(SmsOutboxStatus.Failed, message.Status);
        Assert.Equal(2, message.Attempts);

        // Simulate retry delay passed again
        nextAttemptProp?.SetValue(message, DateTime.UtcNow.AddSeconds(-1));

        // Attempt 3 (Max retries reached -> DeadLetter)
        await processor.ProcessOutboxAsync();
        Assert.Equal(SmsOutboxStatus.DeadLetter, message.Status);
        Assert.Equal(3, message.Attempts);
    }
}
