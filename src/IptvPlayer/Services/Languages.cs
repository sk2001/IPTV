using System.Text.RegularExpressions;

namespace IptvPlayer.Services;

/// <summary>Langues proposées dans les paramètres (audio et sous-titres).</summary>
public static class Languages
{
    public sealed record Lang(string Code, string Label, string Vlc, string Pattern);

    /// <summary>Code vide = automatique (comportement par défaut de VLC / du flux).</summary>
    public static readonly IReadOnlyList<Lang> All = new[]
    {
        new Lang("en", "English", "eng,en", @"\b(en|eng|english|anglais|inglés|ingles)\b"),
        new Lang("fr", "Français", "fra,fre,fr", @"\b(fr|fra|fre|french|français|francais|vf|vff|vfq)\b"),
        new Lang("es", "Español", "spa,es", @"\b(es|spa|spanish|español|espagnol)\b"),
        new Lang("de", "Deutsch", "deu,ger,de", @"\b(de|deu|ger|german|deutsch|allemand)\b"),
        new Lang("it", "Italiano", "ita,it", @"\b(it|ita|italian|italiano|italien)\b"),
        new Lang("pt", "Português", "por,pt", @"\b(pt|por|portuguese|português|portugues|portugais)\b"),
        new Lang("ja", "日本語", "jpn,ja", @"\b(ja|jpn|japanese|japonais|日本語)\b"),
    };

    public static Lang? Find(string? code) => string.IsNullOrEmpty(code) ? null : All.FirstOrDefault(l => l.Code == code);

    /// <summary>Valeur de l'option VLC <c>:audio-language=</c> / <c>:sub-language=</c> (null = automatique).</summary>
    public static string? VlcCodes(string? code) => Find(code)?.Vlc;

    /// <summary>Le nom d'une piste VLC (« Piste 2 - [English] ») correspond-il à la langue ?</summary>
    public static bool NameMatches(string? code, string trackName)
    {
        var l = Find(code);
        return l != null && Regex.IsMatch(trackName, l.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
