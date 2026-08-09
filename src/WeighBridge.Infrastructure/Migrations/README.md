# Migrations

This folder holds the Entity Framework Core migrations for `WeighBridgeDbContext`.

Module 0.1 maps **no entities**, so no migration exists yet. The infrastructure is in
place and verified:

- `WeighBridgeDbContext` — the context, with automatic discovery of
  `IEntityTypeConfiguration<>` classes.
- `WeighBridgeDbContextFactory` — design-time factory, so the CLI tooling works
  without launching the WPF application.
- `DatabaseInitializer` — creates the database file, then applies pending migrations
  (or falls back to `EnsureCreated` while no migrations exist).

## Adding the first migration

Run from the repository root:

```bash
dotnet tool install --global dotnet-ef            # once per machine
dotnet ef migrations add Initial \
    --project src/WeighBridge.Infrastructure \
    --startup-project src/WeighBridge.Infrastructure \
    --output-dir Migrations
```

Once a migration exists, `DatabaseInitializer` automatically switches from
`EnsureCreated` to `Migrate`, so no startup code needs to change.

## Conventions

- One migration per module; name it after the module (`AddVehicleEntry`,
  `AddMasters`, …).
- Never edit an applied migration — add a new one.
- Every entity derives from `EntityBase`, so audit columns come for free.
