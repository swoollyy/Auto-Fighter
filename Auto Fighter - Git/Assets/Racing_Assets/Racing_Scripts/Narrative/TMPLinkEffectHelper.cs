using System;
using System.Collections.Generic;
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
}
