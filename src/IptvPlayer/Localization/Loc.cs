using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace IptvPlayer.Localization;

/// <summary>
/// Traductions de l'interface (anglais par défaut, français). Dans le code : <c>Loc.T("clé", args)</c>.
/// Dans le XAML : <c>{loc:T clé}</c> (se met à jour en direct quand la langue change).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    /// <summary>Code de la langue active : « en » ou « fr ».</summary>
    public static string Language { get; private set; } = "en";

    public static readonly IReadOnlyList<(string Code, string Label)> Available = new[] { ("en", "English"), ("fr", "Français") };

    public static event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private static bool _applied;

    public static void SetLanguage(string? code)
    {
        var lang = code == "fr" ? "fr" : "en";
        if (_applied && lang == Language) return;       // pas de rafraîchissement inutile
        _applied = true;
        Language = lang;
        var culture = CultureInfo.GetCultureInfo(Language == "fr" ? "fr-FR" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));   // met à jour tous les {loc:T}
        LanguageChanged?.Invoke();
    }

    /// <summary>Indexeur lié par le XAML.</summary>
    public string this[string key] => T(key);

    public static string T(string key) =>
        Table.TryGetValue(key, out var v) ? (Language == "fr" ? v.Fr : v.En) : key;

    public static string T(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    // ------------------------------------------------------------------ Table de traductions

    private static readonly Dictionary<string, (string En, string Fr)> Table = new()
    {
        // Navigation / liste
        ["nav.live"] = ("Live", "Live"),
        ["nav.guide"] = ("Guide", "Guide"),
        ["nav.movies"] = ("Movies", "Films"),
        ["nav.series"] = ("Series", "Séries"),
        ["nav.rec"] = ("Recordings", "Enreg."),
        ["nav.refresh.tip"] = ("Refresh catalog", "Actualiser le catalogue"),
        ["nav.settings.tip"] = ("Settings", "Paramètres"),
        ["item.favorite.tip"] = ("Favorite", "Favori"),
        ["main.account.label"] = ("ACCOUNT", "COMPTE"),
        ["main.account.none"] = ("No account", "Aucun compte"),
        ["main.manageAccounts"] = ("Manage accounts…", "Gérer les comptes…"),
        ["main.search"] = ("Search…", "Rechercher…"),
        ["cat.all"] = ("All", "Tout"),
        ["cat.favorites"] = ("★  Favorites", "★  Favoris"),
        ["cat.history"] = ("↺  History", "↺  Historique"),

        // Guide TV
        ["guide.title"] = ("TV Guide", "Guide TV"),
        ["guide.earlier.tip"] = ("2 h earlier (Shift + wheel, ←)", "2 h plus tôt (Maj + molette, ←)"),
        ["guide.now"] = ("Now", "Maintenant"),
        ["guide.later.tip"] = ("2 h later (Shift + wheel, →)", "2 h plus tard (Maj + molette, →)"),
        ["guide.filter"] = ("Filter channels…", "Filtrer les chaînes…"),
        ["guide.channels"] = ("CHANNELS", "CHAÎNES"),
        ["guide.loading"] = ("Loading TV guide…", "Chargement du guide TV…"),
        ["guide.noSource"] = ("This playlist provides no TV guide (url-tvg missing).", "Cette playlist ne fournit pas de guide TV (url-tvg absent)."),
        ["guide.downloading"] = ("TV guide: downloading…", "Guide TV : téléchargement…"),
        ["guide.channelsCount"] = ("TV guide: {0:N0} channels", "Guide TV : {0:N0} chaînes"),
        ["guide.unavailableStatus"] = ("TV guide unavailable: {0}", "Guide TV indisponible : {0}"),
        ["guide.unavailable"] = ("TV guide unavailable for this account.", "Guide TV indisponible pour ce compte."),
        ["guide.none"] = ("No TV guide available for this account.", "Aucun guide TV disponible pour ce compte."),
        ["guide.noPrograms"] = ("None of the channels in this selection has programs in the guide.", "Aucune chaîne de cette sélection n'a de programme dans le guide."),

        // Enregistrements
        ["rec.title"] = ("Recordings", "Enregistrements"),
        ["rec.openFolder"] = ("Open folder", "Ouvrir le dossier"),
        ["rec.changeFolder"] = ("Change folder…", "Changer le dossier…"),
        ["rec.active"] = ("● recording in progress", "● enregistrement en cours"),
        ["rec.play.tip"] = ("Play", "Lire"),
        ["rec.delete.tip"] = ("Delete", "Supprimer"),
        ["rec.empty"] = ("No recordings.\nDuring playback, click the ● button in the player (or press R).", "Aucun enregistrement.\nPendant une lecture, clique sur le bouton ● du lecteur (ou touche R)."),
        ["rec.localNothing"] = ("Nothing to record: playing a local file", "Rien à enregistrer : lecture d'un fichier local"),
        ["rec.done"] = ("Recording finished: {0}", "Enregistrement terminé : {0}"),
        ["rec.failed"] = ("Recording failed: {0}", "Enregistrement impossible : {0}"),
        ["rec.started"] = ("Recording started", "Enregistrement démarré"),
        ["rec.noData"] = ("No data recorded yet — the stream may be unavailable", "Aucune donnée enregistrée pour l'instant — le flux est peut-être indisponible"),
        ["rec.badge"] = ("REC  {0}  ·  {1}", "REC  {0}  ·  {1}"),
        ["rec.dir.err"] = ("Folder", "Dossier"),
        ["rec.dir.dialog"] = ("Recordings folder", "Dossier des enregistrements"),
        ["rec.delete.confirm"] = ("Permanently delete “{0}” ({1})?", "Supprimer définitivement « {0} » ({1}) ?"),
        ["rec.delete.title"] = ("Delete recording", "Supprimer l'enregistrement"),
        ["rec.delete.err"] = ("Cannot delete", "Suppression impossible"),
        ["rec.stopFirst"] = ("Stop the current recording first.", "Arrête d'abord l'enregistrement en cours."),
        ["quit.rec"] = ("A recording is in progress. Quitting the application will stop it.\n\nQuit anyway?", "Un enregistrement est en cours. Quitter l'application l'arrêtera.\n\nQuitter quand même ?"),
        ["quit.rec.title"] = ("Recording in progress", "Enregistrement en cours"),

        // Lecteur
        ["player.idle"] = ("Double-click an item to play it", "Double-cliquez sur un contenu pour le lire"),
        ["player.playpause.tip"] = ("Play / Pause (Space)", "Lecture / Pause (Espace)"),
        ["player.stop.tip"] = ("Stop", "Arrêter"),
        ["player.record.tip"] = ("Record (R)", "Enregistrer (R)"),
        ["player.audio.tip"] = ("Next audio track", "Piste audio suivante"),
        ["player.subs.tip"] = ("Next subtitles", "Sous-titres suivants"),
        ["player.mute.tip"] = ("Mute (M)", "Muet (M)"),
        ["player.fullscreen.tip"] = ("Fullscreen (F)", "Plein écran (F)"),
        ["player.error"] = ("Playback error — stream unavailable", "Erreur de lecture — flux indisponible"),
        ["player.buffering"] = ("Buffering… {0:0}%", "Mise en mémoire tampon… {0:0}%"),
        ["player.initializing"] = ("Initializing player…", "Initialisation du lecteur…"),
        ["player.connecting"] = ("Connecting…", "Connexion…"),
        ["player.errorPrefix"] = ("Error: {0}", "Erreur : {0}"),
        ["player.vlcFail"] = ("Unable to initialize VLC:\n{0}", "Impossible d'initialiser VLC :\n{0}"),
        ["resume.at"] = ("Resuming at {0}", "Reprise à {0}"),
        ["track.audio"] = ("Audio: {0}", "Audio : {0}"),
        ["track.subs"] = ("Subtitles: {0}", "Sous-titres : {0}"),
        ["track.noAudio"] = ("No audio track", "Aucune piste audio"),
        ["track.noSubs"] = ("No subtitles", "Aucun sous-titre"),

        // Statuts / listes vides
        ["vm.loading"] = ("Loading…", "Chargement…"),
        ["vm.updating"] = ("Updating…", "Mise à jour…"),
        ["vm.cacheFrom"] = ("Cache from {0:g}", "Cache du {0:g}"),
        ["vm.updated"] = ("{0:N0} items · updated", "{0:N0} éléments · mis à jour"),
        ["vm.error"] = ("Error: {0}", "Erreur : {0}"),
        ["vm.loadingEpisodes"] = ("Loading episodes…", "Chargement des épisodes…"),
        ["vm.noEpisodes"] = ("No episodes found.", "Aucun épisode trouvé."),
        ["empty.noAccount"] = ("Add an account to get started.", "Ajoutez un compte pour commencer."),
        ["empty.noFav"] = ("No favorites yet.", "Aucun favori pour l'instant."),
        ["empty.noHistory"] = ("Nothing in history yet.\nMovies and episodes you watch appear here (Delete to remove one).", "Rien dans l'historique pour l'instant.\nLes films et épisodes regardés apparaissent ici (Suppr pour en retirer un)."),
        ["empty.noContent"] = ("No content in this section.", "Aucun contenu dans cette section."),
        ["empty.noResult"] = ("No results.", "Aucun résultat."),
        ["watch.seen"] = ("✓ Watched", "✓ Vu"),

        // Comptes
        ["acc.title"] = ("IPTV accounts", "Comptes IPTV"),
        ["acc.mine"] = ("My accounts", "Mes comptes"),
        ["acc.new"] = ("＋  New account", "＋  Nouveau compte"),
        ["acc.formNew"] = ("New account", "Nouveau compte"),
        ["acc.name"] = ("Account name", "Nom du compte"),
        ["acc.type"] = ("Type", "Type"),
        ["acc.xtream"] = ("Xtream Codes", "Xtream Codes"),
        ["acc.m3u"] = ("M3U playlist", "Playlist M3U"),
        ["acc.urlXtream"] = ("Server URL (http://host:port)", "URL du serveur (http://hote:port)"),
        ["acc.urlM3u"] = ("Playlist URL or local file (.m3u / .m3u8)", "URL de la playlist ou fichier local (.m3u / .m3u8)"),
        ["acc.browse"] = ("Browse…", "Parcourir…"),
        ["acc.username"] = ("Username", "Nom d'utilisateur"),
        ["acc.password"] = ("Password", "Mot de passe"),
        ["acc.test"] = ("Test", "Tester"),
        ["acc.save"] = ("Save", "Enregistrer"),
        ["acc.delete"] = ("Delete", "Supprimer"),
        ["acc.close"] = ("Close", "Fermer"),
        ["acc.testing"] = ("Testing…", "Test en cours…"),
        ["acc.ok"] = ("Connection successful ✔", "Connexion réussie ✔"),
        ["acc.playlistOk"] = ("Valid playlist: {0:N0} entries ✔", "Playlist valide : {0:N0} entrées ✔"),
        ["acc.playlistBad"] = ("Empty or invalid playlist.", "Playlist vide ou invalide."),
        ["acc.saved"] = ("Account saved ✔", "Compte enregistré ✔"),
        ["acc.confirmDelete"] = ("Delete account “{0}”?", "Supprimer le compte « {0} » ?"),
        ["acc.confirm"] = ("Confirmation", "Confirmation"),
        ["acc.err.url"] = ("The URL is required.", "L'URL est obligatoire."),
        ["acc.err.creds"] = ("Username and password are required.", "Nom d'utilisateur et mot de passe obligatoires."),
        ["acc.defaultName"] = ("My account", "Mon compte"),
        ["xtream.err.auth"] = ("Credentials rejected by the server.", "Identifiants refusés par le serveur."),
        ["xtream.err.unexpected"] = ("Unexpected server response.", "Réponse inattendue du serveur."),
        ["xtream.live"] = ("Channels…", "Chaînes…"),
        ["xtream.movies"] = ("Movies…", "Films…"),
        ["xtream.series"] = ("Series…", "Séries…"),
        ["m3u.uncategorized"] = ("Uncategorized", "Sans catégorie"),

        // Épisodes
        ["ep.title"] = ("Episodes", "Épisodes"),
        ["ep.season"] = ("Season", "Saison"),
        ["ep.toggle"] = ("Mark watched / unwatched", "Marquer vu / non vu"),
        ["ep.toggle.tip"] = ("Checks or unchecks the selected episode in your history", "Coche ou décoche l'épisode sélectionné dans l'historique"),
        ["ep.cancel"] = ("Cancel", "Annuler"),
        ["ep.play"] = ("Play", "Lire"),
        ["ep.resume"] = ("Resume", "Reprendre"),

        // Paramètres
        ["set.title"] = ("Settings", "Paramètres"),
        ["set.note"] = ("Changes apply to the next playback.", "Les changements s'appliquent à la prochaine lecture."),
        ["set.language"] = ("INTERFACE LANGUAGE", "LANGUE DE L'INTERFACE"),
        ["set.language.desc"] = ("Language of menus and messages. Applied immediately.", "Langue des menus et des messages. Appliquée immédiatement."),
        ["set.audioVod"] = ("AUDIO LANGUAGE — MOVIES AND SERIES", "LANGUE AUDIO — FILMS ET SÉRIES"),
        ["set.audioVod.desc"] = ("Audio track chosen automatically when the stream offers several (dubbed / original…).", "Piste audio choisie automatiquement quand le flux en propose plusieurs (VF / VO…)."),
        ["set.subs"] = ("SUBTITLES — MOVIES AND SERIES", "SOUS-TITRES — FILMS ET SÉRIES"),
        ["set.subs.desc"] = ("Enabled only if a subtitle track in this language exists.", "Activés seulement si une piste de sous-titres dans cette langue existe."),
        ["set.audioLive"] = ("AUDIO LANGUAGE — LIVE CHANNELS", "LANGUE AUDIO — CHAÎNES LIVE"),
        ["set.audioLive.desc"] = ("“Auto” lets the stream decide (recommended for local channels).", "« Auto » laisse le flux décider (recommandé pour les chaînes locales)."),
        ["set.resume"] = ("RESUME PLAYBACK", "REPRISE DE LA LECTURE"),
        ["set.resume.desc"] = ("Resume a movie or episode where you stopped.", "Reprendre un film ou un épisode là où tu t'étais arrêté."),
        ["set.history"] = ("HISTORY", "HISTORIQUE"),
        ["set.history.clear"] = ("Clear history…", "Effacer l'historique…"),
        ["set.history.info"] = ("{0} movie(s) and {1} series in this account's history. Available under Movies / Series → “History” (Delete to remove an item).", "{0} film(s) et {1} série(s) dans l'historique de ce compte. Disponible dans Films / Séries → « Historique » (Suppr pour retirer un élément)."),
        ["set.history.confirm"] = ("Clear all watch history (all accounts)?\nResume positions will be lost.", "Effacer tout l'historique de visionnage (tous comptes) ?\nLes positions de reprise seront perdues."),
        ["set.history.confirm.title"] = ("Clear history", "Effacer l'historique"),
        ["set.accounts"] = ("ACCOUNTS", "COMPTES"),
        ["set.about"] = ("ABOUT", "À PROPOS"),
        ["set.about.text"] = ("IPTV Player {0} — playback powered by libVLC (VideoLAN, LGPL-2.1+). The app provides no content: you are responsible for the rights to the streams you play or record.", "IPTV Player {0} — lecture propulsée par libVLC (VideoLAN, LGPL-2.1+). L'application ne fournit aucun contenu : tu es responsable des droits des flux que tu lis ou enregistres."),
        ["set.licenses"] = ("Component licenses…", "Licences des composants…"),
        ["set.close"] = ("Close", "Fermer"),
        ["set.auto"] = ("Auto", "Auto"),
        ["set.none"] = ("None", "Aucun"),
        ["set.yes"] = ("Yes", "Oui"),
        ["set.no"] = ("No", "Non"),

        // Licences
        ["lic.title"] = ("Component licenses", "Licences des composants"),
        ["lic.tab.project"] = ("IPTV Player (MIT)", "IPTV Player (MIT)"),
        ["lic.tab.notices"] = ("Components and obligations", "Composants et obligations"),
        ["lic.tab.lgpl"] = ("LGPL-2.1 (VLC, LibVLCSharp)", "LGPL-2.1 (VLC, LibVLCSharp)"),
        ["lic.tab.dotnet"] = (".NET runtime notices", "Notices du runtime .NET"),
        ["lic.missing"] = ("(document “{0}” not found in the application)", "(document « {0} » introuvable dans l'application)"),

        // Divers
        ["error.title"] = ("Error", "Erreur"),
        ["unit.b"] = ("B", "o"),
        ["unit.kb"] = ("KB", "Ko"),
        ["unit.mb"] = ("MB", "Mo"),
        ["unit.gb"] = ("GB", "Go"),
    };
}

/// <summary>Extension XAML : <c>Text="{loc:T nav.live}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
