# Protocole de contrôle à distance — version 1 (brouillon)

Commun au téléphone, à la TV et au PC. Les termes **cible** (appareil qui lit et expose l'API) et **contrôleur** (appareil qui pilote) sont relatifs à une connexion.

> **Rôles (décision du 2026-11-16)** : les appareils **Android** (téléphone, tablette, TV) sont **cibles et contrôleurs**. L'application **Windows est contrôleur seulement** : elle n'écoute pas et n'annonce aucun service. Les valeurs `platform` des cibles sont donc `android` et `androidtv`.

## 1. Principes

* Réseau local uniquement. Désactivé par défaut (réglage « Contrôle à distance »).
* Découverte **mDNS / DNS-SD**, transport **HTTPS** (TLS 1.2+), état temps réel par **WebSocket**, données en **JSON UTF-8**.
* Appairage par **PIN** affiché sur la cible ; jeton secret par appareil ; certificat auto-signé **épinglé**.
* Version du protocole dans chaque réponse (`protocol: 1`). Un client refuse un serveur de version majeure supérieure.

## 2. Découverte

Service `_iptvplayer._tcp.local.`, nom d'instance = nom de l'appareil. Port par défaut **47231** (aléatoire si occupé).

| Clé TXT | Exemple | Sens |
|---|---|---|
| `v` | `1` | version du protocole |
| `id` | UUID | identifiant stable de l'appareil |
| `name` | `Salon` | nom affichable |
| `platform` | `android` / `androidtv` | type d'appareil (une cible) |
| `fp` | 16 hex | début de l'empreinte SHA-256 du certificat (indice, **non fiable**) |

Repli sans mDNS : saisie de l'adresse IP et du port, ou **QR code** affiché par la cible : `iptvplayer://pair?host=<ip>&port=<port>&id=<uuid>`.

## 3. Sécurité

1. **Certificat** : ECDSA P-256 auto-signé, généré au premier lancement, validité 10 ans. Les clients **épinglent** son empreinte SHA-256 à l'appairage et la vérifient ensuite à chaque connexion.
2. **Source** : la cible n'accepte que les adresses privées (RFC 1918, lien local, `fc00::/7`) ; tout autre pair est refusé (HTTP 403).
3. **Non appairé** : seuls `GET /api/v1/info` et `/api/v1/pair/*` répondent.
4. **Appairage** (§4) lie le PIN à l'empreinte vue par le client : un intercepteur TLS fait échouer la preuve.
5. **Jeton** : 32 octets aléatoires (base64url), envoyé en `Authorization: Bearer <jeton>`. La cible ne stocke que son SHA-256 + nom + date de dernière utilisation.
6. **Limites** : 5 essais de PIN, puis l'appairage en cours est annulé ; 5 démarrages d'appairage par minute ; 120 requêtes par minute et par jeton.
7. **Révocation** : depuis la cible (liste des appareils) ou par le contrôleur (`DELETE /api/v1/pair`).
8. Les URL de flux et les mots de passe ne sont **jamais journalisés**.

## 4. Appairage

```
Contrôleur                                    Cible (affiche le PIN)
    │  POST /api/v1/pair/start                       │
    │  {deviceId, deviceName, platform, clientNonce} │
    │───────────────────────────────────────────────►│  génère PIN à 6 chiffres (valable 120 s)
    │◄───────────────────────────────────────────────│  {pairingId, serverNonce, expiresIn}
    │  l'utilisateur saisit le PIN lu sur la cible   │
    │  proof = HMAC-SHA256(PIN, "iptvplayer-pair-v1" │
    │          ‖ pairingId ‖ clientNonce ‖ serverNonce ‖ fpVueParLeClient)
    │  POST /api/v1/pair/confirm {pairingId, proof}  │
    │───────────────────────────────────────────────►│  recalcule avec SA propre empreinte
    │◄───────────────────────────────────────────────│  {token, serverId, serverName, serverProof}
    │  vérifie serverProof (même HMAC, préfixe "server")
```

**Format exact de la preuve** : HMAC-SHA256 (clé = PIN en UTF-8) du texte `label \n pairingId \n clientNonce \n serverNonce \n empreinte`, sorti en hexadécimal minuscule. `label` = `iptvplayer-pair-v1` (preuve du client) ou `iptvplayer-pair-v1-server` (preuve du serveur) ; `empreinte` = SHA-256 hexadécimal minuscule du certificat DER vu par le client. L'implémentation de référence est `PairCrypto.proof` (`android/.../remote/Pairing.kt`).

Le contrôleur mémorise `token`, `serverId` et l'empreinte du certificat.

## 5. Points d'accès (tous sous `/api/v1`)

| Méthode | Chemin | Auth | Rôle |
|---|---|---|---|
| GET | `/info` | non | `{protocol, id, name, platform, appVersion, remoteEnabled, paired}` |
| POST | `/pair/start`, `/pair/confirm` | non | appairage (§4) |
| DELETE | `/pair` | jeton | l'appareil se désappaire |
| GET | `/state` | jeton | `PlayerState` courant |
| WS | `/ws` | jeton | flux d'états et d'événements |
| POST | `/command` | jeton | envoie une commande (§7) |
| GET | `/sync/manifest` | jeton | version de chaque collection |
| GET | `/sync/{collection}?since=<ISO>` | jeton | enregistrements modifiés depuis |
| PUT | `/sync/{collection}` | jeton | envoie des enregistrements |

Erreurs : `{ "error": { "code": "...", "message": "..." } }` avec HTTP 400 (`bad_request`), 401 (`unauthorized`), 403 (`remote_disabled` / `forbidden_network`),
404 (`not_found`), 409 (`busy`), 422 (`invalid_command`), 429 (`rate_limited`).

## 6. `PlayerState`

```json
{
  "protocol": 1,
  "state": "idle | buffering | playing | paused | error",
  "media": {
    "title": "AMI TÉLÉ", "subtitle": "19:00 Et si c'était toi ?",
    "kind": "live | movie | series | file",
    "ref": { "accountId": "uuid", "itemId": "123", "episodeId": "456" },
    "logo": "https://…"
  },
  "positionMs": 0, "durationMs": 0, "seekable": false,
  "volume": 80, "muted": false,
  "audioTracks": [{ "id": 1, "name": "English" }], "audioTrackId": 1,
  "subtitleTracks": [{ "id": 2, "name": "English" }], "subtitleTrackId": -1,
  "recording": { "active": false, "elapsedMs": 0, "bytes": 0 },
  "fullscreen": false, "canNext": true, "canPrev": true,
  "updatedAt": "2026-11-16T20:00:00.000Z"
}
```

Le serveur diffuse `{ "type": "state", "data": PlayerState }` immédiatement à chaque changement, et au plus une fois par seconde pendant la lecture
(position). Événements : `{ "type": "event", "name": "playbackError | recordingStopped | unpaired", "data": {…} }`.

## 7. Commandes (`POST /command`)

Corps : `{ "type": "<commande>", …paramètres }`. Réponse : `{ "ok": true }` (le résultat réel arrive par l'état).

| `type` | Paramètres | Effet |
|---|---|---|
| `play` | `source: { url, title, subtitle?, kind, logo?, ref? }`, `startMs?` | **Envoyer une lecture** : lit l'URL (remplace la lecture en cours) |
| `playRef` | `ref`, `startMs?` | lit un élément du catalogue de la cible (nécessite le même `accountId` ; évite d'envoyer l'URL) |
| `pause`, `resume`, `toggle`, `stop` | — | transport |
| `seek` / `seekBy` | `positionMs` / `deltaMs` | déplacement (si `seekable`) |
| `volume` / `volumeBy` | `level` (0–100) / `delta` | volume |
| `mute` | `muted?` (bascule si absent) | muet |
| `next`, `prev` | — | chaîne ou épisode suivant / précédent dans la liste de la cible |
| `audioTrack` | `id` ou `"next"` | piste audio |
| `subtitleTrack` | `id`, `"off"` ou `"next"` | sous-titres |
| `fullscreen` | `value?` | plein écran (bascule si absent) |
| `record` | `start: true|false` | enregistrement du flux en cours |

Les commandes sont idempotentes quand c'est possible. Une commande inconnue renvoie `422 invalid_command` (compatibilité ascendante : un client ne doit pas planter si un champ est inconnu).

## 8. Synchronisation

Collections : `accounts`, `favorites`, `history`, `settings` (préférences de langue).
Un enregistrement : `{ "id": "…", "updatedAt": "ISO-UTC-ms", "deleted": false, "data": {…} }`.

* **Résolution** : la dernière écriture gagne (`updatedAt`) ; à égalité, l'`id` d'appareil le plus grand gagne.
* **Suppressions** : enregistrement `deleted: true` conservé 90 jours.
* **Déroulé** : l'initiateur lit `/sync/manifest`, tire `?since=` ce qui est plus récent chez le pair, applique, puis envoie ce qui est plus récent chez lui (`PUT`).
* **Comptes** : n'est effectuée que si l'option « Synchroniser les comptes » est activée **sur les deux appareils**. Les mots de passe voyagent sous TLS épinglé et sont chiffrés au repos (DPAPI sur Windows, Android Keystore).
* **Clés d'enregistrement** : `accounts` = GUID du compte ; `favorites` = `accountId|kind|itemId` ; `history` = `accountId|kind|itemId|episodeId` (mêmes clés que l'application Windows) ; `settings` = nom du réglage.

**Précisions d'implémentation (v1)** : l'initiateur tire la collection entière (`GET /sync/{collection}`, `since` facultatif), fusionne, puis envoie ce que le pair n'a pas (`PUT`). Chaque enregistrement porte `by` (identifiant de l'appareil auteur) pour départager les égalités. `data` est un objet à clés triées pour l'empreinte ; les identifiants de compte sont des GUID avec tirets en minuscules et le type de contenu un entier (0 live, 1 film, 2 série). `/sync/accounts` répond `403 forbidden` si l'option « synchroniser les comptes » est désactivée sur la cible.

## 9. Offre de configuration (QR code « connexion simplifiée »)

Un appareil qui **détient** la configuration (PC, téléphone) ou qui doit **la recevoir** (TV au premier lancement) affiche une *offre* : un PIN à usage unique valable 5 minutes, placé aussi dans le QR code.

Lien : `iptvplayer://pair?host=<ip>&port=<port>&id=<uuid>&name=<nom>&pin=<6 chiffres>&fp=<SHA-256 du certificat, 64 hex>&plat=<android|androidtv|windows>`

* Le lecteur du QR code se connecte à l'adresse, **vérifie l'empreinte `fp`** avant tout échange, puis déroule l'appairage du §4 avec le `pin` lu (sans saisie). L'hôte doit être une adresse locale (RFC 1918, lien local, `fc00::/7`).
* L'offre est **à usage unique** (fermée dès le premier appairage réussi) et annulée après 10 mauvaises preuves ou 5 minutes.
* L'appareil appairé par une offre est autorisé, pendant 10 minutes, à échanger la collection `accounts` (mots de passe compris) même si l'option « synchroniser les comptes » est désactivée ; un appairage ordinaire (PIN lu à l'écran) ne donne pas ce droit.
* Le contrôleur demande **l'accord de l'utilisateur** avant le transfert (comptes, réglages, favoris, progression) puis lance la synchronisation du §8 dans les deux sens.
* **PC** : le serveur n'existe que le temps où la fenêtre « Partager ma configuration » est ouverte (port aléatoire, certificat éphémère) ; il n'implémente que `/info`, `/pair/*` et `/sync/*`.
* **TV** : au premier lancement (aucun compte), la TV active le contrôle à distance et affiche l'offre ; le téléphone la scanne et envoie sa configuration.

