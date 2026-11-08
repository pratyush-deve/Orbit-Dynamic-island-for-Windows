using System;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Core;
using Orbit.Services.Notes;

namespace Orbit.Services.Commands.Handlers;

public sealed class NotesHandler : ICommandHandler
{
    private const string LogCategory = "NOTES_HANDLER";
    private readonly INotesService _notesService;

    public CommandType SupportedType => CommandType.TakeNote;

    public NotesHandler(INotesService notesService)
    {
        _notesService = notesService;
    }

    public async Task<CommandResult> ExecuteAsync(CommandIntent intent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent.Target))
        {
            return new CommandResult(false, "Note content cannot be empty", intent);
        }

        string noteText = intent.Target.Trim();
        OrbitLogger.Info(LogCategory, $"Appending voice note to Orbit Notes: '{noteText}'");

        try
        {
            // CRITICAL REQUIREMENT: Always append to existing notes with newline, never overwrite or clear!
            string existing = _notesService.LoadNotes();
            string updated = string.IsNullOrWhiteSpace(existing)
                ? noteText
                : $"{existing.TrimEnd()}\n{noteText}";

            await _notesService.SaveNotesAsync(updated);

            OrbitLogger.Info(LogCategory, "Voice note successfully appended to Orbit Notes.");
            return new CommandResult(true, "Note added to Orbit Notes", intent, noteText);
        }
        catch (Exception ex)
        {
            OrbitLogger.Error(LogCategory, "Failed to append voice note to Orbit Notes", ex);
            return new CommandResult(false, "Failed to save note", intent, ex.Message);
        }
    }
}

