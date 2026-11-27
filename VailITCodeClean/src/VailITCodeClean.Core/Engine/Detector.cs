using System.Text.RegularExpressions;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Engine;

/// <summary>One rule hit inside a piece of text (before overlaps are resolved).</summary>
/// <param name="Rule">The rule that matched.</param>
/// <param name="Start">Offset of the text to replace.</param>
/// <param name="Length">Length of the text to replace.</param>
/// <param name="Value">The original text that will be replaced.</param>
/// <param name="Label">Placeholder label (from the rule or a <c>secret_LABEL</c> group).</param>
public sealed record Detection(CompiledRule Rule, int Start, int Length, string Value, string Label)
{
    public int End => Start + Length;
}

/// <summary>
/// One piece of text that will be replaced. Usually a single detection; when several rules match
/// overlapping text they are merged so nothing is left half-hidden (e.g. a URL that also contains a
/// password becomes one placeholder).
/// </summary>
public sealed class DetectionGroup
{
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required string Value { get; init; }

    /// <summary>The detection that names this group (the one covering it all, or the most severe).</summary>
    public required Detection Primary { get; init; }

    /// <summary>All detections in the group, including <see cref="Primary"/>.</summary>
    public required IReadOnlyList<Detection> Members { get; init; }

    public int End => Start + Length;

    /// <summary>Highest severity of any member.</summary>
    public Severity Severity => Members.Max(m => m.Rule.Rule.Severity);
}

/// <summary>Runs a set of compiled rules over text and works out what to replace.</summary>
public sealed class Detector
{
    private readonly List<CompiledRule> _rules = [];
    private readonly HashSet<string> _allowValues;
    private readonly List<Regex> _allowPatterns = [];

    /// <summary>Rules that could not be compiled, with the reason. They are skipped.</summary>
    public List<string> Errors { get; } = [];

    public IReadOnlyList<CompiledRule> Rules => _rules;

    /// <param name="profile">Supplies the global allow lists, the regex timeout and (unless <paramref name="rules"/> is given) the enabled rules.</param>
    /// <param name="rules">Run exactly these rules instead (enabled or not). Used by the rule tester.</param>
    public Detector(CleanProfile profile, IEnumerable<FilterRule>? rules = null)
    {
        var timeout = profile.Options.RegexTimeoutMs;
        int order = 0;
        foreach (var rule in rules ?? profile.Rules.Where(r => r.Enabled))
        {
            try { _rules.Add(CompiledRule.Compile(rule, order++, timeout)); }
            catch (ArgumentException ex) { Errors.Add(ex.Message); }
        }

        _allowValues = new HashSet<string>(profile.AllowValues.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
            StringComparer.OrdinalIgnoreCase);
        foreach (var p in profile.AllowPatterns.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            try { _allowPatterns.Add(new Regex(p, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(timeout))); }
            catch (ArgumentException ex) { Errors.Add($"Allow pattern '{p}' is invalid: {ex.Message}"); }
        }
    }

    /// <summary>Finds everything to replace in <paramref name="text"/>, already merged and sorted by position.</summary>
    /// <param name="relativePath">Used for the rules' include/exclude file filters. Empty = all rules apply.</param>
    /// <param name="warnings">Receives messages about regex timeouts (optional).</param>
    public List<DetectionGroup> Detect(string text, string relativePath = "", ICollection<string>? warnings = null,
        CancellationToken cancel = default)
    {
        var raw = FindAll(text, relativePath, warnings, cancel);
        return Merge(raw, text);
    }

    /// <summary>Every individual hit from every rule (no overlap handling). Useful for the rule tester.</summary>
    public List<Detection> FindAll(string text, string relativePath = "", ICollection<string>? warnings = null,
        CancellationToken cancel = default)
    {
        var found = new List<Detection>();
        if (string.IsNullOrEmpty(text)) return found;
        LineIndex? lines = null;

        foreach (var rule in _rules)
        {
            cancel.ThrowIfCancellationRequested();
            if (!rule.AppliesTo(relativePath)) continue;
            try
            {
                for (var m = rule.Regex.Match(text); m.Success; m = m.NextMatch())
                {
                    if (m.Length == 0) continue;
                    lines ??= new LineIndex(text);
                    AddMatch(rule, m, text, lines, found);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                warnings?.Add($"Rule '{rule.Rule.Id}' timed out on '{relativePath}' and was skipped for that file. Consider simplifying its pattern.");
            }
        }
        return found;
    }

    private void AddMatch(CompiledRule rule, Match m, string text, LineIndex lines, List<Detection> found)
    {
        // Which parts of the match are the secret?
        var spans = new List<(int Start, int Length, string Label)>();
        foreach (var name in rule.SecretGroups)
        {
            var g = m.Groups[name];
            if (!g.Success || g.Length == 0) continue;
            var label = name.Length > 7 ? CompiledRule.MakeLabel(name[7..]) : rule.Label;
            spans.Add((g.Index, g.Length, label));
        }
        if (spans.Count == 0) spans.Add((m.Index, m.Length, rule.Label));

        var line = lines.GetLineText(text, m.Index);
        if (rule.IgnoresLine(line)) return;

        if (rule.Rule.AllPartsRequired &&
            spans.Any(sp => { var v = text.Substring(sp.Start, sp.Length); return !rule.AcceptsValue(v) || IsAllowed(v); }))
            return;

        foreach (var (start, length, label) in spans)
        {
            var value = text.Substring(start, length);
            if (!rule.AcceptsValue(value) || IsAllowed(value)) continue;

            if (rule.Rule.Scope == ReplaceScope.Line)
            {
                var (ls, le) = lines.GetLineBounds(text, start);
                found.Add(new Detection(rule, ls, le - ls, text[ls..le], label));
            }
            else
            {
                found.Add(new Detection(rule, start, length, value, label));
            }
        }
    }

    /// <summary>Global allow list: values that are never scrubbed.</summary>
    public bool IsAllowed(string value)
    {
        var v = value.Trim();
        if (_allowValues.Contains(v)) return true;
        foreach (var r in _allowPatterns)
        {
            try { if (r.IsMatch(v)) return true; }
            catch (RegexMatchTimeoutException) { }
        }
        return false;
    }

    /// <summary>
    /// Merges overlapping detections into groups. The group's label comes from the detection that covers
    /// the whole group (most severe one if several do), otherwise from the most severe detection.
    /// </summary>
    public static List<DetectionGroup> Merge(List<Detection> detections, string text)
    {
        var result = new List<DetectionGroup>();
        if (detections.Count == 0) return result;

        var sorted = detections
            .OrderBy(d => d.Start)
            .ThenByDescending(d => d.Length)
            .ToList();

        int i = 0;
        while (i < sorted.Count)
        {
            int start = sorted[i].Start, end = sorted[i].End;
            var members = new List<Detection> { sorted[i] };
            int j = i + 1;
            while (j < sorted.Count && sorted[j].Start < end)
            {
                members.Add(sorted[j]);
                end = Math.Max(end, sorted[j].End);
                j++;
            }

            var covering = members.Where(d => d.Start == start && d.End == end).ToList();
            var pool = covering.Count > 0 ? covering : members;
            var primary = pool
                .OrderByDescending(d => d.Rule.Rule.Severity)
                .ThenBy(d => d.Rule.Order)
                .First();

            // Remove exact duplicates of the same rule (a rule with several identical matches).
            var distinct = members.DistinctBy(d => (d.Rule.Rule.Id, d.Start, d.Length)).ToList();

            result.Add(new DetectionGroup
            {
                Start = start,
                Length = end - start,
                Value = text[start..end],
                Primary = primary,
                Members = distinct,
            });
            i = j;
        }
        return result;
    }
}
