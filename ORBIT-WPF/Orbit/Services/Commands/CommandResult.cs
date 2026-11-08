namespace Orbit.Services.Commands;

/// <summary>
/// Result of executing a parsed voice command.
/// </summary>
public record CommandResult(
    bool Success,
    string Message,
    CommandIntent Intent,
    string? Detail = null
);

