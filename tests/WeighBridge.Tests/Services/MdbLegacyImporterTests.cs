using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Common;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Import;
using Xunit;

namespace WeighBridge.Tests.Services;

public sealed class MdbLegacyImporterTests
{
    [Fact]
    public async Task ImportAsync_WhenPathEmpty_ThrowsArgumentException()
    {
        var importer = new MdbLegacyImporter(
            () => new StubUnitOfWork(),
            NullLogger<MdbLegacyImporter>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(string.Empty));
    }

    [Fact]
    public async Task ImportAsync_WhenFileDoesNotExist_ThrowsFileNotFoundException()
    {
        var importer = new MdbLegacyImporter(
            () => new StubUnitOfWork(),
            NullLogger<MdbLegacyImporter>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(() => importer.ImportAsync("C:\\nonexistent_file_12345.mdb"));
    }

    [Fact]
    public async Task ImportAsync_WhenExtensionUnsupported_ThrowsNotSupportedException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var renamed = Path.ChangeExtension(tempFile, ".xyz");
            File.Move(tempFile, renamed);

            var importer = new MdbLegacyImporter(
                () => new StubUnitOfWork(),
                NullLogger<MdbLegacyImporter>.Instance);

            await Assert.ThrowsAsync<NotSupportedException>(() => importer.ImportAsync(renamed));
            File.Delete(renamed);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenCsvProvided_ImportsWeighmentRecords()
    {
        var tempCsv = Path.GetTempFileName() + ".csv";
        try
        {
            var csvContent = "VehicleNo,PartyName,MaterialName,Gross,Tare,Charges,Remarks\n" +
                             "MH12AB1234,Alpha Logistics,Coal,25000,10000,250,Test Remarks\n" +
                             "DL01XY9876,Beta Cement,Cement,32000,12000,300,Second Entry\n";

            await File.WriteAllTextAsync(tempCsv, csvContent);

            var stubUow = new StubUnitOfWork();
            var importer = new MdbLegacyImporter(
                () => stubUow,
                NullLogger<MdbLegacyImporter>.Instance);

            var result = await importer.ImportAsync(tempCsv);

            Assert.Equal(2, result.WeighmentsImported);
            Assert.False(result.HasErrors);
            Assert.Equal(2, stubUow.WeighmentRepo.Items.Count);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    private sealed class StubUnitOfWork : IUnitOfWork
    {
        public StubRepository<Weighment> WeighmentRepo { get; } = new();

        public IRepository<TEntity> Repository<TEntity>() where TEntity : EntityBase, IAggregateRoot
        {
            if (typeof(TEntity) == typeof(Weighment))
            {
                return (IRepository<TEntity>)(object)WeighmentRepo;
            }

            return new StubRepository<TEntity>();
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubRepository<T> : IRepository<T> where T : EntityBase, IAggregateRoot
    {
        public List<T> Items { get; } = [];

        public Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.ToList());

        public Task<IReadOnlyList<T>> FindAsync(System.Linq.Expressions.Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).ToList());

        public Task<IReadOnlyList<T>> ListRecentAsync(
            System.Linq.Expressions.Expression<Func<T, bool>>? predicate = null,
            int? take = null,
            CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            query = query.OrderByDescending(x => x.Id);
            if (take.HasValue) query = query.Take(take.Value);
            return Task.FromResult<IReadOnlyList<T>>(query.ToList());
        }

        public Task<IReadOnlyList<T>> QueryAsync(
            System.Linq.Expressions.Expression<Func<T, bool>>? predicate,
            Func<IQueryable<T>, IQueryable<T>>? transform,
            CancellationToken cancellationToken = default)
        {
            var query = Items.AsQueryable();
            if (predicate != null) query = query.Where(predicate);
            if (transform != null) query = transform(query);
            return Task.FromResult<IReadOnlyList<T>>(query.ToList());
        }

        public Task<int> CountAsync(
            System.Linq.Expressions.Expression<Func<T, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            int count = predicate == null ? Items.Count : Items.AsQueryable().Count(predicate);
            return Task.FromResult(count);
        }

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity) { }
        public void Remove(T entity) { Items.Remove(entity); }
    }
}
