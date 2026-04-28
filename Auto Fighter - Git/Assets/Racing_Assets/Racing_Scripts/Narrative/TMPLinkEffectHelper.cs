using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Helper for applying vertex effects only to specific words in TMP text.
/// Supports nested links (e.g. &lt;link="wave"&gt;&lt;link="rainbow"&gt;text&lt;/link&gt;&lt;/link&gt;) via raw-text parsing
/// when TMP's linkInfo only reports the inner link.
/// </summary>
public static class TMPLinkEffectHelper
{
    /// <summary>
    /// Prefer this overload when you have TMP_Text: uses linkInfo first, then raw-text parsing for nested links.
    /// </summary>
    public static bool IsCharacterInLink(TMP_Text text, int characterIndex, string linkTag)
    {
        if (text == null) return false;
        if (characterIndex < 0 || characterIndex >= text.textInfo.characterCount)
            return false;

        if (string.IsNullOrEmpty(linkTag))
            return true;

        // 1) Try TMP's linkInfo (works when TMP reports the link for this character).
        if (IsCharacterInLink(text.textInfo, characterIndex, linkTag))
            return true;

        // 2) Nested-link fallback: TMP often only reports the innermost link. Parse raw text to find all link ranges.
        return IsCharacterInLinkFromParsedText(text.text, characterIndex, linkTag);
    }

    /// <summary>
    /// Returns true if the character at index <paramref name="characterIndex"/> is inside a link
    /// whose ID matches <paramref name="linkTag"/> (case-insensitive), using only TMP's linkInfo.
    /// </summary>
    public static bool IsCharacterInLink(TMP_TextInfo textInfo, int characterIndex, string linkTag)
    {
        if (textInfo == null || characterIndex < 0 || characterIndex >= textInfo.characterCount)
            return false;

        if (string.IsNullOrEmpty(linkTag))
            return true;

        if (textInfo.linkCount <= 0)
            return false;

        string tagNormalized = NormalizeLinkId(linkTag);
        if (string.IsNullOrEmpty(tagNormalized))
            return true;

        for (int i = 0; i < textInfo.linkCount; i++)
        {
            TMP_LinkInfo link = textInfo.linkInfo[i];
            int first = link.linkTextfirstCharacterIndex;
            int length = link.linkTextLength;
            if (characterIndex < first || characterIndex >= first + length)
                continue;

            string id = NormalizeLinkId(link.GetLinkID() ?? "");
            if (string.IsNullOrEmpty(id)) return true;
            if (string.Equals(id, tagNormalized, StringComparison.OrdinalIgnoreCase)) return true;
            if (id.IndexOf(tagNormalized, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (tagNormalized.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (link.hashCode == tagNormalized.GetHashCode()) return true;
        }
        return false;
    }

    /// <summary>
    /// Parses raw text for &lt;link="id"&gt;...&lt;/link&gt; and returns whether characterIndex (visible index) is inside any link with the given tag.
    /// Handles nested links so both outer and inner tags apply.
    /// </summary>
    private static bool IsCharacterInLinkFromParsedText(string rawText, int characterIndex, string linkTag)
    {
        if (string.IsNullOrEmpty(rawText)) return false;

        string tagNormalized = NormalizeLinkId(linkTag);
        if (string.IsNullOrEmpty(tagNormalized)) return true;

        List<(string id, int start, int end)> ranges = ParseLinkRanges(rawText);
        for (int i = 0; i < ranges.Count; i++)
        {
            var r = ranges[i];
            if (!string.Equals(r.id, tagNormalized, StringComparison.OrdinalIgnoreCase)) continue;
            if (characterIndex >= r.start && characterIndex < r.end)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parse raw rich text and return (linkId, startVisibleIndex, endVisibleIndex) for every link.
    /// Nested links each get their own range; overlapping ranges share the same visible indices.
    /// </summary>
    private static List<(string id, int start, int end)> ParseLinkRanges(string raw)
    {
        var ranges = new List<(string id, int start, int end)>();
        var stack = new Stack<(string id, int start)>();
        int visibleIndex = 0;
        int i = 0;

        while (i < raw.Length)
        {
            if (raw[i] == '<')
            {
                int tagStart = i;
                i++;
                while (i < raw.Length && raw[i] != '>') i++;
                if (i >= raw.Length) break;
                string tag = raw.Substring(tagStart, i - tagStart + 1);
                i++;

                if (tag.StartsWith("<link=", StringComparison.OrdinalIgnoreCase))
                {
                    string id = ParseLinkIdFromTag(tag);
                    stack.Push((id, visibleIndex));
                }
                else if (tag.Equals("</link>", StringComparison.OrdinalIgnoreCase) && stack.Count > 0)
                {
                    var pop = stack.Pop();
                    // Link content starts at first visible char after opening tag (start was index before tag)
                    int start = pop.start + 1;
                    if (visibleIndex > start)
                        ranges.Add((pop.id, start, visibleIndex));
                }
                continue;
            }

            if (raw[i] == '&')
            {
                int entityStart = i;
                i++;
                while (i < raw.Length && raw[i] != ';') i++;
                if (i < raw.Length) i++;
                visibleIndex++;
                continue;
            }

            visibleIndex++;
            i++;
        }

        return ranges;
    }

    private static string ParseLinkIdFromTag(string tag)
    {
        // <link="wave"> or <link='wave'>
        int eq = tag.IndexOf('=');
        if (eq < 0) return "";
        int start = eq + 1;
        while (start < tag.Length && (tag[start] == '"' || tag[start] == '\'')) start++;
        if (start >= tag.Length) return "";
        int end = start;
        char quote = '\0';
        if (eq + 1 < tag.Length && (tag[eq + 1] == '"' || tag[eq + 1] == '\''))
            quote = tag[eq + 1];
        while (end < tag.Length && (quote == '\0' ? tag[end] != ' ' && tag[end] != '>' : tag[end] != quote))
            end++;
        string id = tag.Substring(start, end - start);
        return NormalizeLinkId(id);
    }

    private static string NormalizeLinkId(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        id = id.Trim();
        if (id.Length >= 2 && ((id[0] == '"' && id[id.Length - 1] == '"') || (id[0] == '\'' && id[id.Length - 1] == '\'')))
            id = id.Substring(1, id.Length - 2);
        return id.Trim();
    }

    // ---------- Per-span parameters ----------
    //
    // Link IDs may carry parameters after a colon, e.g.:
    //   "jitter"               → no params (component defaults apply)
    //   "jitter:2"             → one positional float (treated as the effect's primary "amp" knob)
    //   "jitter:amp=3,spd=0.5" → named params
    //   "jitter:amp=3;spd=0.5" → ';' is also accepted as a separator
    //
    // MatchesBaseTag recognizes "baseTag" exactly OR "baseTag:anything".

    /// <summary>Parsed breakdown of a link ID like "jitter:amp=3,spd=0.5".</summary>
    public struct LinkParams
    {
        /// <summary>The part before ':' (empty if the raw id was empty).</summary>
        public string baseTag;
        /// <summary>First value after ':' that wasn't a key=val pair (e.g. the "2" in "jitter:2"). Null if none.</summary>
        public float? positional;
        /// <summary>Named key=val pairs, case-insensitive keys.</summary>
        public Dictionary<string, float> named;
    }

    /// <summary>
    /// Returns true if <paramref name="id"/> is exactly <paramref name="baseTag"/> or starts with
    /// "<paramref name="baseTag"/>:". Case-insensitive. Prevents "wave" matching "wavelength".
    /// </summary>
    public static bool MatchesBaseTag(string id, string baseTag)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(baseTag)) return false;
        if (id.Length < baseTag.Length) return false;
        if (string.Compare(id, 0, baseTag, 0, baseTag.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
        if (id.Length == baseTag.Length) return true;
        return id[baseTag.Length] == ':';
    }

    /// <summary>Parse a link id into its base tag and any positional / named float params.</summary>
    public static LinkParams ParseLinkId(string id)
    {
        LinkParams result = default;
        if (string.IsNullOrEmpty(id)) return result;

        id = NormalizeLinkId(id);
        int colon = id.IndexOf(':');
        if (colon < 0) { result.baseTag = id; return result; }

        result.baseTag = id.Substring(0, colon).Trim();
        string body = id.Substring(colon + 1).Trim();
        if (body.Length == 0) return result;

        string[] parts = body.Split(',', ';');
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (p.Length == 0) continue;
            int eq = p.IndexOf('=');
            if (eq < 0)
            {
                if (!result.positional.HasValue &&
                    float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out float pv))
                {
                    result.positional = pv;
                }
                continue;
            }
            string key = p.Substring(0, eq).Trim();
            string val = p.Substring(eq + 1).Trim();
            if (key.Length == 0) continue;
            if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv)) continue;
            if (result.named == null) result.named = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            result.named[key] = fv;
        }
        return result;
    }

    /// <summary>
    /// Scan raw rich text for every <c>&lt;link="baseTag[:...]"&gt;…&lt;/link&gt;</c> (nested included)
    /// and fill <paramref name="multipliers"/> with the parsed value of <paramref name="paramKey"/> for
    /// each character inside any matching link. Characters not inside a matching link get
    /// <paramref name="defaultValue"/>. Inner nested links override outer ones.
    /// </summary>
    /// <param name="usePositionalFallback">If true and the link has no <paramref name="paramKey"/>=val,
    /// use the positional value (e.g. the "2" in "jitter:2").</param>
    public static void BuildPerCharMultipliers(
        string rawText,
        int visibleCharCount,
        string baseTag,
        string paramKey,
        bool usePositionalFallback,
        ref float[] multipliers,
        float defaultValue = 1f)
    {
        int alloc = Mathf.Max(visibleCharCount, 1);
        if (multipliers == null || multipliers.Length < alloc)
            multipliers = new float[alloc];
        for (int i = 0; i < visibleCharCount; i++) multipliers[i] = defaultValue;

        if (visibleCharCount == 0 || string.IsNullOrEmpty(rawText) || string.IsNullOrEmpty(baseTag))
            return;

        ParseAllLinkRanges(rawText, _scratchRanges);
        for (int r = 0; r < _scratchRanges.Count; r++)
        {
            var range = _scratchRanges[r];
            if (!MatchesBaseTag(range.id, baseTag)) continue;

            LinkParams parsed = ParseLinkId(range.id);
            float? v = null;
            if (parsed.named != null && parsed.named.TryGetValue(paramKey, out float nv)) v = nv;
            else if (usePositionalFallback && parsed.positional.HasValue) v = parsed.positional;
            if (!v.HasValue) continue;

            int start = Mathf.Max(0, range.start);
            int end = Mathf.Min(visibleCharCount, range.end);
            for (int c = start; c < end; c++) multipliers[c] = v.Value;
        }
    }

    /// <summary>A single link span in visible-character space.</summary>
    public struct LinkRange
    {
        public string id;
        public int start; // inclusive, visible-char index
        public int end;   // exclusive
    }

    private static readonly List<LinkRange> _scratchRanges = new List<LinkRange>();

    /// <summary>
    /// Parse raw rich text and emit one <see cref="LinkRange"/> per <c>&lt;link=…&gt;…&lt;/link&gt;</c>,
    /// including nested links. Output is written into <paramref name="outRanges"/> (cleared first).
    /// Visible-char indexing matches TMP's <c>TMP_TextInfo.characterInfo</c>.
    /// </summary>
    public static void ParseAllLinkRanges(string rawText, List<LinkRange> outRanges)
    {
        if (outRanges == null) return;
        outRanges.Clear();
        if (string.IsNullOrEmpty(rawText)) return;

        var stack = new Stack<(string id, int start)>();
        int visibleIndex = 0;
        int i = 0;
        int len = rawText.Length;

        while (i < len)
        {
            char c = rawText[i];
            if (c == '<')
            {
                int tagEnd = rawText.IndexOf('>', i + 1);
                if (tagEnd < 0) break;
                string tag = rawText.Substring(i, tagEnd - i + 1);
                i = tagEnd + 1;

                if (tag.StartsWith("<link=", StringComparison.OrdinalIgnoreCase))
                {
                    string id = ParseLinkIdFromTag(tag);
                    stack.Push((id, visibleIndex));
                }
                else if (tag.Equals("</link>", StringComparison.OrdinalIgnoreCase) && stack.Count > 0)
                {
                    var pop = stack.Pop();
                    if (visibleIndex > pop.start)
                        outRanges.Add(new LinkRange { id = pop.id, start = pop.start, end = visibleIndex });
                }
                continue;
            }

            if (c == '&')
            {
                int entityEnd = rawText.IndexOf(';', i + 1);
                if (entityEnd >= 0) i = entityEnd + 1; else i++;
                visibleIndex++;
                continue;
            }

            visibleIndex++;
            i++;
        }
    }
}
