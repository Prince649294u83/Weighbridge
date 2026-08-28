using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;
using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Security;

namespace WeighBridge.Core.Logging;

/// <summary>
/// Shared implementation behind every category logger.
/// </summary>
/// <remarks>
/// <para>
/// The enrichment is assembled per entry and pushed as a logging scope, so it reaches
/// whatever providers are registered: the existing file logger renders it as text, and a
/// structured sink added later reads the same fields as key/value pairs.
/// </para>
/// <para>
/// Machine name and version are captured once in the constructor — they cannot change
/// while the process runs, and reading them per entry would cost a reflection lookup on
/// a path that runs thousands of times a shift.
/// </para>
/// <para>
/// The operator is not captured, because it does change: it is read per entry from
/// <see cref="SignedInOperator"/>. Capturing it was a real defect — every entry in the
/// audit trail named the Windows account the terminal runs under rather than whoever had
/// signed in, so on a shared terminal no action could be attributed to anyone.
/// </para>
/// </remarks>
public abstract class CategoryLoggerBase : IApplicationLogger
{
    private readonly ILogger _logger;
    private readonly string _machineName;
    private readonly string _applicationVersion;
    private readonly string _terminalUser;
    private readonly SignedInOperator? _signedInOperator;

    /// <summary>Creates a category logger over the supplied sink.</summary>
    /// <param name="category">Operational category name, from <see cref="LogCategory"/>.</param>
    /// <param name="loggerFactory">Factory the underlying <see cref="ILogger"/> comes from.</param>
    /// <param name="applicationInfo">Supplies the terminal and version fields.</param>
    /// <param name="signedInOperator">
    /// Who is signed in. Omitted only where nobody can be — a test, or a log written before
    /// the container exists — and the Windows account is then used, which is the honest
    /// answer for an entry no operator caused.
    /// </param>
    protected CategoryLoggerBase(
        string category,
        ILoggerFactory loggerFactory,
        IApplicationInfoService applicationInfo,
        SignedInOperator? signedInOperator = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(applicationInfo);

        Category = category;
        _logger = loggerFactory.CreateLogger(category);
        _machineName = applicationInfo.MachineName;
        _applicationVersion = applicationInfo.Version;
        _terminalUser = applicationInfo.CurrentUserName;
        _signedInOperator = signedInOperator;
    }

    /// <summary>Category these entries are written under.</summary>
    public string Category { get; }

    /// <inheritdoc />
    public void Trace(string message, params object?[] args) => Write(LogLevel.Trace, null, message, args);

    /// <inheritdoc />
    public void Debug(string message, params object?[] args) => Write(LogLevel.Debug, null, message, args);

    /// <inheritdoc />
    public void Information(string message, params object?[] args) => Write(LogLevel.Information, null, message, args);

    /// <inheritdoc />
    public void Warning(string message, params object?[] args) => Write(LogLevel.Warning, null, message, args);

    /// <inheritdoc />
    public void Error(string message, params object?[] args) => Write(LogLevel.Error, null, message, args);

    /// <inheritdoc />
    public void Error(Exception exception, string message, params object?[] args)
        => Write(LogLevel.Error, exception, message, args);

    /// <inheritdoc />
    public void Critical(string message, params object?[] args) => Write(LogLevel.Critical, null, message, args);

    /// <inheritdoc />
    public void Critical(Exception exception, string message, params object?[] args)
        => Write(LogLevel.Critical, exception, message, args);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => _logger.IsEnabled(level);

    /// <inheritdoc />
    public IDisposable BeginOperation(string module, string? correlationId = null)
        => OperationScope.Begin(module, correlationId);

    /// <summary>
    /// Writes one enriched entry.
    /// </summary>
    /// <remarks>
    /// Returns early when the level is disabled so neither the enrichment nor the message
    /// formatting is paid for. The scope is disposed immediately after the write, which is
    /// correct: it decorates this entry only.
    /// </remarks>
    protected void Write(LogLevel level, Exception? exception, string message, object?[] args)
    {
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var enrichment = new LogEnrichment(
            module: ModuleScope.Current,
            user: _signedInOperator?.UserName ?? _terminalUser,
            correlationId: CorrelationScope.Current,
            machineName: _machineName,
            applicationVersion: _applicationVersion);

        using (_logger.BeginScope(enrichment))
        {
            _logger.Log(level, exception, message, args ?? []);
        }
    }
}
