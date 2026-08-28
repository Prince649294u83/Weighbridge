using WeighBridge.Domain.Security;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Tests.Masters;
using Xunit;

namespace WeighBridge.Tests.Infrastructure;

/// <summary>
/// Guards the change-tracker rule that user management got wrong: a repository only
/// commits through the unit of work that created it.
/// </summary>
/// <remarks>
/// AdministrationViewModel injected <c>IRepository&lt;User&gt;</c> and <c>IUnitOfWork</c>
/// side by side. WeighBridgeDbContext is registered transient, so those two owned separate
/// change trackers: every create, update, disable and enable staged the change on one
/// context and committed the other. Nothing persisted, nothing threw, and the view model
/// reported success. These tests pin both halves of that behaviour so the shape cannot
/// come back unnoticed.
/// </remarks>
public sealed class UnitOfWorkTrackerTests
{
    [Fact]
    public async Task RepositoryFromUnitOfWork_PersistsTheWrite()
    {
        using var root = new TempDataRoot();
        var options = MasterHarness.MigratedDatabase(root);

        await using (var unitOfWork = new UnitOfWork(new WeighBridgeDbContext(options)))
        {
            await unitOfWork.Repository<User>().AddAsync(
                User.Create("shared_tracker", "Shared Tracker", "hash", "Operator"));

            Assert.Equal(1, await unitOfWork.SaveChangesAsync());
        }

        await using var verify = new UnitOfWork(new WeighBridgeDbContext(options));
        var saved = await verify.Repository<User>().FindAsync(u => u.Username == "shared_tracker");
        Assert.Single(saved);
    }

    [Fact]
    public async Task RepositoryOverItsOwnContext_SavesNothingThroughAnotherUnitOfWork()
    {
        using var root = new TempDataRoot();
        var options = MasterHarness.MigratedDatabase(root);

        // Exactly what the container hands out when both IRepository<T> and IUnitOfWork
        // are injected: two contexts, two change trackers.
        await using var repositoryContext = new WeighBridgeDbContext(options);
        var detachedRepository = new EfRepository<User>(repositoryContext);

        await using var unitOfWork = new UnitOfWork(new WeighBridgeDbContext(options));

        await detachedRepository.AddAsync(
            User.Create("split_tracker", "Split Tracker", "hash", "Operator"));

        // The silent part: no exception, and a zero row count nobody was checking.
        Assert.Equal(0, await unitOfWork.SaveChangesAsync());

        var saved = await unitOfWork.Repository<User>().FindAsync(u => u.Username == "split_tracker");
        Assert.Empty(saved);
    }
}
