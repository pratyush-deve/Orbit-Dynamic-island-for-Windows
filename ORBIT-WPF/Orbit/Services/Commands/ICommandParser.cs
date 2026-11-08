namespace Orbit.Services.Commands;

/// <summary>
/// Deterministic offline voice command parser.
/// Converts recognized speech text into structured command intents.
/// </summary>
public interface ICommandParser
{
    CommandIntent Parse(string rawText);
}

