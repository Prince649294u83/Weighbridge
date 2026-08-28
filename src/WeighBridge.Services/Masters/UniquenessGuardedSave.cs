using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;

namespace WeighBridge.Services.Masters;

/// <summary>
/// Saves through a unit of work, translating database uniqueness violations into the same
/// friendly failure the pre-check produces.
/// </summary>
/// <remarks>
/// Every master table carries a filtered unique index, and the open-weighment rule is
/// enforced by one too. Those indexes are what actually stop two operators creating the
/// same record in the instant between the pre-check passing and the insert landing; this
/// wrapper makes that race surface as an ordinary "already exists" message rather than a
/// raw <see cref="DbUpdateException"/>.
/// </remarks>
internal static class UniquenessGuardedSave
{
    public static async Task SaveChangesAsync(
        IUnitOfWork unitOfWork,
        string duplicateMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new InvalidOperationException(duplicateMessage, exception);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
}
