using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WeighBridge.App.Services;
using WeighBridge.Core.Application;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Security;
using WeighBridge.Core.Threading;
using WeighBridge.Services.Busy;
using Xunit;

namespace WeighBridge.Tests.Security;

public sealed class SessionInactivityTests
{
    private sealed class TestAppLogger : IApplicationLogger
    {
        public string Category => "Test";
        public void Trace(string message, params object?[] args) { }
        public void Debug(string message, params object?[] args) { }
        public void Information(string message, params object?[] args) { }
        public void Warning(string message, params object?[] args) { }
        public void Error(string message, params object?[] args) { }
        public void Error(Exception exception, string message, params object?[] args) { }
        public void Critical(string message, params object?[] args) { }
        public void Critical(Exception exception, string message, params object?[] args) { }
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable BeginOperation(string module, string? correlationId = null) => new EmptyDisposable();
        private sealed class EmptyDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public bool SignedOutCalled { get; private set; }
        public Task<bool> AuthenticateAsync(string username, string password) => Task.FromResult(true);
        public void SignOut() => SignedOutCalled = true;
        public Task<bool> RequiresInitialSetupAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CreateInitialAdministratorAsync(string username, string password, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public string HashPassword(string password) => password;
        public bool VerifyPassword(string password, string storedHash) => true;
    }

    private sealed class StubPermissionService : IPermissionService
    {
        public OperatorIdentity CurrentOperator { get; set; } = new("operator1", "Operator One", Roles.Operator);
        public event EventHandler<OperatorChangedEventArgs>? OperatorChanged { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public AuthorizationResult Authorize(Permission permission) => AuthorizationResult.Allowed;
        public AuthorizationResult Authorize(object candidate) => AuthorizationResult.Allowed;
        public bool HasPermission(Permission permission) => true;
        public bool HasAllPermissions(params Permission[] permissions) => true;
        public bool HasAnyPermission(params Permission[] permissions) => true;
        public void SetOperator(OperatorIdentity identity) { CurrentOperator = identity; }
        public void SignOut() { CurrentOperator = new("anon", "Anonymous", Roles.Unauthenticated); }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public List<string> Actions { get; } = [];
        public string Category => "StubAudit";
        public void Record(string action, string entity, string? entityId = null, string? details = null) => Actions.Add(action);
        public void RecordDenied(string action, string entity, string reason, string? entityId = null) => Actions.Add(action);
        public void RecordFailed(string action, string entity, string reason, string? entityId = null) => Actions.Add(action);
        public void Trace(string message, params object?[] args) { }
        public void Debug(string message, params object?[] args) { }
        public void Information(string message, params object?[] args) { }
        public void Warning(string message, params object?[] args) { }
        public void Error(string message, params object?[] args) { }
        public void Error(Exception exception, string message, params object?[] args) { }
        public void Critical(string message, params object?[] args) { }
        public void Critical(Exception exception, string message, params object?[] args) { }
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable BeginOperation(string module, string? correlationId = null) => new EmptyDisposable();
        private sealed class EmptyDisposable : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public async Task SessionInactivityService_DefersLockout_WhileBusyOperationActive()
    {
        var busy = new BusyStateService(new ImmediateUiDispatcher(), new TestAppLogger());
        var auth = new StubAuthenticationService();
        var perm = new StubPermissionService();
        var audit = new StubAuditLogger();
        var options = Options.Create(new SecurityOptions { InactivityTimeoutMinutes = 1 });

        using var service = new SessionInactivityService(
            perm,
            auth,
            busy,
            audit,
            options,
            NullLogger<SessionInactivityService>.Instance);

        // Enter busy operation
        var scope = await busy.BeginAsync("Printing critical ticket");
        Assert.True(busy.IsBusy);

        // Simulate elapsed activity beyond timeout
        var lastActivityProp = typeof(SessionInactivityService).GetField("_lastActivityUtc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lastActivityProp?.SetValue(service, DateTime.UtcNow.AddMinutes(-5));

        // Trigger timer tick via reflection
        var tickMethod = typeof(SessionInactivityService).GetMethod("OnTimerTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        tickMethod?.Invoke(service, [null, EventArgs.Empty]);

        // Assert: Lockout was DEFERRED because busy is true
        Assert.False(auth.SignedOutCalled);

        // Now complete the busy operation
        scope.Dispose();

        // Assert: Once busy ended, deferred lock immediately executed
        Assert.True(auth.SignedOutCalled);
        Assert.Contains(AuditActions.InactivityLock, audit.Actions);
    }
}
