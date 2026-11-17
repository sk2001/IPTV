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
        ["nav.live"] = ("Live TV", "TV en direct"),
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

        // Contrôle à distance (PC = contrôleur)
        ["guide.noInfo"] = ("No program information", "Aucune information de programme"),
        ["hero.onNow"] = ("ON NOW", "À L'ANTENNE"),
        ["hero.upNext"] = ("UP NEXT · {0}", "À SUIVRE · {0}"),
        ["hero.ended"] = ("ENDED", "TERMINÉ"),
        ["hero.watch"] = ("Watch", "Regarder"),
        ["hero.last"] = ("Last channel (Q)", "Dernière chaîne (Q)"),
        ["hero.none"] = ("Select a program in the guide.", "Sélectionnez un programme dans le guide."),
        ["hero.noDesc"] = ("No description available.", "Aucune description disponible."),
        ["hero.back"] = ("Back to the guide (Esc)", "Retour au guide (Échap)"),
        ["hero.expand"] = ("Click to watch full size", "Cliquer pour regarder en grand"),
        ["rem.section"] = ("CONTROL OTHER DEVICES", "PILOTER D'AUTRES APPAREILS"),
        ["rem.open"] = ("Remote control…", "Télécommande…"),
        ["rem.sendTo"] = ("Send to another device", "Envoyer vers un autre appareil"),
        ["rem.title"] = ("Remote control", "Télécommande"),
        ["rem.note"] = ("Control an Android phone or Android TV on your network (remote control must be enabled there). This PC never accepts connections.", "Piloter un téléphone ou une TV Android de votre réseau (le contrôle à distance doit y être activé). Ce PC n'accepte aucune connexion."),
        ["rem.paired"] = ("PAIRED DEVICES", "APPAREILS APPAIRÉS"),
        ["rem.nonePaired"] = ("No paired device yet.", "Aucun appareil appairé pour l'instant."),
        ["rem.nearby"] = ("NEARBY DEVICES", "APPAREILS À PROXIMITÉ"),
        ["rem.searching"] = ("Searching…", "Recherche…"),
        ["rem.noneFound"] = ("No device found. Enable remote control on the other device, or enter its address below.", "Aucun appareil trouvé. Activez le contrôle à distance sur l'autre appareil ou saisissez son adresse ci-dessous."),
        ["rem.rescan"] = ("Search again", "Rechercher à nouveau"),
        ["rem.manual"] = ("OR ENTER AN ADDRESS OR A PAIRING LINK", "OU SAISIR UNE ADRESSE OU UN LIEN D'APPAIRAGE"),
        ["rem.pair"] = ("Pair", "Appairer"),
        ["rem.enterPin"] = ("Enter the PIN shown on “{0}”:", "Saisissez le PIN affiché sur « {0} » :"),
        ["rem.wrongPin"] = ("Wrong PIN", "PIN incorrect"),
        ["rem.forget"] = ("Forget", "Oublier"),
        ["rem.forget.confirm"] = ("Forget “{0}”? You will have to pair it again.", "Oublier « {0} » ? Il faudra l'appairer à nouveau."),
        ["rem.connecting"] = ("Connecting…", "Connexion…"),
        ["rem.unauthorized"] = ("Not authorized: pair this device again.", "Non autorisé : appairez à nouveau cet appareil."),
        ["rem.idle"] = ("Nothing is playing", "Aucune lecture en cours"),
        ["rem.volume"] = ("Volume {0} %", "Volume {0} %"),
        ["rem.mute"] = ("Mute", "Muet"),
        ["rem.audio"] = ("Audio track", "Piste audio"),
        ["rem.subs"] = ("Subtitles", "Sous-titres"),
        ["rem.sync"] = ("Sync favorites, history and settings", "Synchroniser favoris, historique et réglages"),
        ["rem.syncing"] = ("Synchronizing…", "Synchronisation…"),
        ["rem.syncDone"] = ("Synchronized: {0} received, {1} sent.", "Synchronisé : {0} reçus, {1} envoyés."),
        ["rem.syncSkipped"] = ("(Accounts skipped: enable account sync on both devices.)", "(Comptes ignorés : activez leur synchronisation sur les deux appareils.)"),
        ["rem.syncAccounts"] = ("Also sync accounts (passwords travel over the paired, encrypted link)", "Synchroniser aussi les comptes (les mots de passe transitent par la liaison appairée et chiffrée)"),
        ["rem.badLink"] = ("This link is not a valid pairing link for the local network.", "Ce lien n'est pas un lien d'appairage valide pour le réseau local."),
        ["share.open"] = ("Share my setup (QR code)…", "Partager ma configuration (QR code)…"),
        ["share.title"] = ("Share my setup", "Partager ma configuration"),
        ["share.note"] = ("On the Android device, open IPTV Player and choose “Sign in with another device” (or Settings, Remote control, Scan a QR code), then scan this code. Accounts (with passwords), settings, favorites and progress are exchanged.", "Sur l'appareil Android, ouvrez IPTV Player et choisissez « Se connecter avec un autre appareil » (ou Paramètres, Télécommande, Scanner un QR code), puis scannez ce code. Comptes (avec mots de passe), réglages, favoris et progression sont échangés."),
        ["share.code"] = ("Code: {0}", "Code : {0}"),
        ["share.valid"] = ("Valid for 5 minutes, single use. This PC only listens while this window is open.", "Valable 5 minutes, usage unique. Ce PC n'écoute que tant que cette fenêtre est ouverte."),
        ["share.waiting"] = ("Waiting for the other device…", "En attente de l'autre appareil…"),
        ["share.connected"] = ("“{0}” is connected, transferring…", "« {0} » est connecté, transfert en cours…"),
        ["share.done"] = ("Transfer with “{0}” done. You can close this window.", "Transfert avec « {0} » terminé. Vous pouvez fermer cette fenêtre."),
        ["share.noNetwork"] = ("No local network found. Connect to Wi-Fi or Ethernet.", "Aucun réseau local trouvé. Connectez-vous au Wi-Fi ou à Ethernet."),
        ["share.firewall"] = ("The first time, Windows may ask to allow IPTV Player on private networks: allow it so that the other device can connect.", "La première fois, Windows peut demander d'autoriser IPTV Player sur les réseaux privés : autorisez-le pour que l'autre appareil puisse se connecter."),
        ["rem.certChanged"] = ("This device's security certificate has changed (app reinstalled or device replaced). Forget it in the Remote control window, then pair it again.", "Le certificat de sécurité de cet appareil a changé (appli réinstallée ou appareil remplacé). Oubliez-le dans la fenêtre Télécommande, puis appairez-le à nouveau."),
        ["rem.autoSync"] = ("Keep this PC and linked devices in sync automatically (every 30 s and after each change)", "Garder ce PC et les appareils liés à jour automatiquement (toutes les 30 s et après chaque changement)"),
        ["rem.autoSyncOk"] = ("Kept in sync automatically — last check {0}", "Synchronisé automatiquement — dernière vérification {0}"),
        ["rem.autoSyncErr"] = ("Automatic sync: {0} (will retry)", "Synchro automatique : {0} (nouvel essai)"),
        ["share.linked"] = ("“{0}” is now linked: it stays in sync with this PC while both apps are open.", "« {0} » est maintenant lié : il reste synchronisé avec ce PC tant que les deux applications sont ouvertes."),
        ["rem.back"] = ("← Devices", "← Appareils"),

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
