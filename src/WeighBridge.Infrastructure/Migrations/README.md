# Migrations

This folder holds the Entity Framework Core migrations for `WeighBridgeDbContext`.

| Migration | Added by | What it creates |
| --- | --- | --- |
| `20260815104634_AddVehicleEntry` | Prompt 7 (Vehicle Entry) | `Weighments` — the first real table. |

The supporting plumbing:

- `WeighBridgeDbContext` — the context, with automatic discovery of
  `IEntityTypeConfiguration<>` classes.
- `WeighBridgeDbContextFactory` — design-time factory, so the CLI tooling works
  without launching the WPF application.
- `DatabaseInitializer` — creates the database file, then applies pending migrations.
  The call is memoized, so the background run started at startup and the `await` from
  the first module that opens are the same run rather than two concurrent migrations.

## Adding the next migration

Run from the repository root:

```bash
dotnet tool install --global dotnet-ef            # once per machine
dotnet ef migrations add AddMasters \
    --project src/WeighBridge.Infrastructure \
    --startup-project src/WeighBridge.Infrastructure \
    --output-dir Migrations
```

Three files are produced and all three belong in the commit: the migration, its
`.Designer.cs`, and the regenerated `WeighBridgeDbContextModelSnapshot.cs`. Leaving the
snapshot out makes the *next* migration diff against the wrong model.

## Conventions

- One migration per module; name it after the module (`AddVehicleEntry`, `AddMasters`, …).
- **Never edit an applied migration** — add a new one. Editing one that has already run
  on a machine leaves that database on a schema no migration describes, and
  `Migrate()` will not repair it. That includes "just fixing" a column type.
- Every entity derives from `EntityBase`, so audit columns come for free.
- SQLite has no decimal type. Weights are mapped through a `ValueConverter<decimal, long>`
  that stores whole grams (see `Persistence/Configurations/WeighmentConfiguration.cs`), so
  a money-style figure round-trips exactly instead of drifting through `REAL`.

## Starting over during development

`DatabaseInitializer` migrates whatever it finds. To go back to a clean first launch,
close the application and delete the file (and its `-wal` / `-shm` siblings):

```bash
rm "$LOCALAPPDATA/WeighBridge Modern/Data/weighbridge.db"*
```

`scripts/vehicle-entry-smoke.ps1` does exactly this before its first run, which is what
covers the first-launch migration path.
