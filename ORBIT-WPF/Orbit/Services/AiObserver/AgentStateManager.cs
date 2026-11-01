using System.IO;
using Orbit.Core;
using Orbit.Models;
using Orbit.Services.DevWorkspace;

namespace Orbit.Services.AiObserver;

public interface IAgentStateManager : IDisposable
{
    AgentSessionInfo AntigravitySession { get; }
    AgentSessionInfo AntigravityIdeSession { get; }
    event Action<AgentSessionInfo, AgentSessionInfo>? StateUpdated;
    void Refresh();
    bool ActivateApplication(AntigravityAppType appType);
    void AcknowledgeCompletion(AntigravityAppType appType);
}

public sealed class AgentStateManager : IAgentStateManager
{
    private const string LogCategory = "AGENT_STATE_MGR";

    private readonly IAntigravityProcessMonitor _processMonitor;
    private readonly IAntigravityDatabaseObserver _dbObserver;
    private readonly IAntigravityTranscriptWatcher _transcriptWatcher;
    private readonly IAntigravitySessionResolver _sessionResolver;
    private readonly IIconExtractorService _iconExtractor;
    private readonly object _lock = new();

    private AgentSessionInfo _agSession;
    private AgentSessionInfo _ideSession;
    private readonly HashSet<string> _acknowledgedCompletions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public AgentSessionInfo AntigravitySession
    {
        get { lock (_lock) return _agSession; }
    }

    public AgentSessionInfo AntigravityIdeSession
    {
        get { lock (_lock) return _ideSession; }
    }

    public event Action<AgentSessionInfo, AgentSessionInfo>? StateUpdated;

    public AgentStateManager(
        IAntigravityProcessMonitor processMonitor,
        IAntigravityDatabaseObserver dbObserver,
        IAntigravityTranscriptWatcher transcriptWatcher,
        IAntigravitySessionResolver sessionResolver,
        IIconExtractorService iconExtractor)
    {
        _processMonitor = processMonitor;
        _dbObserver = dbObserver;
        _transcriptWatcher = transcriptWatcher;
        _sessionResolver = sessionResolver;
        _iconExtractor = iconExtractor;

        // Initialize baseline sessions
        string agExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\antigravity\Antigravity.exe");
        string ideExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Antigravity IDE\Antigravity IDE.exe");

        _agSession = new AgentSessionInfo
        {
            AppType = AntigravityAppType.Antigravity,
            AppIcon = _iconExtractor.GetOrExtractIcon(agExe),
            State = AgentState.Idle,
            StatusMessage = "No active task"
        };

        _ideSession = new AgentSessionInfo
        {
            AppType = AntigravityAppType.AntigravityIde,
            AppIcon = _iconExtractor.GetOrExtractIcon(ideExe),
            State = AgentState.Idle,
            StatusMessage = "No active task"
        };

        // Wire event triggers
        _processMonitor.ProcessesChanged += OnProcessesChanged;
        _dbObserver.DatabaseUpdated += OnDatabaseUpdated;

        // Run initial evaluation
        EvaluateStateInternal();
    }

    public void Refresh()
    {
        _processMonitor.CheckProcesses();
        _dbObserver.RefreshNow();
        EvaluateStateInternal();
    }

    public bool ActivateApplication(AntigravityAppType appType)
    {
        bool success = _processMonitor.ActivateApplication(appType);
        if (success)
        {
            AcknowledgeCompletion(appType);
        }
        return success;
    }

    public void AcknowledgeCompletion(AntigravityAppType appType)
    {
        lock (_lock)
        {
            string? cid = appType == AntigravityAppType.Antigravity ? _agSession.ConversationId : _ideSession.ConversationId;
            if (!string.IsNullOrWhiteSpace(cid))
            {
                _acknowledgedCompletions.Add(cid);
            }
        }
        EvaluateStateInternal();
    }

    private void OnProcessesChanged(bool agRunning, bool ideRunning)
    {
        EvaluateStateInternal();
    }

    private void OnDatabaseUpdated(IReadOnlyList<RawConversationSummary> summaries)
    {
        EvaluateStateInternal();
    }

    private void EvaluateStateInternal()
    {
        if (_disposed) return;

        bool agRunning = _processMonitor.IsAntigravityRunning;
        bool ideRunning = _processMonitor.IsAntigravityIdeRunning;
        var summaries = _dbObserver.GetLatestSummaries();

        AgentSessionInfo newAgSession;
        AgentSessionInfo newIdeSession;

        lock (_lock)
        {
            newAgSession = CloneSession(_agSession);
            newIdeSession = CloneSession(_ideSession);
        }

        newAgSession.IsProcessRunning = agRunning;
        newIdeSession.IsProcessRunning = ideRunning;

        // Categorize summaries by app without duplication
        RawConversationSummary? agSummary = null;
        RawConversationSummary? ideSummary = null;

        foreach (var s in summaries)
        {
            var appType = _sessionResolver.ResolveOriginatingApp(s, _processMonitor);
            if (appType == AntigravityAppType.Antigravity && agSummary == null && agRunning)
            {
                agSummary = s;
            }
            else if (appType == AntigravityAppType.AntigravityIde && ideSummary == null && ideRunning)
            {
                ideSummary = s;
            }

            if (agSummary != null && ideSummary != null) break;
        }

        // Apply updates to Antigravity
        UpdateSessionWithSummary(newAgSession, agSummary, agRunning);

        // Apply updates to Antigravity IDE
        UpdateSessionWithSummary(newIdeSession, ideSummary, ideRunning);

        lock (_lock)
        {
            _agSession = newAgSession;
            _ideSession = newIdeSession;
        }

        StateUpdated?.Invoke(newAgSession, newIdeSession);
    }

    private void UpdateSessionWithSummary(AgentSessionInfo session, RawConversationSummary? summary, bool isProcessRunning)
    {
        session.IsProcessRunning = isProcessRunning;

        if (!isProcessRunning || summary == null)
        {
            session.State = AgentState.Idle;
            session.StatusMessage = isProcessRunning ? "Idle" : "App not running";
            session.TaskTitle = string.Empty;
            session.WorkspaceName = string.Empty;
            session.WorkspacePath = string.Empty;
            session.ConversationId = string.Empty;
            session.LatestPrompt = string.Empty;
            session.LatestActivity = "Standing by";
            session.ModifiedFiles = [];
            session.ExecutedCommands = [];
            session.StepCount = 0;
            return;
        }

        session.ConversationId = summary.ConversationId;
        session.TaskTitle = !string.IsNullOrWhiteSpace(summary.Title) ? summary.Title : (!string.IsNullOrWhiteSpace(summary.Preview) ? summary.Preview : "Active Task");
        session.StepCount = summary.StepCount;
        session.LastModifiedTime = summary.LastModifiedTime;

        // Workspace
        if (summary.WorkspacePaths.Count > 0)
        {
            session.WorkspacePath = summary.WorkspacePaths[0];
            session.WorkspaceName = Path.GetFileName(summary.WorkspacePaths[0]);
        }
        else
        {
            session.WorkspacePath = string.Empty;
            session.WorkspaceName = "Global Workspace";
        }

        // Pull transcript snapshot
        var transcript = _transcriptWatcher.GetOrUpdateSnapshot(summary.ConversationId);
        session.LatestPrompt = !string.IsNullOrWhiteSpace(transcript.LatestPrompt) ? transcript.LatestPrompt : summary.Preview;
        session.LatestActivity = !string.IsNullOrWhiteSpace(transcript.LatestActivity) ? transcript.LatestActivity : "Standing by";
        session.RecentActivities = transcript.RecentActivities;
        session.ModifiedFiles = transcript.ModifiedFiles;
        session.ExecutedCommands = transcript.ExecutedCommands;

        // State detection
        bool isAcknowledged;
        lock (_lock)
        {
            isAcknowledged = _acknowledgedCompletions.Contains(summary.ConversationId);
        }

        bool isRunning = string.Equals(summary.Status, "CASCADE_RUN_STATUS_RUNNING", StringComparison.OrdinalIgnoreCase) || summary.NotFullyIdle;
        bool isIdle = string.Equals(summary.Status, "CASCADE_RUN_STATUS_IDLE", StringComparison.OrdinalIgnoreCase);
        bool isRecent = (DateTime.UtcNow - summary.LastModifiedTime).TotalMinutes < 5.0;

        if (summary.Killed)
        {
            session.State = AgentState.Cancelled;
            session.StatusMessage = "Cancelled";
        }
        else if (isRunning)
        {
            // Clear acknowledged state when a new run begins
            lock (_lock)
            {
                _acknowledgedCompletions.Remove(summary.ConversationId);
            }

            if (transcript.IsWaitingForApproval)
            {
                session.State = AgentState.Attention;
                session.StatusMessage = "Waiting for input";
            }
            else
            {
                session.State = AgentState.Working;
                session.StatusMessage = !string.IsNullOrWhiteSpace(transcript.LatestActivity) ? transcript.LatestActivity : "Processing task...";
            }
        }
        else if (transcript.IsWaitingForApproval && !isAcknowledged)
        {
            session.State = AgentState.Attention;
            session.StatusMessage = "Waiting for input";
        }
        else if (transcript.HasError && isRecent && !isAcknowledged)
        {
            session.State = AgentState.Error;
            session.StatusMessage = transcript.ErrorMessage ?? "Error encountered";
        }
        else if (isIdle)
        {
            // If recently completed and not yet acknowledged by user
            if (!isAcknowledged && isRecent)
            {
                session.State = AgentState.Completed; // DONE
                session.StatusMessage = "Task completed";
            }
            else
            {
                session.State = AgentState.Idle;
                session.StatusMessage = "Idle";
            }
        }
        else
        {
            session.State = AgentState.Idle;
            session.StatusMessage = "Idle";
        }
    }

    private static AgentSessionInfo CloneSession(AgentSessionInfo src)
    {
        return new AgentSessionInfo
        {
            AppType = src.AppType,
            AppIcon = src.AppIcon,
            ConversationId = src.ConversationId,
            TaskTitle = src.TaskTitle,
            WorkspaceName = src.WorkspaceName,
            WorkspacePath = src.WorkspacePath,
            LatestPrompt = src.LatestPrompt,
            State = src.State,
            StatusMessage = src.StatusMessage,
            LatestActivity = src.LatestActivity,
            StepCount = src.StepCount,
            LastModifiedTime = src.LastModifiedTime,
            RecentActivities = src.RecentActivities.ToList(),
            ModifiedFiles = src.ModifiedFiles.ToList(),
            ExecutedCommands = src.ExecutedCommands.ToList(),
            IsProcessRunning = src.IsProcessRunning
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processMonitor.ProcessesChanged -= OnProcessesChanged;
        _dbObserver.DatabaseUpdated -= OnDatabaseUpdated;
    }
}
