using TMPro;

/// <summary>
/// Helper for applying vertex effects only to specific words in TMP text.
/// Words are "flagged" by wrapping them in a link tag in the string, e.g.:
///   This is &lt;link="jitter"&gt;shaky&lt;/link&gt; and this is &lt;link="wave"&gt;smooth&lt;/link&gt;.
/// Each effect component (jitter, wave, zoom) is assigned a "link tag" (e.g. "jitter").
/// Only characters inside &lt;link="tagName"&gt;...&lt;/link&gt; get that effect; the rest stay default.
/// </summary>
public static class TMPLinkEffectHelper
{
    /// <summary>
    /// Returns true if the character at index <paramref name="characterIndex"/> is inside a link
    /// whose ID matches <paramref name="linkTag"/> (case-insensitive).
    /// If <paramref name="linkTag"/> is null or empty, returns true for all characters (effect applies to whole text).
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
            if (characterIndex >= first && characterIndex < first + length)
            {
                string id = NormalizeLinkId(link.GetLinkID() ?? "");
                if (string.IsNullOrEmpty(id))
                    return true;
                if (string.Equals(id, tagNormalized, System.StringComparison.OrdinalIgnoreCase))
                    return true;
                if (id.IndexOf(tagNormalized, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (tagNormalized.IndexOf(id, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (link.hashCode == tagNormalized.GetHashCode())
                    return true;
                return false;
            }
        }
        return false;
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
