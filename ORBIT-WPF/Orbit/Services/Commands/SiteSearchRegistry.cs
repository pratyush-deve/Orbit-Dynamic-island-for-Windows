using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Orbit.Services.Commands;

public record SiteSearchProvider(
    string Key,
    string DisplayName,
    string[] Aliases,
    string Domain,
    string? DirectSearchUrlPattern = null
);

/// <summary>
/// Central registry and URL builder for in-site searches across supported platforms
/// (Amazon, Myntra, Flipkart, YouTube, GitHub, Reddit, Wikipedia) with safe Google site: fallback for unknown domains.
/// </summary>
public static class SiteSearchRegistry
{
    private static readonly List<SiteSearchProvider> Providers =
    [
        new("amazon", "Amazon India", ["amazon", "amazon india", "amazon in", "amazon.in"], "amazon.in", "https://www.amazon.in/s?k={query}"),
        new("myntra", "Myntra", ["myntra", "myntra.com"], "myntra.com", "https://www.myntra.com/{query}"),
        new("flipkart", "Flipkart", ["flipkart", "flipkart.com"], "flipkart.com", "https://www.flipkart.com/search?q={query}"),
        new("youtube", "YouTube", ["youtube", "yt", "youtube.com"], "youtube.com", "https://www.youtube.com/results?search_query={query}"),
        new("github", "GitHub", ["github", "git hub", "github.com"], "github.com", "https://github.com/search?q={query}"),
        new("reddit", "Reddit", ["reddit", "reddit.com"], "reddit.com", "https://www.reddit.com/search/?q={query}"),
        new("wikipedia", "Wikipedia", ["wikipedia", "wiki", "wikipedia.org"], "wikipedia.org", "https://en.wikipedia.org/wiki/Special:Search?search={query}")
    ];

    private static readonly Dictionary<string, string> KnownDomainAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stack overflow"] = "stackoverflow.com",
        ["stackoverflow"] = "stackoverflow.com",
        ["ajio"] = "ajio.com",
        ["zara"] = "zara.com",
        ["twitter"] = "x.com",
        ["x"] = "x.com",
        ["linkedin"] = "linkedin.com",
        ["netflix"] = "netflix.com",
        ["spotify"] = "spotify.com"
    };

    public static SiteSearchProvider? FindProvider(string siteNameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(siteNameOrAlias)) return null;
        string clean = siteNameOrAlias.Trim().ToLowerInvariant();

        return Providers.FirstOrDefault(p =>
            string.Equals(p.Key, clean, StringComparison.OrdinalIgnoreCase) ||
            p.Aliases.Any(a => string.Equals(a, clean, StringComparison.OrdinalIgnoreCase)));
    }

    public static bool IsKnownOrProbableSite(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        string clean = candidate.Trim().ToLowerInvariant();

        if (FindProvider(clean) != null) return true;
        if (KnownDomainAliases.ContainsKey(clean)) return true;
        if (clean.EndsWith(".com") || clean.EndsWith(".org") || clean.EndsWith(".in") || clean.EndsWith(".io") || clean.EndsWith(".net")) return true;

        // Disallow local storage names or generic search terms
        string[] reserved = ["desktop", "downloads", "documents", "drive", "laptop", "pc", "files", "folder", "google", "the web", "web", "internet"];
        if (reserved.Contains(clean)) return false;

        return false;
    }

    public static string BuildSearchUrl(string siteOrDomain, string query)
    {
        string cleanQuery = query?.Trim() ?? string.Empty;
        var provider = FindProvider(siteOrDomain);

        if (provider != null && !string.IsNullOrWhiteSpace(provider.DirectSearchUrlPattern))
        {
            return provider.DirectSearchUrlPattern.Replace("{query}", Uri.EscapeDataString(cleanQuery));
        }

        // Unknown site fallback: Safe Google site: search
        string domain = ResolveDomain(siteOrDomain);
        return $"https://www.google.com/search?q=site%3A{Uri.EscapeDataString(domain)}+{Uri.EscapeDataString(cleanQuery)}";
    }

    public static string ResolveDomain(string siteOrDomain)
    {
        if (string.IsNullOrWhiteSpace(siteOrDomain)) return "google.com";
        string clean = siteOrDomain.Trim().ToLowerInvariant();

        if (KnownDomainAliases.TryGetValue(clean, out var knownDomain))
        {
            return knownDomain;
        }

        var provider = FindProvider(clean);
        if (provider != null)
        {
            return provider.Domain;
        }

        if (clean.Contains('.'))
        {
            return clean;
        }

        return $"{clean.Replace(" ", "")}.com";
    }

    /// <summary>
    /// Attempts to parse in-site search intent from natural speech.
    /// E.g. "Search Myntra for oversized hoodies", "Search for XL socks in Amazon India",
    /// "Search Stack Overflow for null pointer exceptions", "Open chrome and search YouTube for Kotlin tutorials".
    /// </summary>
    public static bool TryExtractSiteSearch(string candidateText, out string targetSite, out string query)
    {
        targetSite = string.Empty;
        query = string.Empty;

        if (string.IsNullOrWhiteSpace(candidateText)) return false;

        // Strip leading app prefix: "open chrome and search ...", "go to chrome and search ..."
        string text = Regex.Replace(candidateText, @"^(?:please\s+)?(?:open|launch|go\s+to)\s+(?:google\s+)?chrome\s+and\s+", "", RegexOptions.IgnoreCase).Trim();

        // Pattern 1: "search [site] for [query]" or "find [query] on [site]"
        // E.g. "search myntra for oversized hoodies", "search amazon india for men's socks"
        var matchSiteFor = Regex.Match(text, @"^(?:please\s+)?(?:search|find|look\s+up)\s+(?:on\s+|in\s+)?([a-zA-Z0-9\.\s]+?)\s+(?:for\s+)(.+)$", RegexOptions.IgnoreCase);
        if (matchSiteFor.Success)
        {
            string potentialSite = matchSiteFor.Groups[1].Value.Trim();
            string potentialQuery = matchSiteFor.Groups[2].Value.Trim();

            // Require candidate to be a known site, known domain alias, or probable domain (e.g. .com, .in)
            // Prevents common phrases like "in the middle", "for a while", "in the background" from hijacking as websites
            if (IsKnownOrProbableSite(potentialSite))
            {
                targetSite = potentialSite.Trim().ToLowerInvariant();
                query = potentialQuery;
                return true;
            }
        }

        // Pattern 2: "search for [query] in/on [site]"
        // E.g. "search for oversized hoodies in myntra", "search for men's socks on amazon"
        var matchQueryInSite = Regex.Match(text, @"^(?:please\s+)?(?:search|find|look\s+up|buy)(?:\s+for)?\s+(.+?)\s+(?:in|on)\s+([a-zA-Z0-9\.\s]+)$", RegexOptions.IgnoreCase);
        if (matchQueryInSite.Success)
        {
            string potentialQuery = matchQueryInSite.Groups[1].Value.Trim();
            string potentialSite = matchQueryInSite.Groups[2].Value.Trim();

            if (IsKnownOrProbableSite(potentialSite))
            {
                targetSite = potentialSite.Trim().ToLowerInvariant();
                query = potentialQuery;
                return true;
            }
        }

        return false;
    }
}
