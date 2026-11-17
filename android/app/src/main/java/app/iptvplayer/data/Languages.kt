package app.iptvplayer.data

/** Langues proposées pour l'audio et les sous-titres (mêmes codes que l'application Windows). */
object Languages {
    data class Lang(val code: String, val label: String, val vlc: String, val pattern: Regex)

    val all = listOf(
        Lang("en", "English", "eng,en", Regex("""\b(en|eng|english|anglais|inglés|ingles)\b""", RegexOption.IGNORE_CASE)),
        Lang("fr", "Français", "fra,fre,fr", Regex("""\b(fr|fra|fre|french|français|francais|vf|vff|vfq)\b""", RegexOption.IGNORE_CASE)),
        Lang("es", "Español", "spa,es", Regex("""\b(es|spa|spanish|español|espagnol)\b""", RegexOption.IGNORE_CASE)),
        Lang("de", "Deutsch", "deu,ger,de", Regex("""\b(de|deu|ger|german|deutsch|allemand)\b""", RegexOption.IGNORE_CASE)),
        Lang("it", "Italiano", "ita,it", Regex("""\b(it|ita|italian|italiano|italien)\b""", RegexOption.IGNORE_CASE)),
        Lang("pt", "Português", "por,pt", Regex("""\b(pt|por|portuguese|português|portugues|portugais)\b""", RegexOption.IGNORE_CASE)),
        Lang("ja", "日本語", "jpn,ja", Regex("""\b(ja|jpn|japanese|japonais|日本語)\b""", RegexOption.IGNORE_CASE)),
    )

    fun find(code: String?): Lang? = if (code.isNullOrEmpty()) null else all.firstOrNull { it.code == code }

    /** Valeur des options VLC `:audio-language=` / `:sub-language=` (null = automatique). */
    fun vlcCodes(code: String?): String? = find(code)?.vlc

    /** Le nom d'une piste VLC (« Track 2 - [English] ») correspond-il à la langue ? */
    fun nameMatches(code: String?, trackName: String?): Boolean =
        trackName != null && find(code)?.pattern?.containsMatchIn(trackName) == true
}
