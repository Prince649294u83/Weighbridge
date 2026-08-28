using WeighBridge.Core.Events;

namespace WeighBridge.Core.Events.Catalog;

#region Vehicle Events

public sealed class VehicleCreatedEvent(long id, string vehicleNumber, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string VehicleNumber { get; } = vehicleNumber;
}

public sealed class VehicleUpdatedEvent(long id, string vehicleNumber, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string VehicleNumber { get; } = vehicleNumber;
}

public sealed class VehicleDeactivatedEvent(long id, string vehicleNumber, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string VehicleNumber { get; } = vehicleNumber;
}

public sealed class VehicleReactivatedEvent(long id, string vehicleNumber, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string VehicleNumber { get; } = vehicleNumber;
}

#endregion

#region Party Events

public sealed class PartyCreatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class PartyUpdatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class PartyDeactivatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class PartyReactivatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

#endregion

#region Material Events

public sealed class MaterialCreatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class MaterialUpdatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class MaterialDeactivatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class MaterialReactivatedEvent(long id, string name, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string Name { get; } = name;
}

#endregion

#region VehicleType Events

public sealed class VehicleTypeCreatedEvent(long id, string typeName, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string TypeName { get; } = typeName;
}

public sealed class VehicleTypeUpdatedEvent(long id, string typeName, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string TypeName { get; } = typeName;
}

public sealed class VehicleTypeDeactivatedEvent(long id, string typeName, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string TypeName { get; } = typeName;
}

public sealed class VehicleTypeReactivatedEvent(long id, string typeName, string? source = null)
    : ApplicationEvent(source)
{
    public long Id { get; } = id;
    public string TypeName { get; } = typeName;
}

#endregion
