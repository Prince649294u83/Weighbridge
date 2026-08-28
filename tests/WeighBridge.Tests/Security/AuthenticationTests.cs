using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Security;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Services.Security;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Security;

/// <summary>
/// Pins the security defects the audit found in the sign-in path.
/// </summary>
/// <remarks>
/// <para>
/// F-004: the database initialiser seeded <c>admin</c> with a hash of a password published
/// in this repository, so every installation that had never changed it shared one known
/// administrator credential. The seed is gone and the first administrator is now appointed
/// at first run, which is what <see cref="FreshDatabase_HasNoAccountAndRequiresSetup"/>
/// and the two refusal tests hold in place.
/// </para>
/// <para>
/// F-005: passwords were stored as an unsalted single-round SHA-256 digest, which is a
/// lookup away from the plaintext for anything in a wordlist. Hashing is now PBKDF2 with a
/// per-password salt, and the tests below check the properties that distinguishes the two
/// rather than the exact bytes: equal passwords must not produce equal hashes.
/// </para>
/// <para>
/// F-020: the log enrichment named the Windows account rather than the operator who signed
/// in, so every audit entry on a shared terminal carried one name no matter who acted. The
/// operator now reaches the enrichment through <see cref="SignedInOperator"/>, which is what
/// <see cref="Authenticate_NamesTheOperator_InTheLogAndTheAuditTrail"/> holds in place.
/// </para>
/// </remarks>
public sealed class AuthenticationTests
{
    private const string GoodPassword = "Bridge-Weigh-2026";

    [Fact]
    public async Task FreshDatabase_HasNoAccountAndRequiresSetup()
    {
        using var root = new TempDataRoot();

        // The real initialiser, not a migration standing in for it: the seed that has to
        // stay deleted lived in InitializeAsync, so nothing less than running it can prove
        // it is gone.
        var result = await CreateInitializer(root).InitializeAsync();

        Assert.True(result.Succeeded, result.Message);

        await using var context = new WeighBridgeDbContext(DatabaseOptionsFor(root));
        Assert.Empty(await context.Set<User>().ToListAsync());

        using var harness = new AuthenticationHarness(root);
        Assert.True(await harness.Service.RequiresInitialSetupAsync());
    }

    /// <summary>
    /// F-023: a zero-byte file is a valid empty SQLite database, so the initialiser migrated
    /// a fresh schema over it and the terminal offered first-run administrator setup — on an
    /// installation that had been in service, to whoever happened to be standing there, with
    /// no credential and no indication that a database had been destroyed.
    /// </summary>
    [Fact]
    public async Task EmptyDatabaseFile_IsRefused_NotTreatedAsAFreshInstallation()
    {
        using var root = new TempDataRoot();
        root.Paths.EnsureCreated();

        var databaseFile = Path.Combine(root.Root, "Data", "weighbridge.db");
        File.WriteAllBytes(databaseFile, []);

        var result = await CreateInitializer(root).InitializeAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("empty", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(databaseFile, result.Message);

        // The file is the only evidence left that something damaged it, and a specialist may
        // still want it. Refusing is worth nothing if the schema is written anyway.
        Assert.Equal(0, new FileInfo(databaseFile).Length);
    }

    [Fact]
    public async Task InitialSetup_CreatesAnAdministratorAndSignsItIn()
    {
        using var harness = new AuthenticationHarness();

        Assert.True(await harness.Service.CreateInitialAdministratorAsync("bridge_admin", GoodPassword));

        Assert.False(await harness.Service.RequiresInitialSetupAsync());
        Assert.Equal("bridge_admin", harness.Permissions.CurrentOperator.UserName);
        Assert.Equal(Roles.Administrator.Name, harness.Permissions.CurrentOperator.Role.Name);

        // The password itself must not be recoverable from what was stored, and the stored
        // value must not be the digest format this fix replaced.
        var stored = await harness.StoredHashAsync("bridge_admin");
        Assert.DoesNotContain(GoodPassword, stored);
        Assert.StartsWith("pbkdf2-sha256$", stored);
    }

    [Fact]
    public async Task InitialSetup_IsRefusedOnceAnAccountExists()
    {
        using var harness = new AuthenticationHarness();

        Assert.True(await harness.Service.CreateInitialAdministratorAsync("first", GoodPassword));

        // The dialog only offers setup when no account exists; this is the check that makes
        // that a control rather than a courtesy.
        Assert.False(await harness.Service.CreateInitialAdministratorAsync("smuggled_in", GoodPassword));

        await using var context = new WeighBridgeDbContext(harness.Options);
        Assert.Single(await context.Set<User>().ToListAsync());
    }

    [Fact]
    public async Task InitialSetup_IsRefusedForATooShortPassword()
    {
        using var harness = new AuthenticationHarness();

        var tooShort = new string('a', IAuthenticationService.MinimumPasswordLength - 1);

        Assert.False(await harness.Service.CreateInitialAdministratorAsync("admin", tooShort));
        Assert.True(await harness.Service.RequiresInitialSetupAsync());
    }

    [Fact]
    public async Task Authenticate_AcceptsTheChosenPasswordAndRejectsAnyOther()
    {
        using var harness = new AuthenticationHarness();
        await harness.Service.CreateInitialAdministratorAsync("bridge_admin", GoodPassword);

        Assert.True(await harness.Service.AuthenticateAsync("bridge_admin", GoodPassword));
        Assert.False(await harness.Service.AuthenticateAsync("bridge_admin", GoodPassword + "!"));
        Assert.False(await harness.Service.AuthenticateAsync("no_such_operator", GoodPassword));

        // The credential this fix removed, in case it is ever seeded again.
        Assert.False(await harness.Service.AuthenticateAsync("admin", "admin123"));
    }

    /// <summary>
    /// F-020, end to end: signing in has to reach the log enrichment, not only the permission
    /// service. The success entry asserted below is the one quoted as evidence in the audit —
    /// it read <c>user=dell</c>, the Windows account, on a terminal a shift of operators share.
    /// </summary>
    /// <remarks>
    /// Goes through the real <see cref="PermissionService"/> rather than setting the shared
    /// cell by hand, because the defect was the wiring: nothing published the operator, and a
    /// missing publish is exactly what this has to catch.
    /// </remarks>
    [Fact]
    public async Task Authenticate_NamesTheOperator_InTheLogAndTheAuditTrail()
    {
        using var harness = new AuthenticationHarness();
        await harness.Service.CreateInitialAdministratorAsync("bridge_admin", GoodPassword);

        Assert.True(await harness.Service.AuthenticateAsync("bridge_admin", GoodPassword));

        // Written by SignIn immediately after it installs the operator.
        var success = harness.Sink.Entries.Last(e => e.Message.Contains("authenticated successfully"));
        Assert.Contains("user=bridge_admin", success.ScopeText);
        Assert.DoesNotContain("user=testoperator", success.ScopeText);

        // AuditLogger.Record puts no operator in the message, so the enrichment is the only
        // record an audit entry keeps of who acted.
        harness.Audit.Record("Record second weight", "Weighment", "WB-000001");

        Assert.Contains("user=bridge_admin", harness.Sink.Entries[^1].ScopeText);
        Assert.DoesNotContain("user=testoperator", harness.Sink.Entries[^1].ScopeText);
    }

    [Fact]
    public async Task Authenticate_RejectsADisabledAccount()
    {
        using var harness = new AuthenticationHarness();
        await harness.Service.CreateInitialAdministratorAsync("bridge_admin", GoodPassword);

        await using (var context = new WeighBridgeDbContext(harness.Options))
        {
            var user = await context.Set<User>().FirstAsync();
            user.Deactivate();
            await context.SaveChangesAsync();
        }

        Assert.False(await harness.Service.AuthenticateAsync("bridge_admin", GoodPassword));
    }

    [Fact]
    public void HashPassword_SaltsEveryPasswordSeparately()
    {
        using var harness = new AuthenticationHarness();

        var first = harness.Service.HashPassword(GoodPassword);
        var second = harness.Service.HashPassword(GoodPassword);

        // The property that an unsalted digest cannot have: two hashes of one password are
        // different, so a stolen database cannot be attacked with one precomputed table and
        // two accounts sharing a password do not look alike.
        Assert.NotEqual(first, second);

        Assert.True(harness.Service.VerifyPassword(GoodPassword, first));
        Assert.True(harness.Service.VerifyPassword(GoodPassword, second));
        Assert.False(harness.Service.VerifyPassword(GoodPassword + "!", first));
    }

    [Fact]
    public void VerifyPassword_StillOpensAnAccountStoredInTheLegacyFormat()
    {
        using var harness = new AuthenticationHarness();

        // Written by builds before this one: base64 of an unsalted SHA-256 digest. Accepted
        // so upgrading the application does not lock an existing installation out of its own
        // accounts. Nothing in this build writes this format.
        var legacy = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GoodPassword)));

        Assert.True(harness.Service.VerifyPassword(GoodPassword, legacy));
        Assert.False(harness.Service.VerifyPassword(GoodPassword + "!", legacy));
    }

    /// <summary>
    /// A database created by an earlier build still holds that build's passwords, and the
    /// account that build seeded had one published in the repository. Verification stays
    /// backwards compatible on purpose — refusing would lock an installation out of every
    /// account it has — but a legacy credential no longer lingers: the first successful
    /// sign-in re-hashes it into the current salted format, under the operator's own
    /// authenticated session.
    /// </summary>
    [Fact]
    public async Task Authenticate_AgainstAPreSaltDigest_UpgradesTheHashAndSaysSo()
    {
        using var harness = new AuthenticationHarness();

        var legacy = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GoodPassword)));

        await using (var context = new WeighBridgeDbContext(harness.Options))
        {
            context.Add(User.Create("olduser", "Old User", legacy, Roles.Administrator.Name));
            await context.SaveChangesAsync();
        }

        Assert.True(await harness.Service.AuthenticateAsync("olduser", GoodPassword));

        var upgrade = harness.Sink.Entries.LastOrDefault(entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("upgraded from the pre-salt format"));

        Assert.NotNull(upgrade);
        Assert.Contains("olduser", upgrade.Message);

        // The stored hash is now in the current format: the weak digest is gone.
        await using (var context = new WeighBridgeDbContext(harness.Options))
        {
            var user = await context.Set<User>().SingleAsync(u => u.Username == "olduser");
            Assert.StartsWith("pbkdf2-sha256$", user.PasswordHash);
            Assert.NotEqual(legacy, user.PasswordHash);
        }

        // The upgraded hash still verifies with the same password, and the old digest does not.
        Assert.True(await harness.Service.AuthenticateAsync("olduser", GoodPassword));

        // Whatever else those lines say, they must not say the password.
        Assert.DoesNotContain(
            GoodPassword,
            string.Join(' ', harness.Sink.Entries.Select(entry => entry.Message)));
    }

    /// <summary>
    /// Repeated wrong passwords must not be free. After the configured number of failures
    /// the account locks for the configured window even when the correct password arrives.
    /// </summary>
    [Fact]
    public async Task Authenticate_LocksTheAccount_AfterRepeatedFailures()
    {
        using var harness = new AuthenticationHarness();

        await using (var context = new WeighBridgeDbContext(harness.Options))
        {
            context.Add(User.Create(
                "lockme", "Lock Me", harness.Service.HashPassword(GoodPassword), Roles.Operator.Name));
            await context.SaveChangesAsync();
        }

        // Default options: five attempts, five-minute lockout.
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.False(await harness.Service.AuthenticateAsync("lockme", "wrong-password"));
        }

        // The fifth failure trips the lockout...
        Assert.False(await harness.Service.AuthenticateAsync("lockme", "wrong-password"));

        // ...and the lockout holds against the CORRECT password too.
        Assert.False(await harness.Service.AuthenticateAsync("lockme", GoodPassword));
    }

    /// <summary>
    /// A correct password clears the failure history, so one typo after a good sign-in is
    /// not one attempt away from a lockout.
    /// </summary>
    [Fact]
    public async Task Authenticate_SuccessResetsTheFailureCount()
    {
        using var harness = new AuthenticationHarness();

        await using (var context = new WeighBridgeDbContext(harness.Options))
        {
            context.Add(User.Create(
                "resetme", "Reset Me", harness.Service.HashPassword(GoodPassword), Roles.Operator.Name));
            await context.SaveChangesAsync();
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.False(await harness.Service.AuthenticateAsync("resetme", "wrong-password"));
        }

        Assert.True(await harness.Service.AuthenticateAsync("resetme", GoodPassword));

        // The count was cleared: four more mistakes do not lock anything.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.False(await harness.Service.AuthenticateAsync("resetme", "wrong-password"));
        }

        // A fifth failure after reset starts counting again rather than locking instantly,
        // and a correct password right after still opens.
        Assert.True(await harness.Service.AuthenticateAsync("resetme", GoodPassword));
    }

    /// <summary>
    /// Before anyone signs in the terminal grants nothing: the unauthenticated role has an
    /// empty permission set, so nothing executes until somebody authenticates.
    /// </summary>
    [Fact]
    public void PermissionService_StartsWithNoPermissions_AtAll()
    {
        using var harness = new AuthenticationHarness();

        foreach (var permission in Core.Security.Permissions.All)
        {
            Assert.False(harness.Permissions.HasPermission(permission), permission.Key);
        }
    }

    /// <summary>
    /// The counterpart: an account stored in this build's format must not be reported as though
    /// it needed attention, or the warning becomes noise and stops meaning anything.
    /// </summary>
    [Fact]
    public async Task Authenticate_AgainstACurrentHash_SaysNothingAboutTheFormat()
    {
        using var harness = new AuthenticationHarness();

        await using (var context = new WeighBridgeDbContext(harness.Options))
        {
            context.Add(User.Create(
                "newuser", "New User", harness.Service.HashPassword(GoodPassword), Roles.Administrator.Name));
            await context.SaveChangesAsync();
        }

        Assert.True(await harness.Service.AuthenticateAsync("newuser", GoodPassword));

        Assert.DoesNotContain(
            harness.Sink.Entries,
            entry => entry.Message.Contains("pre-salt format"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$600000$!!!not base64!!!$aGFzaA==")]
    [InlineData("pbkdf2-sha256$600000$c2FsdA==")]
    public void VerifyPassword_RejectsAMalformedStoredHashInsteadOfThrowing(string stored)
    {
        using var harness = new AuthenticationHarness();

        // A row corrupted by hand-editing the database must fail the sign-in, not take the
        // application down with an unhandled FormatException at the login dialog.
        Assert.False(harness.Service.VerifyPassword(GoodPassword, stored));
    }

    /// <summary>
    /// Sign-out has to leave the terminal with no authority at all. Before this existed, the
    /// only way off a signed-in shell was to close the application, so a shift change on a
    /// shared terminal meant the next operator inherited the previous one's role — and every
    /// audit entry they produced carried the previous operator's name.
    /// </summary>
    [Fact]
    public async Task SignOut_LeavesTheTerminalUnauthenticated_AndRecordsWhoLeft()
    {
        using var harness = new AuthenticationHarness();
        await harness.Service.CreateInitialAdministratorAsync("bridge_admin", GoodPassword);

        Assert.Equal(Roles.Administrator, harness.Permissions.CurrentOperator.Role);
        Assert.True(harness.Permissions.HasPermission(Permissions.UsersManage));

        harness.Service.SignOut();

        // The role is what stops a command, so it is the role that is asserted - not just
        // the name. An identity whose name was cleared but whose role survived would be a
        // signed-out session that can still manage users.
        Assert.Equal(Roles.Unauthenticated, harness.Permissions.CurrentOperator.Role);
        Assert.False(harness.Permissions.HasPermission(Permissions.UsersManage));
        Assert.False(harness.Permissions.Authorize(Permissions.UsersManage).IsAuthorized);
        Assert.NotEqual("bridge_admin", harness.Permissions.CurrentOperator.UserName);

        // Recorded against the operator who signed out, not the Windows account that owns
        // the terminal afterwards: the entry is written before the identity is cleared.
        var signedOut = harness.Sink.Entries.Last(e => e.Message.Contains("SignedOut"));
        Assert.Contains("bridge_admin", signedOut.Message);
        Assert.Contains("user=bridge_admin", signedOut.ScopeText);
    }

    /// <summary>
    /// A lockout is a defence of the terminal, so it must not be clearable by the person
    /// being locked out. Signing out and back in is the obvious way to try.
    /// </summary>
    [Fact]
    public async Task SignOut_DoesNotClearAnActiveLockout()
    {
        using var harness = new AuthenticationHarness();
        await harness.Service.CreateInitialAdministratorAsync("bridge_admin", GoodPassword);

        var allowed = new SecurityOptions().EffectiveMaxFailedSignIns;
        for (var attempt = 0; attempt < allowed; attempt++)
        {
            Assert.False(await harness.Service.AuthenticateAsync("bridge_admin", "not-the-password"));
        }

        harness.Service.SignOut();

        // The correct password, refused because the lockout is still in force.
        Assert.False(await harness.Service.AuthenticateAsync("bridge_admin", GoodPassword));
        Assert.Equal(Roles.Unauthenticated, harness.Permissions.CurrentOperator.Role);
    }

    private static DbContextOptions<WeighBridgeDbContext> DatabaseOptionsFor(TempDataRoot root)
        => new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root.Root, "Data", "weighbridge.db")}")
            .Options;

    private static DatabaseInitializer CreateInitializer(TempDataRoot root)
    {
        var databaseOptions = Options.Create(new DatabaseOptions());

        return new DatabaseInitializer(
            new TestContextFactory(DatabaseOptionsFor(root)),
            new ConnectionStringProvider(databaseOptions, root.Paths),
            databaseOptions,
            NullLogger<DatabaseInitializer>.Instance);
    }

    private sealed class TestContextFactory(DbContextOptions<WeighBridgeDbContext> options)
        : IDbContextFactory<WeighBridgeDbContext>
    {
        public WeighBridgeDbContext CreateDbContext() => new(options);
    }

    /// <summary>
    /// A real <see cref="AuthenticationService"/> over a real migrated SQLite file, because
    /// what is being tested is what ends up in the database.
    /// </summary>
    private sealed class AuthenticationHarness : IDisposable
    {
        private readonly TempDataRoot? _owned;

        public AuthenticationHarness()
            : this(new TempDataRoot(), ownsRoot: true)
        {
        }

        public AuthenticationHarness(TempDataRoot root)
            : this(root, ownsRoot: false)
        {
        }

        private AuthenticationHarness(TempDataRoot root, bool ownsRoot)
        {
            _owned = ownsRoot ? root : null;

            Directory.CreateDirectory(Path.Combine(root.Root, "Data"));
            Options = DatabaseOptionsFor(root);

            using (var context = new WeighBridgeDbContext(Options))
            {
                context.Database.Migrate();
            }

            var applicationInfo = new TestApplicationInfoService();
            var factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(Sink);
            });

            // One cell shared by the permission service that writes it and the loggers that
            // read it, exactly as the container wires it, so a test can see what the audit
            // trail would actually have recorded.
            var signedIn = new SignedInOperator();

            // Unauthenticated until sign-in, so a passing sign-in assertion cannot be the
            // default operator's role being mistaken for the one that was just authenticated.
            Permissions = new PermissionService(
                applicationInfo,
                new ApplicationLogger(factory, applicationInfo, signedIn),
                signedIn);

            Audit = new AuditLogger(factory, applicationInfo, auditStore: null, signedIn);

            Service = new AuthenticationService(
                () => new UnitOfWork(new WeighBridgeDbContext(Options), signedIn),
                Permissions,
                new ApplicationLogger(factory, applicationInfo, signedIn),
                Audit,
                Microsoft.Extensions.Options.Options.Create(new SecurityOptions()));
        }

        public DbContextOptions<WeighBridgeDbContext> Options { get; }

        public RecordingLoggerProvider Sink { get; } = new();

        public AuditLogger Audit { get; }

        public PermissionService Permissions { get; }

        public AuthenticationService Service { get; }

        public async Task<string> StoredHashAsync(string username)
        {
            await using var context = new WeighBridgeDbContext(Options);
            var user = await context.Set<User>().FirstAsync(u => u.Username == username);
            return user.PasswordHash;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            _owned?.Dispose();
        }
    }
}
