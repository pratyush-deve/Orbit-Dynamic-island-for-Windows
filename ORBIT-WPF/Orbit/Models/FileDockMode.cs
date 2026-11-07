namespace Orbit.Models;

/// <summary>
/// Defines the activation state of the ORBIT File Dock.
/// </summary>
public enum FileDockMode
{
    /// <summary>
    /// File dock is closed/hidden.
    /// </summary>
    Closed,

    /// <summary>
    /// Temporary preview/drop mode triggered by dragging a file into ORBIT.
    /// Automatically collapses when the cursor leaves ORBIT.
    /// </summary>
    Temporary,

    /// <summary>
    /// Pinned mode triggered by clicking the File Dock icon in Quick Actions.
    /// Remains open until explicitly dismissed or toggled.
    /// </summary>
    Pinned
}

