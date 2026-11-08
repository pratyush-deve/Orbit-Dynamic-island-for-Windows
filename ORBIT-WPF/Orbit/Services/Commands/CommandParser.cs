using System;
using System.Text.RegularExpressions;
using Orbit.Core;

namespace Orbit.Services.Commands;

/// <summary>
/// Deterministic rule-based command parser with offline regex matching,
/// number normalization, and confidence scoring.
/// </summary>
public sealed class CommandParser : ICommandParser
{
    private const string LogCategory = "COMMAND_PARSER";
    private readonly AppRegistry _appRegistry;

    public CommandParser(AppRegistry appRegistry)
    {
        _appRegistry = appRegistry;
    }

    public CommandIntent Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new CommandIntent(CommandType.Unknown, string.Empty, 0.0f);
        }

        string cleaned = CleanPunctuation(rawText);
        string normalized = NormalizeNumberWords(cleaned.ToLowerInvariant());

        OrbitLogger.Info(LogCategory, $"Parsing raw: '{rawText}' -> normalized: '{normalized}'");

        // 1. Screenshot
        if (IsScreenshotCommand(normalized))
        {
            return new CommandIntent(CommandType.Screenshot, rawText, 0.95f);
        }

        // 2. System Controls (Volume & Mute)
        var systemIntent = TryParseSystemControl(normalized, rawText);
        if (systemIntent != null)
        {
            return systemIntent;
        }

        // 3. Notes (Note taking)
        var noteIntent = TryParseNotes(normalized, rawText);
        if (noteIntent != null)
        {
            return noteIntent;
        }

        // 4. Local File Search (Checked before Web Search to capture "Find ... files")
        var fileSearchIntent = TryParseFileSearch(normalized, rawText);
        if (fileSearchIntent != null)
        {
            return fileSearchIntent;
        }

        // 5. Web Search
        var webSearchIntent = TryParseWebSearch(normalized, rawText);
        if (webSearchIntent != null)
        {
            return webSearchIntent;
        }

        // 6. Application Launch
        var appIntent = TryParseAppLaunch(normalized, rawText);
        if (appIntent != null)
        {
            return appIntent;
        }

        OrbitLogger.Info(LogCategory, $"Unrecognized command: '{rawText}'");
        return new CommandIntent(CommandType.Unknown, rawText, 0.0f);
    }

    private static string CleanPunctuation(string text)
    {
        var trimmed = text.Trim();
        char[] toTrim = ['.', '?', '!', ',', '"', '\'', ';', ':'];
        return trimmed.Trim(toTrim).Trim();
    }

    private static string NormalizeNumberWords(string text)
    {
        // Replace number words with digits for volume matching
        var map = new (string Word, string Digit)[]
        {
            ("one hundred", "100"),
            ("hundred", "100"),
            ("ninety", "90"),
            ("eighty", "80"),
            ("seventy", "70"),
            ("sixty", "60"),
            ("fifty five", "55"),
            ("fifty", "50"),
            ("forty five", "45"),
            ("forty", "40"),
            ("thirty five", "35"),
            ("thirty", "30"),
            ("twenty five", "25"),
            ("twenty", "20"),
            ("fifteen", "15"),
            ("ten", "10"),
            ("zero", "0"),
            ("one", "1"),
            ("two", "2"),
            ("three", "3"),
            ("four", "4"),
            ("five", "5"),
            ("six", "6"),
            ("seven", "7"),
            ("eight", "8"),
            ("nine", "9")
        };

        string result = text;
        foreach (var (word, digit) in map)
        {
            result = Regex.Replace(result, $@"\b{word}\b", digit, RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static bool IsScreenshotCommand(string normalized)
    {
        return Regex.IsMatch(normalized, @"^(please\s+)?(take\s+(a\s+)?screenshot|capture\s+(my\s+)?screen|take\s+screen\s*shot|screenshot)$", RegexOptions.IgnoreCase);
    }

    private static CommandIntent? TryParseSystemControl(string normalized, string rawText)
    {
        // Set volume with explicit percent or number: "set volume to 50 percent", "set volume 50%", "volume to 70 percent"
        var matchSet = Regex.Match(normalized, @"^(?:please\s+)?(?:set|change)?\s*volume\s*(?:to)?\s*(\d+)\s*(?:percent|%)?$", RegexOptions.IgnoreCase);
        if (matchSet.Success && int.TryParse(matchSet.Groups[1].Value, out int targetVolume))
        {
            int clamped = Math.Clamp(targetVolume, 0, 100);
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.SetVolume, Value: clamped);
        }

        // Increase volume
        if (Regex.IsMatch(normalized, @"^(?:please\s+)?(?:increase\s+volume|turn\s+up\s+(?:the\s+)?volume|volume\s+up|boost\s+volume)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.IncreaseVolume, Value: 10);
        }

        var matchInc = Regex.Match(normalized, @"^(?:please\s+)?increase\s+volume\s+by\s+(\d+)\s*(?:percent|%)?$", RegexOptions.IgnoreCase);
        if (matchInc.Success && int.TryParse(matchInc.Groups[1].Value, out int incStep))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.IncreaseVolume, Value: Math.Clamp(incStep, 1, 100));
        }

        // Decrease volume
        if (Regex.IsMatch(normalized, @"^(?:please\s+)?(?:decrease\s+volume|turn\s+down\s+(?:the\s+)?volume|volume\s+down|lower\s+(?:the\s+)?volume)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.DecreaseVolume, Value: 10);
        }

        var matchDec = Regex.Match(normalized, @"^(?:please\s+)?decrease\s+volume\s+by\s+(\d+)\s*(?:percent|%)?$", RegexOptions.IgnoreCase);
        if (matchDec.Success && int.TryParse(matchDec.Groups[1].Value, out int decStep))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.DecreaseVolume, Value: Math.Clamp(decStep, 1, 100));
        }

        // Mute
        if (Regex.IsMatch(normalized, @"^(?:please\s+)?(?:mute|mute\s+volume|mute\s+sound|mute\s+audio)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.Mute);
        }

        // Unmute
        if (Regex.IsMatch(normalized, @"^(?:please\s+)?(?:unmute|unmute\s+volume|unmute\s+sound|unmute\s+audio)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.Unmute);
        }

        return null;
    }

    private static CommandIntent? TryParseNotes(string normalized, string rawText)
    {
        // "create a note saying finish my project", "take a note that...", "take a note: buy groceries", "add note saying..."
        var match = Regex.Match(rawText, @"^(?:please\s+)?(?:create\s+(?:a\s+)?note\s+(?:saying|that)|take\s+(?:a\s+)?note(?:\s+that|:)?|add\s+(?:a\s+)?note\s+(?:saying|that)|add\s+(?:a\s+)?note:?|note\s+down(?:\s+that|:)?|write\s+(?:a\s+)?note\s+(?:saying|that)|new\s+note:?)\s+(.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string noteContent = match.Groups[1].Value.Trim();
            if (noteContent.StartsWith("that ", StringComparison.OrdinalIgnoreCase))
            {
                noteContent = noteContent[5..].Trim();
            }

            if (!string.IsNullOrWhiteSpace(noteContent))
            {
                return new CommandIntent(CommandType.TakeNote, rawText, 0.95f, Target: noteContent);
            }
        }

        return null;
    }

    private static CommandIntent? TryParseFileSearch(string normalized, string rawText)
    {
        // 0. Explicit in-folder search: "Find random college files in my desktop", "Find invoice in downloads", "Search for photos in pictures"
        var matchInFolder = Regex.Match(rawText, @"^(?:please\s+)?(?:find|search(?:\s+for)?)\s+(.+?)(?:\s+files)?\s+in\s+(?:my\s+|the\s+)?(desktop|downloads|documents|pictures|music|videos|c\s+drive|d\s+drive|[a-zA-Z0-9_\-]+)$", RegexOptions.IgnoreCase);
        if (matchInFolder.Success)
        {
            string query = matchInFolder.Groups[1].Value.Trim();
            string folder = matchInFolder.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: query, Scope: folder);
            }
        }

        // "Search desktop for random college files", "Search downloads for invoice"
        var matchFolderSearch = Regex.Match(rawText, @"^(?:please\s+)?search\s+(?:my\s+|the\s+)?(desktop|downloads|documents|pictures|music|videos|c\s+drive|d\s+drive)\s+(?:for\s+)(.+)$", RegexOptions.IgnoreCase);
        if (matchFolderSearch.Success)
        {
            string folder = matchFolderSearch.Groups[1].Value.Trim();
            string query = matchFolderSearch.Groups[2].Value.Trim();
            if (query.EndsWith(" files", StringComparison.OrdinalIgnoreCase))
            {
                query = query[..^6].Trim();
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: query, Scope: folder);
            }
        }

        // 1. "Find files named project report", "Search for files named...", "Find files called..."
        var matchNamed = Regex.Match(rawText, @"^(?:please\s+)?(?:search\s+for\s+|find\s+)?files\s+(?:named|called)\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchNamed.Success)
        {
            string query = matchNamed.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: query);
            }
        }

        // 2. "Find Android Studio files" -> "find (.+) files"
        var matchFiles = Regex.Match(rawText, @"^(?:please\s+)?find\s+(.+?)\s+files$", RegexOptions.IgnoreCase);
        if (matchFiles.Success)
        {
            string query = matchFiles.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query);
            }
        }

        // 3. "Search my/this laptop/computer/pc/drive for PDF files", "Search local files for...", "Search files for..."
        var matchDevice = Regex.Match(rawText, @"^(?:please\s+)?search\s+(?:my\s+|this\s+|local\s+)?(?:laptop|computer|pc|drive|local\s+files|files)\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchDevice.Success)
        {
            string query = matchDevice.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query);
            }
        }

        // 4. "Find file <name>" or "Find the file <name>"
        var matchSingleFile = Regex.Match(rawText, @"^(?:please\s+)?find\s+(?:the\s+)?file\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchSingleFile.Success)
        {
            string query = matchSingleFile.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query);
            }
        }

        return null;
    }

    private static CommandIntent? TryParseWebSearch(string normalized, string rawText)
    {
        // 0. Compound search phrases: "Open Google and search for wifi speed test", "Open Chrome and search...", "Go to Google and search for..."
        var matchCompound = Regex.Match(rawText, @"^(?:please\s+)?(?:open|launch|start|go\s+to)\s+(?:google|chrome|google\s+chrome|browser|the\s+browser|the\s+web|the\s+internet)\s+(?:and\s+)?(?:search|look\s+up)(?:\s+for)?\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchCompound.Success)
        {
            string query = matchCompound.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query);
            }
        }

        // Generic "... and search for <query>" or "... and search <query>"
        var matchAndSearch = Regex.Match(rawText, @"^(?:.+?)\s+and\s+search\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchAndSearch.Success)
        {
            string query = matchAndSearch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query);
            }
        }

        // "Search Google for Kotlin Jetpack Compose", "Search Google for..."
        var matchGoogle = Regex.Match(rawText, @"^(?:please\s+)?search\s+google\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchGoogle.Success)
        {
            string query = matchGoogle.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query);
            }
        }

        // "Search the web for...", "Search web for..."
        var matchWeb = Regex.Match(rawText, @"^(?:please\s+)?search\s+(?:the\s+)?web\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchWeb.Success)
        {
            string query = matchWeb.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query);
            }
        }

        // "Look up Whisper.cpp on Google", "Look up Whisper.cpp"
        var matchLookup = Regex.Match(rawText, @"^(?:please\s+)?look\s+up\s+(.+?)(?:\s+on\s+google|\s+online)?$", RegexOptions.IgnoreCase);
        if (matchLookup.Success)
        {
            string query = matchLookup.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.90f, Target: query);
            }
        }

        // "Google Kotlin Jetpack Compose"
        var matchGoogleDirect = Regex.Match(rawText, @"^(?:please\s+)?google\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchGoogleDirect.Success)
        {
            string query = matchGoogleDirect.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.90f, Target: query);
            }
        }

        // "Search for Wi-Fi speed test", "Search for...", "Search Wi-Fi speed test"
        var matchSearchGeneral = Regex.Match(rawText, @"^(?:please\s+)?search\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchSearchGeneral.Success)
        {
            string query = matchSearchGeneral.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.90f, Target: query);
            }
        }

        return null;
    }

    private CommandIntent? TryParseAppLaunch(string normalized, string rawText)
    {
        // "open Android Studio", "launch Google Chrome", "start Notepad"
        var match = Regex.Match(rawText, @"^(?:please\s+)?(?:open|launch|start|run)\s+(.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string appName = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(appName))
            {
                // Disallow arbitrary executable extensions or command injection characters
                if (appName.Contains(';') || appName.Contains('&') || appName.Contains('|') || appName.Contains('>') || appName.Contains('<'))
                {
                    OrbitLogger.Warn(LogCategory, $"Rejected app launch request with suspicious shell characters: '{appName}'");
                    return null;
                }

                // If this is actually a search conjunction like "Google and search for...", reject from app launch
                if (appName.IndexOf(" and search", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    appName.IndexOf(" search for ", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return null;
                }

                float confidence = _appRegistry.IsRecognizedApp(appName) ? 0.95f : 0.70f;
                return new CommandIntent(CommandType.LaunchApp, rawText, confidence, Target: appName);
            }
        }

        return null;
    }
}

