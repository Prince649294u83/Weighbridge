namespace WeighBridge.Domain.Common;

/// <summary>
/// Marks an entity as the root of a consistency boundary. Repositories are only
/// ever exposed for aggregate roots, which keeps persistence access intentional.
/// </summary>
public interface IAggregateRoot;
