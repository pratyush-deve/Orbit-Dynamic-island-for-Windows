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
    private readonly Orbit.Services.CodeGeneration.ICodeGenerationService _codeGenService;

    public CommandParser(AppRegistry appRegistry, Orbit.Services.CodeGeneration.ICodeGenerationService? codeGenService = null)
    {
        _appRegistry = appRegistry;
        _codeGenService = codeGenService ?? new Orbit.Services.CodeGeneration.CodeGenerationService();
    }

    public CommandIntent Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new CommandIntent(CommandType.Unknown, string.Empty, 0.0f);
        }

        string cleaned = CleanPunctuation(rawText);
        string normalized = NormalizeNumberWords(cleaned.ToLowerInvariant());
        string stripped = StripConversationalFillers(normalized);

        OrbitLogger.Info(LogCategory, $"Parsing raw: '{rawText}' -> normalized: '{normalized}' -> stripped: '{stripped}'");

        // 1. Screenshot
        if (IsScreenshotCommand(normalized) || IsScreenshotCommand(stripped))
        {
            return new CommandIntent(CommandType.Screenshot, rawText, 0.95f);
        }

        // 2. System Controls (Volume & Mute)
        var systemIntent = TryParseSystemControl(normalized, rawText) ?? TryParseSystemControl(stripped, rawText);
        if (systemIntent != null)
        {
            return systemIntent;
        }

        // 2b. System Metrics & Laptop Health (RAM, Battery, CPU, Status)
        var metricsIntent = TryParseSystemMetrics(normalized, rawText) ?? TryParseSystemMetrics(stripped, rawText);
        if (metricsIntent != null)
        {
            return metricsIntent;
        }

        // 3. Notes (Note taking)
        var noteIntent = TryParseNotes(normalized, rawText) ?? TryParseNotes(stripped, rawText);
        if (noteIntent != null)
        {
            return noteIntent;
        }

        // 3b. Code Generation & File Creation (Checked before File Search & Web Search)
        var codeGenIntent = TryParseCodeGeneration(normalized, rawText) ?? TryParseCodeGeneration(stripped, rawText);
        if (codeGenIntent != null)
        {
            return codeGenIntent;
        }

        // 4. Local File Search (Checked before Web Search to capture local storage targets)
        var fileSearchIntent = TryParseFileSearch(normalized, rawText) ?? TryParseFileSearch(stripped, rawText);
        if (fileSearchIntent != null)
        {
            return fileSearchIntent;
        }

        // 5. Targeted Website Search (Amazon, Myntra, Flipkart, YouTube, GitHub, Reddit, Wikipedia, unknown site)
        var websiteSearchIntent = TryParseWebsiteSearch(normalized, rawText) ?? TryParseWebsiteSearch(stripped, rawText);
        if (websiteSearchIntent != null)
        {
            return websiteSearchIntent;
        }

        // 6. Application Launch & Ambiguity Clarification
        var appIntent = TryParseAppLaunch(normalized, rawText);
        if (appIntent != null)
        {
            return appIntent;
        }

        // 7. General Web Search (Google, online lookup)
        var webSearchIntent = TryParseWebSearch(normalized, rawText);
        if (webSearchIntent != null)
        {
            return webSearchIntent;
        }

        OrbitLogger.Info(LogCategory, $"Unrecognized command: '{rawText}'");
        return new CommandIntent(CommandType.Unknown, rawText, 0.0f);
    }

    public static string StripConversationalFillers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string cleaned = CleanPunctuation(text);

        // Leading greetings and assistant wake names
        cleaned = Regex.Replace(cleaned, @"^(?:hey\s+orbit[, ]*|orbit[, ]*|hey[, ]*|hi[, ]*|hello[, ]*)", "", RegexOptions.IgnoreCase).Trim();

        // Leading polite, helper, and modal preambles
        cleaned = Regex.Replace(cleaned, @"^(?:can\s+you\s+(?:please\s+)?|could\s+you\s+(?:please\s+)?|would\s+you\s+(?:please\s+)?|will\s+you\s+(?:please\s+)?|i\s+want\s+you\s+to\s+|i'd\s+like\s+you\s+to\s+|please\s+|go\s+ahead\s+and\s+|just\s+)", "", RegexOptions.IgnoreCase).Trim();

        // Trailing conversational fillers
        cleaned = Regex.Replace(cleaned, @"\s+(?:for\s+me|right\s+now|now|please|bro|buddy|thanks|thank\s+you)$", "", RegexOptions.IgnoreCase).Trim();

        return CleanPunctuation(cleaned);
    }

    private static string CleanPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        char[] toTrim = ['.', '?', '!', ',', '"', '\'', '-', '—', '~', '`', '•'];
        trimmed = trimmed.Trim(toTrim).Trim();
        // Replace internal pause commas, tildes, and em-dashes produced by Whisper with spaces
        trimmed = Regex.Replace(trimmed, @"[,—~]+", " ");
        // Also replace standalone hyphens or bullet dashes surrounded by spaces (e.g. " - ")
        trimmed = Regex.Replace(trimmed, @"\s+[-—]\s+", " ");
        // Collapse multiple spaces
        return Regex.Replace(trimmed, @"\s+", " ").Trim();
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
        string text = normalized.Trim();

        // 1. Set volume: "set volume to 50", "set the volume to 50 percent", "turn volume to 50", "volume to 70", "volume 50%"
        var matchSet = Regex.Match(text, @"^(?:please\s+)?(?:set|change|turn|put)?\s*(?:the\s+|my\s+)?(?:volume|sound|audio)\s*(?:to|at)?\s*(\d+)\s*(?:percent|%)?$", RegexOptions.IgnoreCase);
        if (matchSet.Success && int.TryParse(matchSet.Groups[1].Value, out int targetVolume))
        {
            int clamped = Math.Clamp(targetVolume, 0, 100);
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.SetVolume, Value: clamped);
        }

        // 2. Increase volume: "increase volume", "increase the volume", "turn up the volume", "turn the volume up", "volume up", "louder"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:increase|turn\s+up|raise|boost)\s*(?:the\s+|my\s+)?(?:volume|sound|audio)$|^(?:please\s+)?turn\s+(?:the\s+|my\s+)?(?:volume|sound|audio)\s+up$|^(?:volume\s+up|louder|make\s+it\s+louder)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.IncreaseVolume, Value: 10);
        }

        var matchInc = Regex.Match(text, @"^(?:please\s+)?(?:increase|turn\s+up|raise)\s+(?:the\s+|my\s+)?(?:volume|sound|audio)\s+by\s+(\d+)\s*(?:percent|%)?$", RegexOptions.IgnoreCase);
        if (matchInc.Success && int.TryParse(matchInc.Groups[1].Value, out int incStep))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.IncreaseVolume, Value: Math.Clamp(incStep, 1, 100));
        }

        // 3. Decrease volume: "decrease volume", "decrease the volume", "turn down the volume", "turn the volume down", "volume down", "lower the volume", "quieter"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:decrease|turn\s+down|lower|reduce)\s*(?:the\s+|my\s+)?(?:volume|sound|audio)$|^(?:please\s+)?turn\s+(?:the\s+|my\s+)?(?:volume|sound|audio)\s+down$|^(?:volume\s+down|quieter|softer|make\s+it\s+quieter)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.DecreaseVolume, Value: 10);
        }

        var matchDec = Regex.Match(text, @"^(?:please\s+)?(?:decrease|turn\s+down|lower|reduce)\s+(?:the\s+|my\s+)?(?:volume|sound|audio)\s+by\s+(\d+)\s*(?:percent|%)?$", RegexOptions.IgnoreCase);
        if (matchDec.Success && int.TryParse(matchDec.Groups[1].Value, out int decStep))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.DecreaseVolume, Value: Math.Clamp(decStep, 1, 100));
        }

        // 4. Mute: "mute", "mute volume", "mute the volume", "mute sound", "mute laptop", "mute pc", "silence"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:mute|silence|be\s+quiet)(?:\s+(?:the\s+|my\s+)?(?:volume|sound|audio|laptop|pc))?$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.Mute);
        }

        // 5. Unmute: "unmute", "unmute volume", "unmute sound", "unmute audio", "unmute laptop"
        if (Regex.IsMatch(text, @"^(?:please\s+)?unmute(?:\s+(?:the\s+|my\s+)?(?:volume|sound|audio|laptop|pc))?$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemControl, rawText, 0.95f, ControlAction: SystemControlAction.Unmute);
        }

        return null;
    }

    private static CommandIntent? TryParseSystemMetrics(string normalized, string rawText)
    {
        string text = normalized.Trim();

        // 1. RAM / Memory check: "check my lappy ram", "check ram", "how much ram is used", "check my laptop ram", "check memory"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:check|how\s+much|what\s+is|show)(?:\s+(?:my\s+|the\s+)?(?:lappy|laptop|pc))?\s+(?:ram|memory)(?:\s+usage|\s+is\s+used)?$|^(?:ram\s+usage|memory\s+usage|check\s+ram)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemMetrics, rawText, 0.95f, MetricsSubtype: SystemMetricsSubtype.Ram);
        }

        // 2. Battery check: "check battery", "battery status", "how much battery", "check my battery percentage"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:check|how\s+much|what\s+is|show)(?:\s+(?:my\s+|the\s+)?(?:lappy|laptop|pc))?\s+battery(?:\s+percentage|\s+status|\s+left)?$|^(?:battery\s+status|battery\s+percentage|check\s+battery)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemMetrics, rawText, 0.95f, MetricsSubtype: SystemMetricsSubtype.Battery);
        }

        // 3. CPU check: "check cpu", "cpu usage", "check processor"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:check|how\s+much|what\s+is|show)(?:\s+(?:my\s+|the\s+)?(?:lappy|laptop|pc))?\s+(?:cpu|processor)(?:\s+usage)?$|^(?:cpu\s+usage|check\s+cpu)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemMetrics, rawText, 0.95f, MetricsSubtype: SystemMetricsSubtype.Cpu);
        }

        // 4. Laptop health / general status: "check my laptop health", "laptop status", "how is my laptop", "check system health", "check laptop status", "check pc health"
        if (Regex.IsMatch(text, @"^(?:please\s+)?(?:check|how\s+is|what\s+is|show)\s+(?:my\s+)?(?:lappy|laptop|pc|system)\s+(?:health|status|performance)$|^(?:laptop\s+status|system\s+status|laptop\s+health|system\s+health)$", RegexOptions.IgnoreCase))
        {
            return new CommandIntent(CommandType.SystemMetrics, rawText, 0.95f, MetricsSubtype: SystemMetricsSubtype.All);
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

    private CommandIntent? TryParseCodeGeneration(string candidate, string rawText)
    {
        // 1. Must start with an action verb (create, write, generate, make, build, save)
        if (!Regex.IsMatch(candidate, @"^(?:please\s+)?(?:create|write|generate|make|build|save)\b", RegexOptions.IgnoreCase))
        {
            return null;
        }

        // 2. Filter out conversational questions (e.g. "how do I write a program", "explain quicksort in python")
        if (Regex.IsMatch(candidate, @"^(?:how|why|what|when|where|explain|show)\b", RegexOptions.IgnoreCase))
        {
            return null;
        }

        // 3. Must contain code keywords or programming language references
        bool hasCodeKeyword = Regex.IsMatch(
            candidate,
            @"\b(?:program|script|code|file)\b|(?:\bc\+\+|\bcpp\b|\bc#|\bcsharp\b|\bpython\b|\bpy\b|\bjava\b|\bkotlin\b|\bjavascript\b|\bjs\b|\btypescript\b|\bts\b|\brust\b|\bgo\b|\bc\s+file|\bc\s+program|\bc\s+script)",
            RegexOptions.IgnoreCase);

        if (!hasCodeKeyword)
        {
            return null;
        }

        // Guard: Do not capture "create a note" or "write a note"
        if (Regex.IsMatch(candidate, @"\b(?:note|reminder)\b", RegexOptions.IgnoreCase))
        {
            return null;
        }

        // Parse Language
        var lang = _codeGenService.ParseLanguage(candidate, out string? unsupportedName);

        // Parse Destination (Desktop, Documents, or subdirectories)
        string rootBase = "Desktop";
        if (Regex.IsMatch(candidate, @"\b(?:documents|docs)\b", RegexOptions.IgnoreCase))
        {
            rootBase = "Documents";
        }

        string? subFolder = null;

        // 1. "in a folder named as test", "inside folder named test", "in folder called test"
        var matchNamedFolder = Regex.Match(candidate, @"(?:in|inside)\s+(?:(?:a|the)\s+)?folder\s+(?:named|called)(?:\s+as)?\s+([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (matchNamedFolder.Success)
        {
            subFolder = matchNamedFolder.Groups[1].Value.Trim();
        }
        else
        {
            // 2. "in folder test", "inside folder college"
            var matchFolderDirect = Regex.Match(candidate, @"(?:in|inside)\s+(?:(?:a|the)\s+)?folder\s+([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase);
            if (matchFolderDirect.Success &&
                !matchFolderDirect.Groups[1].Value.Trim().Equals("desktop", StringComparison.OrdinalIgnoreCase) &&
                !matchFolderDirect.Groups[1].Value.Trim().Equals("documents", StringComparison.OrdinalIgnoreCase))
            {
                subFolder = matchFolderDirect.Groups[1].Value.Trim();
            }
            else
            {
                // 3. "in the test folder", "in test folder", "inside my college folder"
                var matchFolderTrailing = Regex.Match(candidate, @"(?:in|inside)\s+(?:the\s+|my\s+|a\s+)?([a-zA-Z0-9_\-]+)\s+folder\b", RegexOptions.IgnoreCase);
                if (matchFolderTrailing.Success &&
                    !matchFolderTrailing.Groups[1].Value.Trim().Equals("desktop", StringComparison.OrdinalIgnoreCase) &&
                    !matchFolderTrailing.Groups[1].Value.Trim().Equals("documents", StringComparison.OrdinalIgnoreCase))
                {
                    subFolder = matchFolderTrailing.Groups[1].Value.Trim();
                }
            }
        }

        string destination = !string.IsNullOrEmpty(subFolder) ? $"{rootBase}/{subFolder}" : rootBase;

        // Parse explicit filename if provided
        string? explicitFileName = null;
        var matchFileName = Regex.Match(candidate, @"(?:save(?:\s+it)?\s+as|named|name\s+it)\s+([a-zA-Z0-9_\-\.]+\.(?:c|py))\b", RegexOptions.IgnoreCase);
        if (matchFileName.Success)
        {
            explicitFileName = matchFileName.Groups[1].Value.Trim();
        }

        // Parse Purpose
        string purpose = ExtractPurpose(candidate);

        var codeGenData = new Orbit.Services.CodeGeneration.CodeGenerationIntentData(
            Language: lang,
            Purpose: purpose,
            DestinationFolder: destination,
            ProposedFileName: explicitFileName,
            UnsupportedLanguageName: unsupportedName,
            Stage: Orbit.Services.CodeGeneration.CodeGenerationStage.Initial
        );

        return new CommandIntent(
            Type: CommandType.GenerateCode,
            RawText: rawText,
            Confidence: 0.95f,
            Target: explicitFileName ?? purpose,
            Scope: destination,
            Query: purpose,
            CodeGenData: codeGenData
        );
    }

    private static string ExtractPurpose(string candidate)
    {
        string text = candidate.Trim();

        // 1. Strip leading trigger + language + program/script/code/file
        text = Regex.Replace(
            text,
            @"^(?:please\s+)?(?:create|write|generate|make|build|save)\s+(?:a\s+)?(?:c\+\+|cpp|c#|csharp|c|python|py|java|kotlin|javascript|js|typescript|ts|rust|go)?\s*(?:program|script|code|file)?\s*",
            "",
            RegexOptions.IgnoreCase);

        // 2. Strip root destination references ("in/on desktop", "in documents")
        text = Regex.Replace(text, @"\b(?:on|in|into|to)\s+(?:my\s+|the\s+)?(?:desktop|documents|docs)\b", "", RegexOptions.IgnoreCase);

        // 3. Strip folder destination clauses
        text = Regex.Replace(text, @"\b(?:in|inside)\s+(?:(?:a|the)\s+)?folder\s+(?:named|called)(?:\s+as)?\s+[a-zA-Z0-9_\-]+", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?:in|inside)\s+(?:(?:a|the)\s+)?folder\s+[a-zA-Z0-9_\-]+", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?:in|inside)\s+(?:the\s+|my\s+|a\s+)?[a-zA-Z0-9_\-]+\s+folder\b", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\band\s+save(?:\s+it)?\b", "", RegexOptions.IgnoreCase);

        // 4. Strip punctuation and bridge preambles at the start of purpose
        text = Regex.Replace(text, @"^[,\.\s;:\-]+", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^(?:the\s+program\s+should|the\s+script\s+should|it\s+should|program\s+should|script\s+should|should|to|for|that|which)\s*", "", RegexOptions.IgnoreCase);

        // 5. Clean punctuation at boundaries and collapse multiple spaces
        text = Regex.Replace(text, @"^[,\.\s;:\-]+|[,\.\s;:\-]+$", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Strip explicit filename clauses
        text = Regex.Replace(text, @"(?:save(?:\s+it)?\s+as|named|name\s+it)\s+[a-zA-Z0-9_\-\.]+\.(?:c|py).*", "", RegexOptions.IgnoreCase);

        text = text.Trim();
        return string.IsNullOrWhiteSpace(text) ? "requested program" : text;
    }

    private static CommandIntent? TryParseFileSearch(string normalized, string rawText)
    {
        string candidate = StripConversationalFillers(rawText);

        // Guardrail: Explicit web search queries with local words (e.g. "Search the web for desktop wallpapers", "Search Google for download speed")
        if (Regex.IsMatch(candidate, @"^(?:please\s+)?(?:search\s+(?:the\s+)?web|search\s+google|google\s+search|search\s+.+?\s+(?:in|on)\s+google)\b", RegexOptions.IgnoreCase))
        {
            return null;
        }

        // 0. Explicit in-folder search:
        // "Open File Explorer and search for college files on my desktop"
        // "Open File Explorer and search for random college files folder in my desktop"
        // "Find college files on my desktop"
        // "Find invoice in downloads"
        var matchInFolder = Regex.Match(candidate, @"^(?:please\s+)?(?:open\s+(?:file\s+)?explorer\s+and\s+)?(?:find|search(?:\s+for)?)\s+(.+?)\s+(?:in|on)\s+(?:my\s+|the\s+)?(desktop|downloads|documents|pictures|music|videos|[a-zA-Z]\s+drive|[a-zA-Z]:)$", RegexOptions.IgnoreCase);
        if (matchInFolder.Success)
        {
            string query = matchInFolder.Groups[1].Value.Trim();
            string folder = matchInFolder.Groups[2].Value.Trim().ToLowerInvariant();
            if (query.EndsWith(" files", StringComparison.OrdinalIgnoreCase))
            {
                query = query[..^6].Trim();
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: query, Scope: folder, Query: query);
            }
        }

        // 1. "Search desktop for random college files", "Search Downloads for PDFs", "Search my D drive for Android projects"
        var matchFolderSearch = Regex.Match(candidate, @"^(?:please\s+)?(?:open\s+(?:file\s+)?explorer\s+and\s+)?search\s+(?:my\s+|the\s+)?(desktop|downloads|documents|pictures|music|videos|[a-zA-Z]\s+drive|[a-zA-Z]:)\s+(?:for\s+)(.+)$", RegexOptions.IgnoreCase);
        if (matchFolderSearch.Success)
        {
            string folder = matchFolderSearch.Groups[1].Value.Trim().ToLowerInvariant();
            string query = matchFolderSearch.Groups[2].Value.Trim();
            if (query.EndsWith(" files", StringComparison.OrdinalIgnoreCase))
            {
                query = query[..^6].Trim();
            }
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: query, Scope: folder, Query: query);
            }
        }

        // 2. "Find the folder containing my project", "Find folder named Android Studio"
        var matchFolderNamed = Regex.Match(candidate, @"^(?:please\s+)?find\s+(?:the\s+)?folder\s+(?:containing\s+|named\s+|called\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchFolderNamed.Success)
        {
            string folderName = matchFolderNamed.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: $"folder {folderName}", Scope: "desktop", Query: folderName);
            }
        }

        // 3. "Find files named project report", "Search for files named...", "Find files called..."
        var matchNamed = Regex.Match(candidate, @"^(?:please\s+)?(?:search\s+for\s+|find\s+)?files\s+(?:named|called)\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchNamed.Success)
        {
            string query = matchNamed.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.95f, Target: query, Query: query);
            }
        }

        // 4. "Find desktop wallpaper files on my laptop" (local files on laptop)
        var matchLaptopFiles = Regex.Match(candidate, @"^(?:please\s+)?(?:find|search(?:\s+for)?)\s+(.+?)\s+(?:files\s+)?on\s+(?:my\s+)?(?:laptop|computer|pc)$", RegexOptions.IgnoreCase);
        if (matchLaptopFiles.Success)
        {
            string query = matchLaptopFiles.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query, Query: query);
            }
        }

        // 5. "Find Android Studio files" -> "find (.+) files"
        var matchFiles = Regex.Match(candidate, @"^(?:please\s+)?find\s+(.+?)\s+files$", RegexOptions.IgnoreCase);
        if (matchFiles.Success)
        {
            string query = matchFiles.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query, Query: query);
            }
        }

        // 6. "Search my/this laptop/computer/pc/drive for PDF files", "Search local files for...", "Search files for..."
        var matchDevice = Regex.Match(candidate, @"^(?:please\s+)?search\s+(?:my\s+|this\s+|local\s+)?(?:laptop|computer|pc|local\s+files|files)\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchDevice.Success)
        {
            string query = matchDevice.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query, Query: query);
            }
        }

        // 7. "Find file <name>" or "Find the file <name>"
        var matchSingleFile = Regex.Match(candidate, @"^(?:please\s+)?find\s+(?:the\s+)?file\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchSingleFile.Success)
        {
            string query = matchSingleFile.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.FileSearch, rawText, 0.90f, Target: query, Query: query);
            }
        }

        return null;
    }

    private static CommandIntent? TryParseWebsiteSearch(string normalized, string rawText)
    {
        string candidate = StripConversationalFillers(rawText);

        if (SiteSearchRegistry.TryExtractSiteSearch(candidate, out string targetSite, out string query))
        {
            return new CommandIntent(
                CommandType.WebsiteSearch,
                rawText,
                0.95f,
                Target: query,
                Scope: targetSite,
                Query: query
            );
        }

        return null;
    }

    private static CommandIntent? TryParseWebSearch(string normalized, string rawText)
    {
        string candidate = StripConversationalFillers(rawText);

        // 0. Explicit "search <query> in/on google", "search for <query> in/on google", "look up <query> in/on google", "find <query> in/on google"
        var matchInGoogle = Regex.Match(candidate, @"^(?:please\s+)?(?:search|find|look\s+up)(?:\s+for)?\s+(.+?)\s+(?:in|on)\s+google$", RegexOptions.IgnoreCase);
        if (matchInGoogle.Success)
        {
            string query = matchInGoogle.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query, Scope: "google", Query: query);
            }
        }

        // Explicit Google search: "Google search for Android Studio errors", "Search Google for..."
        var matchGoogleExplicit = Regex.Match(candidate, @"^(?:please\s+)?(?:google\s+search\s+(?:for\s+)?|search\s+google\s+(?:for\s+)?)(.+)$", RegexOptions.IgnoreCase);
        if (matchGoogleExplicit.Success)
        {
            string query = matchGoogleExplicit.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query, Scope: "google", Query: query);
            }
        }

        // 1. Compound search phrases targeting browser explicitly: "Open Google and search for wifi speed test", "Open Chrome and search..."
        var matchCompound = Regex.Match(candidate, @"^(?:please\s+)?(?:open|launch|start|go\s+to)\s+(?:google|chrome|google\s+chrome|browser|the\s+browser|the\s+web|the\s+internet)\s+(?:and\s+)?(?:search|look\s+up)(?:\s+for)?\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchCompound.Success)
        {
            string query = matchCompound.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query, Scope: "google", Query: query);
            }
        }

        // 2. "Search the web for...", "Search web for..."
        var matchWeb = Regex.Match(candidate, @"^(?:please\s+)?search\s+(?:the\s+)?web\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchWeb.Success)
        {
            string query = matchWeb.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.95f, Target: query, Scope: "google", Query: query);
            }
        }

        // 3. "Look up Whisper.cpp on Google", "Look up Whisper.cpp online"
        var matchLookup = Regex.Match(candidate, @"^(?:please\s+)?look\s+up\s+(.+?)(?:\s+on\s+google|\s+online)?$", RegexOptions.IgnoreCase);
        if (matchLookup.Success)
        {
            string query = matchLookup.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.90f, Target: query, Scope: "google", Query: query);
            }
        }

        // 4. "Google Kotlin Jetpack Compose", "Google Android Studio errors"
        var matchGoogleDirect = Regex.Match(candidate, @"^(?:please\s+)?google\s+(.+)$", RegexOptions.IgnoreCase);
        if (matchGoogleDirect.Success)
        {
            string query = matchGoogleDirect.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.90f, Target: query, Scope: "google", Query: query);
            }
        }

        // 5. General search phrases: "Search for Android Studio errors", "Search for Wi-Fi speed test"
        var matchSearchGeneral = Regex.Match(candidate, @"^(?:please\s+)?search\s+(?:for\s+)?(.+)$", RegexOptions.IgnoreCase);
        if (matchSearchGeneral.Success)
        {
            string query = matchSearchGeneral.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                return new CommandIntent(CommandType.WebSearch, rawText, 0.90f, Target: query, Scope: "google", Query: query);
            }
        }

        return null;
    }

    private CommandIntent? TryParseAppLaunch(string normalized, string rawText)
    {
        string candidate = StripConversationalFillers(rawText);

        // Check for compound multi-action: e.g. "Search Myntra and then open Chrome"
        if (Regex.IsMatch(candidate, @"\band\s+(?:then\s+)?(?:open|launch)\b", RegexOptions.IgnoreCase))
        {
            // Ambiguous compound command requiring clarification
            return new CommandIntent(
                CommandType.Clarification,
                rawText,
                0.50f,
                Target: "CompoundAction",
                ClarificationPrompt: "Would you like me to search Myntra or open Chrome first?"
            );
        }

        // Ambiguous standalone app keyword: e.g. "Studio" or "Open Studio"
        if (Regex.IsMatch(candidate, @"^(?:please\s+)?(?:open\s+|launch\s+|start\s+)?studio$", RegexOptions.IgnoreCase))
        {
            var pending = new CommandIntent(CommandType.LaunchApp, rawText, 0.95f, Target: "Android Studio");
            return new CommandIntent(
                CommandType.Clarification,
                rawText,
                0.50f,
                Target: "Android Studio",
                PendingAction: pending,
                ClarificationPrompt: "Did you mean Android Studio?"
            );
        }

        // Ambiguous phonetic misinterpretation: e.g. "Operate staff", "Open staff"
        if (Regex.IsMatch(candidate, @"^(?:please\s+)?(?:open|launch|start|run|operate)\s+(?:staff|stuff)$", RegexOptions.IgnoreCase))
        {
            var pending = new CommandIntent(CommandType.LaunchApp, rawText, 0.95f, Target: "Android Studio");
            return new CommandIntent(
                CommandType.Clarification,
                rawText,
                0.50f,
                Target: "Android Studio",
                PendingAction: pending,
                ClarificationPrompt: "Did you mean Android Studio?"
            );
        }

        // "open Android Studio", "launch Google Chrome", "start Notepad", "operate studio", "open my mails"
        var match = Regex.Match(candidate, @"^(?:please\s+)?(?:open|launch|start|run|operate)\s+(.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string appName = match.Groups[1].Value.Trim();
            if (appName.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) appName = appName[4..].Trim();
            if (appName.StartsWith("my ", StringComparison.OrdinalIgnoreCase)) appName = appName[3..].Trim();

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
                    appName.IndexOf(" search for ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    appName.IndexOf(" search ", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return null;
                }

                // Direct recognition (high confidence)
                if (_appRegistry.IsRecognizedApp(appName))
                {
                    return new CommandIntent(CommandType.LaunchApp, rawText, 0.95f, Target: appName);
                }

                // Intelligent semantic/fuzzy candidate suggestion (e.g. "messenger app" -> WhatsApp, fuzzy name)
                var candidateTarget = _appRegistry.SuggestCandidate(appName);
                if (candidateTarget != null)
                {
                    var pending = new CommandIntent(CommandType.LaunchApp, rawText, 0.95f, Target: candidateTarget.DisplayName);
                    return new CommandIntent(
                        CommandType.Clarification,
                        rawText,
                        0.75f,
                        Target: candidateTarget.DisplayName,
                        PendingAction: pending,
                        ClarificationPrompt: $"Did you mean {candidateTarget.DisplayName}?"
                    );
                }

                // Completely unrecognized app (e.g. misheard transcription like "same data"):
                // Assign low confidence (0.40f) so it does not trigger a blind deterministic execution
                return new CommandIntent(CommandType.LaunchApp, rawText, 0.40f, Target: appName);
            }
        }

        return null;
    }
}

