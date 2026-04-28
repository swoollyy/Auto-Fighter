using System;
using System.Collections.Generic;
using System.Text;
using AutoFighter.Core;

/// <summary>
/// Replaces <c>{token}</c> placeholders in narrative / dialogue text with live
/// runtime values (player name, class, run count, etc.).
///
/// Usage in a dialogue line:
///   "Welcome back, {player_name}."
///   "So you're a {class}, huh?"
///
/// Tokens are case-insensitive. Unknown tokens are left as-is so missing data
/// is obvious in-game (rather than silently blanking the text).
///
/// To add new tokens, call <see cref="Register"/> from a bootstrap /
/// scene-load script, or add a built-in default in <see cref="EnsureDefaults"/>.
/// </summary>
public static class NarrativeTokens
{
    public const string PlayerNameToken = "player_name";
    public const string PlayerClassToken = "class";

    /// <summary>Fallback shown when the player hasn't entered a name yet.</summary>
    public static string DefaultPlayerName = "Rookie";

    private static readonly Dictionary<string, Func<string>> Providers =
        new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase);
    private static bool _defaultsRegistered;

    /// <summary>Register (or overwrite) a token provider. Token name is case-insensitive, no braces.</summary>
    public static void Register(string token, Func<string> provider)
    {
        if (string.IsNullOrEmpty(token) || provider == null) return;
        Providers[token] = provider;
    }

    /// <summary>Remove a previously registered token.</summary>
    public static void Unregister(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        Providers.Remove(token);
    }

    /// <summary>
    /// Replace every <c>{token}</c> in <paramref name="text"/> with the value
    /// returned by its provider. Unknown tokens are left untouched.
    /// </summary>
    public static string Resolve(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.IndexOf('{') < 0) return text;

        EnsureDefaults();

        StringBuilder sb = null;
        int i = 0;
        int len = text.Length;
        while (i < len)
        {
            int open = text.IndexOf('{', i);
            if (open < 0)
            {
                if (sb == null) return text;
                sb.Append(text, i, len - i);
                break;
            }

            int close = text.IndexOf('}', open + 1);
            if (close < 0)
            {
                if (sb == null) return text;
                sb.Append(text, i, len - i);
                break;
            }

            string tokenName = text.Substring(open + 1, close - open - 1).Trim();
            if (tokenName.Length == 0 || !Providers.TryGetValue(tokenName, out var provider))
            {
                // Unknown / empty token: copy the literal chunk (including the braces) and keep scanning.
                if (sb == null) sb = new StringBuilder(len + 16);
                sb.Append(text, i, close - i + 1);
                i = close + 1;
                continue;
            }

            if (sb == null) sb = new StringBuilder(len + 16);
            sb.Append(text, i, open - i);
            string value;
            try { value = provider() ?? string.Empty; }
            catch { value = string.Empty; }
            sb.Append(value);
            i = close + 1;
        }

        return sb != null ? sb.ToString() : text;
    }

    private static void EnsureDefaults()
    {
        if (_defaultsRegistered) return;
        _defaultsRegistered = true;

        Register(PlayerNameToken, GetPlayerName);
        Register(PlayerClassToken, GetPlayerClass);
    }

    private static string GetPlayerName()
    {
        if (SaveSystem.Current == null) SaveSystem.Load();
        string n = SaveSystem.Current != null ? SaveSystem.Current.playerName : null;
        return string.IsNullOrWhiteSpace(n) ? DefaultPlayerName : n;
    }

    private static string GetPlayerClass()
    {
        if (SaveSystem.Current == null) SaveSystem.Load();
        string c = SaveSystem.Current != null ? SaveSystem.Current.selectedCharacterClass : null;
        return string.IsNullOrWhiteSpace(c) ? string.Empty : c;
    }
}
