namespace WeighBridge.Core.Commands;

/// <summary>How far a command execution has progressed.</summary>
public enum CommandExecutionState
{
    /// <summary>Waiting for a slot or about to start.</summary>
    Pending = 0,

    /// <summary>Dependency validation is running.</summary>
    Validating = 1,

    /// <summary>Permission checks have passed; the command is being authorized.</summary>
    Authorizing = 2,

    /// <summary>The command's own work is running.</summary>
    Executing = 3,

    /// <summary>Finished and the result is being recorded, logged and published.</summary>
    Completing = 4,

    /// <summary>Terminal. Set on success, denial, failure and cancellation alike.</summary>
    Completed = 5,
}
