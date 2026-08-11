# API du ModDB Vintage Story : notes pour le client C# de Prospect

## Méthode

Trois sources, croisées entre elles :

1. **Le code PHP du site**, cloné dans `/home/lfievet/dev/vsmoddb`. C'est la source de vérité pour tout ce qui est comportement serveur : chemins cités en relatif à ce dépôt (`lib/api/...`, `index.php`, etc.).
2. **L'API live** sur `https://mods.vintagestory.at`, interrogée en anonyme avec `User-Agent: Prospect-research`, requêtes espacées d'au moins 1,5 s. Au total une vingtaine de requêtes sur environ dix minutes (une petite quinzaine d'appels JSON pour valider chaque endpoint et chaque cas d'erreur, plus les `HEAD`/téléchargements de fichiers demandés au point 4) : rien d'automatisé, rien en boucle, aucune authentification tentée. Chaque échantillon JSON de ce document vient d'un appel réel fait le 2026-08-10.
3. **`/home/lfievet/dev/VintagePack`**, un projet web antérieur de l'utilisateur qui consomme déjà cette API côté TypeScript (`src/services/api.ts`, `api/download.ts`). Juste survolé, mais ça confirme indépendamment plusieurs points (pas de pagination, v1 préférée à v2, filtrage par `side` cassé côté serveur).

Un détail structurel à avoir en tête tout de suite : le dépôt contient aussi `util/VintagestoryAPI.xml`, le fichier de documentation XML généré par le compilateur .NET pour l'assembly officielle `VintagestoryAPI.dll` (embarquée pour l'outil interne `modpeek` qui parse les mods uploadés). C'est la doc *du jeu lui-même* sur la classe `ModInfo`, donc une source fiable et à part pour la section modinfo.json, indépendante du code web du ModDB.

---

## Vue d'ensemble : deux générations d'API sous un seul routeur

Il n'y a pas de dossier `api/` au sens fichiers statiques : nginx envoie tout à `index.php` (`docker/moddb.conf` lignes 30-37), qui route lui-même selon le premier segment d'URL. Le branchement qui compte est ici, `index.php` lignes 29-39 :

```php
if($urlparts[0] === 'api') { // :ReservedUrlPrefixes
	array_shift($urlparts);
	if(count($urlparts) > 0 && $urlparts[0] === 'v2') {
		array_shift($urlparts);
		include("lib/api/v2.php");
	}
	else {
		include("lib/api/v1/entry.php");
	}
	exit();
}
```

Autrement dit : **tout `/api/...` qui n'est pas explicitement préfixé `/api/v2/...` tombe dans l'API historique (« v1 »)**, celle que tout le monde utilise depuis des années (c'est elle que documente le wiki, elle que VintagePack appelle, elle qui a les endpoints `/api/mods`, `/api/mod/{id}`, `/api/tags`, `/api/gameversions` cités dans la mission). `/api/v2/...` est une deuxième API, plus jeune, en partie authentifiée, découverte en lisant le code plutôt que dans une doc publique. Les deux vivent en parallèle dans le même routeur, avec des conventions différentes (voir plus bas), ce qui a des conséquences concrètes pour le client C#.

Base URL : `https://mods.vintagestory.at`. Toutes les réponses sont `Content-Type: application/json`. Aucun en-tête `Access-Control-Allow-Origin` observé sur aucun endpoint testé (sans intérêt pour un client desktop C#, mais ça explique pourquoi VintagePack, qui tourne dans un navigateur, doit passer par un proxy Vercel plutôt que d'appeler l'API directement).

---

## Endpoints publics v1 (l'API historique, aucune authentification)

Tout vit dans `lib/api/v1/entry.php` (qui inclut `functions.php` puis `logic.php`). Le dispatch complet est le `switch` de `lib/api/v1/logic.php` lignes 9-114.

Point commun à **tous** ces endpoints, détaillé dans la section erreurs plus bas : le vrai code HTTP retourné est presque toujours `200`, et le succès/échec se lit dans un champ JSON `statuscode` (une **chaîne**, pas un nombre).

### `GET /api/mods` : liste/recherche de mods

Implémenté par `listMods()`, `lib/api/v1/functions.php` lignes 148-260.

Paramètres de requête (tous optionnels, tous en `GET`) :

| Paramètre | Type | Effet |
|---|---|---|
| `text` | string | Sous-chaîne, insensible à la casse, sur `name` OU la description (`text`) de l'asset. `LIKE %...%`, caractères spéciaux `%`/`_` échappés (fonctions.php:166-170). |
| `tagids[]` | int[] (répéter le paramètre) | Un `EXISTS` par tag fourni : les tags sont **combinés en ET**, pas en OU (fonctions.php:172-177). Les ids viennent de `/api/tags`. |
| `author` | int (userId) | Égalité exacte sur l'auteur. **C'est un id numérique, pas un nom** : il faut d'abord résoudre le nom via `/api/authors?name=...` (fonctions.php:179-182). |
| `gameversion` | string `"X.Y"` | Version majeure à deux composants seulement (regex `^(\d+)\.(\d+)$`). Matche **toutes** les release compatibles avec n'importe quel patch de cette branche, via la table cache `modCompatibleMajorGameVersionsCached` (fonctions.php:184-187, `lib/version.php` `compilePrimaryVersion` lignes 46-52). |
| `gv` | string `"X.Y.Z[-suffixe]"` | Version exacte, valeur unique. Prioritaire sur `gameversions` si les deux sont fournis (fonctions.php:190-197). |
| `gameversions[]` | string[] (répéter le paramètre) | Versions exactes, combinées en OU. Ignoré si `gv` est présent. |
| `orderby` | string | Un de `asset.created` (défaut), `lastreleased`, `downloads`, `follows`, `comments`, `trendingpoints`. **Une valeur non reconnue est silencieusement ignorée** (retombe sur le défaut), pas d'erreur 400 (fonctions.php:154-160). |
| `orderdirection` | `asc`\|`desc` | Défaut `desc`. Toute valeur autre que la chaîne exacte `"asc"` devient `desc`, silencieusement aussi (fonctions.php:162-164). |

Détail curieux trouvé dans le code mais probablement inutile pour Prospect : `gv`/`gameversion(s)` acceptent aussi une valeur commençant par `-` (ex. `gv=-281492156858370`), traitée comme un entier brut déjà compilé plutôt qu'une chaîne à parser (`parseVersion`/`parsePrimaryVersion`, fonctions.php:139-146). Ça correspond exactement aux `tagid` négatifs renvoyés par `/api/gameversions` (voir plus bas) : c'est un raccourci pour l'UI web qui répercute tel quel un tagid déjà connu. Un client C# n'a aucune raison de s'en servir : passer directement des chaînes de version normales suffit.

**Aucune pagination.** Vérifié en direct : `GET /api/mods` sans aucun filtre renvoie la totalité du catalogue, 7994 mods, 3,5 Mo de JSON en une seule réponse, en un seul appel. Ni `page`, ni `limit`, ni `offset` nulle part dans le code. Même filtré, il n'y a pas de découpage : le serveur renvoie tout ce qui matche, d'un coup.

Échantillon réel tronqué (un seul mod sur les 7994) :

```json
{
  "modid": 792,
  "assetid": 3829,
  "downloads": 1095320,
  "follows": 8133,
  "trendingpoints": 0,
  "comments": 1440,
  "name": "BetterRuins",
  "summary": "Adds many new ruins over and underground to your survival game world.",
  "modidstrs": ["betterruins"],
  "author": "NiclAss",
  "urlalias": "betterruins",
  "side": "both",
  "type": "mod",
  "logo": "https://moddbcdn.vintagestory.at/BetterRuinsmoddbicon_021a6a8838be7058c2ab618ce0097224_480_320.png",
  "tags": ["Crafting", "Exploration", "Lore", "Story", "Structures", "Travel & Exploration", "Worldgen"],
  "lastreleased": "2026-07-28 18:59:32"
}
```

Champs observés sur les 7994 mods (analyse du dump complet, pas juste lecture du code) :

- `type` : seulement 3 valeurs possibles, `mod` (7866), `other` (65), `externaltool` (63), dérivées de `category` via `mapCategoryToType()`, fonctions.php:262-276.
- `side` : seulement 3 valeurs observées, `both` (6434), `client` (804), `server` (756). Colonne `mods.side`, `ENUM('client','server','both')` (`db/000_tables.sql` ligne 210) : **`both`, pas `universal`**, voir la section modinfo.json pour pourquoi ça compte.
- `logo` : `null` sur 2578 mods sur 7994 (32%). Présent = URL absolue complète sur `moddbcdn.vintagestory.at`.
- `urlalias` : `null` sur 3218 mods (40%), uniquement les mods dont l'auteur a défini un alias d'URL personnalisé.
- `modidstrs` : tableau, **peut être vide** (131 mods, presque tous `type=externaltool`/`other`, des outils sans modinfo.json puisqu'ils ne sont pas installés dans le jeu ; 10 exceptions de `type=mod`, probablement des releases dont `modReleases.identifier` est `NULL`, colonne explicitement marquée nullable en base avec un commentaire `-- TODO`, `db/000_tables.sql` ligne 257), **et peut contenir plusieurs entrées** (327 mods, ex. « Auto Map Markers » → `["egocaribautomapmarkers", "egocaribresinmapmarkers"]` : une seule fiche ModDB peut correspondre à plusieurs identifiants modinfo.json distincts).
- `tags` (catégories, pas versions) : peut valoir `[""]` (un tableau contenant une chaîne vide) au lieu de `[]` quand le mod n'a aucun tag voté. C'est un artefact de `explode(',', $row['tags'])` appliqué à un `GROUP_CONCAT` qui vaut `NULL` en SQL quand il n'y a rien à concaténer (fonctions.php ligne 254). Observé sur 838 mods sur 7994. **Piège** : ça n'arrive pas sur l'endpoint détail (voir plus bas), le client doit filtrer les chaînes vides des deux côtés pour rester cohérent.

### `GET /api/mod/{id}` : détail d'un mod

Implémenté par `listMod($modid)`, fonctions.php lignes 3-137.

`{id}` accepte **deux formats**, résolus par ce test (fonctions.php lignes 7-13) :

```php
if ($modid != "" . intval($modid)) {
    $modid = $con->getOne(<<<SQL
        SELECT r.modId FROM modReleases r
        LEFT JOIN modReleaseRetractions rr ON rr.releaseId = r.releaseId
        WHERE r.identifier = ? AND rr.reason IS NULL
    SQL, array($modid));
}
```

- un entier : le `modid` numérique interne (`/api/mod/1783`)
- une chaîne non numérique : traitée comme un `identifier` modinfo.json (le `modid` du mod, ex. `/api/mod/configlib`). **Confirmé en direct** : les deux renvoient le même mod (`modid: 1783, name: "Config lib"`).

Attention, ce n'est **pas** la même chose que `urlalias` : `urlalias` est une colonne à part sur `mods`, utilisée uniquement par le routeur de pages HTML (`index.php` lignes 94-99) pour les URLs façon `mods.vintagestory.at/betterruins`, jamais consultée par `listMod()`. Les deux valeurs coïncident souvent par convention (l'auteur choisit le même mot pour les deux) mais rien ne le garantit. **Pour Prospect : n'utiliser que le `modid` numérique ou le `modidstr` issu d'un modinfo.json, jamais un `urlalias` deviné.** Autre nuance mineure trouvée dans la requête SQL ci-dessus : pas de `LIMIT`/`ORDER BY` sur la résolution par identifiant, donc en théorie si deux mods différents avaient un jour utilisé la même chaîne d'identifiant dans leur historique de releases, la résolution prendrait l'un des deux de façon non garantie. Cas d'école, pas rencontré en pratique.

Mod inconnu → `{"statuscode":"404"}` avec un vrai code HTTP 200 (voir section erreurs).

Forme complète de la réponse, avec un vrai mod (**Config lib**, modid 1783, choisi comme échantillon parce qu'il a 85 releases et sert aussi à la section modinfo.json) :

```json
{
  "mod": {
    "modid": 1783,
    "assetid": 9551,
    "name": "Config lib",
    "text": "<h2>...</h2><p>...</p>",
    "author": "Maltiez",
    "urlalias": null,
    "logofilename": "https://moddbcdn.vintagestory.at/...",
    "logofile": "https://moddbcdn.vintagestory.at/...",
    "logofiledb": null,
    "homepageurl": "",
    "sourcecodeurl": "",
    "trailervideourl": "",
    "issuetrackerurl": "",
    "wikiurl": "",
    "downloads": 627953,
    "follows": 0,
    "trendingpoints": 0,
    "comments": 0,
    "side": "both",
    "type": "mod",
    "created": "...",
    "lastreleased": "2026-05-01 12:03:34",
    "lastmodified": "...",
    "tags": ["..."],
    "releases": [ /* voir section suivante */ ],
    "screenshots": [ /* voir plus bas */ ]
  }
}
```

Points notables sur les champs non liés aux releases :

- `text` est du **HTML brut** issu d'un éditeur WYSIWYG (`<h2>`, `<p>`, `<a href>`, `<img>` observés), pas du texte plat ni du Markdown. Un client qui veut afficher la description doit soit le rendre en HTML soit le nettoyer.
- `homepageurl`, `sourcecodeurl`, `trailervideourl`, `issuetrackerurl`, `wikiurl` : colonnes nullables en base (`db/000_tables.sql` lignes 198-203) mais **observées comme chaîne vide `""` en pratique quand non renseignées, pas comme `null`**. `logofiledb`, lui, a bien été observé à `null`. Ne pas supposer une convention uniforme : traiter `""` et `null` comme équivalents pour tous les champs URL optionnels.
- `logofilename` est marqué `@obsolete` dans le code lui-même avec le commentaire « This is not the filename, but just the link again » (fonctions.php ligne 111) : c'est en fait une deuxième copie de `logofile`, à ignorer.
- `tags` ici (catégories du mod) utilise `getCol()` sur une requête sans `GROUP_CONCAT` (fonctions.php lignes 37-42) : un mod sans tag donne `[]`, **pas** `[""]` comme sur `/api/mods`. Incohérence confirmée entre les deux endpoints pour le même cas (aucun tag).

**Screenshots** (fonctions.php lignes 91-100) :

```json
{
  "fileid": 85349,
  "mainfile": "https://moddbcdn.vintagestory.at/moddb-logo_34c8ef8aaa9e517fc2c99668ad828752.png",
  "filename": "moddb-logo.png",
  "thumbnailfilename": "https://moddbcdn.vintagestory.at/moddb-logo_..._55_60.png",
  "created": "2026-04-16 18:26:45"
}
```

`thumbnailfilename` est `null` si le fichier n'a pas de miniature générée (`hasThumbnail` faux). Sans intérêt direct pour un launcher, mentionné pour être complet.

### `GET /api/tags` : vocabulaire des tags de catégorie

`lib/api/v1/logic.php` lignes 10-13. Aucun paramètre.

```json
{
  "statuscode": "200",
  "tags": [
    {"tagid": "467", "name": "Absolute Cinema", "color": "#92C96AFF"},
    {"tagid": "285", "name": "Accessibility", "color": "#92C96AFF"}
  ]
}
```

**`tagid` est une chaîne JSON**, pas un nombre (`SELECT tagId as tagid, ...` sans `intval()`, contrairement à presque tous les autres ids de l'API). `color` fait 8 caractères hex après le `#`, format `RRGGBBAA` (alpha en dernier, quasi toujours `FF` = opaque dans l'échantillon observé).

### `GET /api/gameversions` : vocabulaire des versions de jeu

`lib/api/v1/logic.php` lignes 15-27. Aucun paramètre. Trié par version croissante.

```json
{
  "statuscode": "200",
  "gameversions": [
    {"tagid": -281492156858370, "name": "1.4.4-dev.2", "color": "#CCCCCC"},
    {"tagid": -281496452136959, "name": "1.5.8", "color": "#CCCCCC"}
  ]
}
```

Deux pièges concrets ici :

1. **`tagid` est cette fois un vrai nombre JSON**, l'opposé exact de `/api/tags`. C'est `-intval($version)`, où `$version` est la version compilée sur 64 bits (voir encodage plus bas) : les valeurs sortent largement hors de portée d'un `int32` (`-281492156858370` dans l'échantillon). **Un client C# doit désérialiser ce champ en `long`, pas en `int`**, sous peine d'overflow/exception.
2. `color` vaut toujours la constante `'#CCCCCC'` codée en dur (logic.php ligne 22) : ce n'est pas une vraie couleur par version, juste un gris de remplissage pour que la forme de l'objet matche celle de `/api/tags`. Ne rien construire dessus.

### `GET /api/authors` : recherche d'utilisateurs

`lib/api/v1/logic.php` lignes 40-53.

| Paramètre | Effet |
|---|---|
| `name` | Sous-chaîne insensible à la casse (tronquée à 20 caractères en entrée), limite 10 résultats, exclut les comptes actuellement bannis. |
| *(absent)* | **Renvoie la totalité des comptes utilisateurs, sans limite.** |

Le deuxième cas n'est pas théorique : vérifié en direct, `GET /api/authors` sans paramètre a renvoyé **110998 comptes, ~4 Mo de JSON**. C'est un endpoint « liste tous les utilisateurs du site » qui ne dit pas son nom. **Toujours passer `?name=`.**

```json
{"statuscode":"200","authors":[{"userid":29859,"name":"Rennorb"}]}
```

### `GET /api/updates` : vérification de version en masse

`lib/api/v1/logic.php` lignes 95-111, implémenté par `listOutOfDateMods()`, fonctions.php lignes 278-332. **C'est l'endpoint taillé sur mesure pour le détecteur de mise à jour de Prospect.**

Paramètre unique : `mods`, une liste `modidstr@version` séparée par des virgules (le `version` est celui **déjà installé**, ex. `configlib@1.0.0,jsonpatcheslib@1.0.0`). Toute entrée mal formée (pas de `@`) fait échouer toute la requête en 400.

Testé en direct avec des versions volontairement anciennes :

```json
{
  "statuscode": "200",
  "updates": {
    "configlib": {
      "releaseid": 39980,
      "mainfile": "https://moddbcdn.vintagestory.at/configlib_1.12.0_....zip?dl=configlib_1.12.0.zip",
      "filename": "configlib_1.12.0.zip",
      "fileid": 88961,
      "downloads": 118728,
      "tags": ["1.22.0-pre.1", "...", "1.22.0", "1.22.1"],
      "modidstr": "configlib",
      "modversion": "1.12.0",
      "created": "2026-05-01 12:03:34"
    }
  }
}
```

Comportement exact à connaître :

- La clé de retour `updates` est une **map par `modidstr`, uniquement pour les mods réellement en retard**. Un mod à jour n'apparaît tout simplement pas dans la réponse : **l'absence ne distingue pas « à jour » de « `modidstr` inconnu du ModDB »**, ce que le client doit garder à l'esprit (comparer les clés présentes à la liste envoyée plutôt que de supposer qu'absence = à jour à coup sûr).
- La comparaison se fait release par release, `modId` par `modId`, mais **sur le dernier `identifier` rencontré dans les résultats triés** (fonctions.php lignes 306-315, commentaire du code : la boucle saute les lignes tant que `identifier` ne change pas). Implication pratique : si un même `modidstr` a été réutilisé par accident sur deux `modId` différents, seul le premier groupe rencontré dans le tri SQL (`ORDER BY r.identifier, r.version DESC`) est retenu.
- Forme de l'objet retourné = même forme que `mod.releases[i]` sur `/api/mod/{id}`, **sauf `changelog`, absent ici** (comparer les deux blocs de code, fonctions.php lignes 63-74 vs 317-328).

### `GET /api/comments` et `GET /api/comments/{assetId}`

`lib/api/v1/logic.php` lignes 55-82. Sans filtre : 100 derniers commentaires tous assets confondus. Avec un `assetId` numérique dans l'URL : tous les commentaires de cet asset, sans limite. Pas testé en direct (hors périmètre de Prospect), documenté depuis le code uniquement.

### `GET /api/changelogs` : endpoint retiré

`lib/api/v1/logic.php` lignes 84-93. Confirmé en direct : renvoie toujours la même charge utile, avec un `statuscode` JSON `"410"` (vrai code HTTP toujours 200) :

```json
{"reason":"This information was previously available, but is no longer distributed. Version 2 of the api might provide this information at some point in the future.","statuscode":"410"}
```

Réponse mise en cache une semaine côté serveur (`Cache-Control: max-age=604800, immutable`, logic.php ligne 86). Le changelog par version existe toujours, mais uniquement au niveau de chaque release individuelle (`mod.releases[i].changelog` sur `/api/mod/{id}`), pas via cet endpoint global.

---

## Endpoints publics v2 (plus riches, en partie sans authentification)

Point de départ commun : `lib/api/v2.php`, qui route vers `lib/api/public/_routing.php` (lignes 63-64). **Contrairement à ce que suppose le commentaire de VintagePack** (`src/services/api.ts` ligne 3 : « v2 nécessite une auth »), une partie de v2 est en fait publique : tout ce qui est câblé dans `lib/api/public/_routing.php` (lignes 1-21) ne demande aucune session. Seul ce qui tombe ensuite dans `lib/api/authenticated/_routing.php` (lignes 1-42, garde `if(empty($user)) fail(401)` en ligne 3) exige un cookie de session issu d'un vrai login sur le site : mods en POST/PUT, `game-versions` en écriture, `comments`, `notifications`. Rien de tout ça n'est utilisable par un client anonyme, donc hors périmètre pour Prospect (et interdit de toute façon : pas d'authentification).

Différence structurelle importante avec v1 : **v2 renvoie de vrais codes HTTP.** `lib/api/v2.php` lignes 7-12 :

```php
function fail($statuscode, $data = null)
{
	header('Content-Type: application/json');
	http_response_code($statuscode);
	exit(($data !== null) ? json_encode($data) : '{}');
}
```

### `GET /api/v2/mods/{modId}/releases` : releases d'un mod, format compact

`lib/api/public/mods.php` lignes 287-332. `{modId}` doit être le **numérique** (pas d'identifier ici, `filter_var($urlparts[0], FILTER_VALIDATE_INT)` ligne 283). 404 si le mod n'existe pas ou n'est pas publié.

Testé en direct sur configlib (`/api/v2/mods/1783/releases`) :

```json
{
  "39980": {"identifier": "configlib", "version": "1.12.0"},
  "38314": {"identifier": "configlib", "version": "1.11.1"}
}
```

Objet (pas tableau) **volontairement forcé même à une seule entrée** via le flag `JSON_FORCE_OBJECT` (mods.php ligne 332), clé = `releaseId` en chaîne. Beaucoup plus léger que `/api/mod/{id}` puisqu'il ne contient ni fichier, ni tags de version de jeu, ni changelog : utile seulement pour lister rapidement les couples `(releaseId, version)` d'un mod.

Query param `ignore-retractions` (bool) pour inclure les releases retirées par un modérateur.

### `GET /api/v2/mods/{modId}/releases/latest` et `/api/v2/mods/{modId}/releases/{releaseId}`

Même fichier, lignes 334-395. `latest` accepte un paramètre `identifier` optionnel (utile si le mod a plusieurs `modidstr` sur une même fiche) et retourne la release la plus haute en version.

Testé en direct (`/api/v2/mods/1783/releases/latest`) :

```json
{
  "releaseId": 39980,
  "identifier": "configlib",
  "version": "1.12.0",
  "compatibleGameVersions": ["1.22.1", "1.22.0", "1.22.0-rc.10", "...", "1.22.0-pre.1"],
  "created": 1777637014,
  "fileName": "configlib_1.12.0.zip",
  "fileUrl": "/download/88961/configlib_1.12.0.zip"
}
```

Deux différences importantes avec les champs équivalents en v1, à ne pas mélanger dans un même modèle C# :

- **`created` est un timestamp Unix (nombre entier)**, alors que partout en v1 c'est une chaîne SQL `"YYYY-MM-DD HH:MM:SS"`.
- **`fileUrl` est un chemin relatif** vers l'endpoint de tracking (`/download/{fileid}/{nom}`), pas une URL CDN absolue comme le `mainfile` de v1. Il faut le préfixer avec `https://mods.vintagestory.at` et s'attendre à une redirection 302 (détails dans la section téléchargement).

`compatibleGameVersions` utilise le même format de chaînes exactes que `releases[].tags` en v1 (voir section suivante).

### `GET /api/v2/mods/install-information` : résolution de version cible, potentiellement très utile pour Prospect

`lib/api/public/mods.php` lignes 20-279. Le plus riche des endpoints v2 publics, pensé pour un launcher : donne, pour une liste de mods et une version de jeu cible, soit le fichier exact demandé, soit une recommandation de mise à niveau.

Paramètres : `ids` (`modidstr[@version]` séparés par virgules ; sans `@version`, il faut fournir `gv`), `gv` (version de jeu cible, optionnel si toutes les entrées de `ids` précisent leur version), `ignore-retractions`, `resolve-deps` (bool, déclenche une résolution de dépendances transitives via `lib/relations.php`), `hosted-mode`.

Testé en direct (`ids=configlib@1.0.0&gv=1.22.0`) :

```json
{
  "data": {
    "configlib": {
      "recommendedUpgrade": "1.12.0",
      "fileName": "configlib_1.0.0.zip",
      "fileUrl": "/download/19456/configlib_1.0.0.zip"
    }
  }
}
```

Même en demandant une version obsolète (`1.0.0`, qui existe toujours et reste téléchargeable), le fichier exact demandé est retourné, avec en prime `recommendedUpgrade` pointant vers la version conseillée pour la `gv` cible. C'est potentiellement une meilleure primitive que `/api/updates` pour Prospect, parce qu'elle raisonne en plus par version de jeu cible (utile pour une instance figée sur une ancienne version du jeu, où on ne veut pas forcément « la dernière release tout court » mais « la dernière compatible avec ma version de jeu »). Le flag `resolve-deps` est documenté dans le code (ajoute des clés `resolved`/`warnings`, logique dans `lib/relations.php`) mais je n'ai pas obtenu d'échantillon live où ces clés apparaissent (le mod testé n'avait apparemment rien à résoudre en plus) : à revérifier avec un mod qui a des dépendances non triviales avant de s'appuyer dessus.

Codes d'erreur spécifiques à cet endpoint, tous documentés dans le code (mods.php lignes 5-11) et renvoyés par entrée plutôt que pour toute la requête : `4001` spec illisible, `4002` version manquante sans `gv` de secours, `4031` interdit en mode hébergé, `4032` retrait non contournable, `4041` spec introuvable, `4101`/`4102` release retirée (contournable ou non selon qui a fait le retrait).

### `GET /api/v2/tags/by-name/{q}` et `GET /api/v2/users/by-name/{q}`

`lib/api/public/tags.php` et `lib/api/public/users.php`. Recherche par préfixe/sous-chaîne avec un paramètre `limit` (défaut 10, max 200) et une vraie erreur `400 Bad Request` sur une recherche vide ou un `limit` invalide, contrairement à v1 qui ignore silencieusement les paramètres invalides. Pas creusé plus loin, redondant avec `/api/tags` et `/api/authors` de v1 pour les besoins de Prospect.

---

## Comment les releases sont taguées par version de jeu (le cœur du détecteur de mises à jour)

C'est la question la plus importante pour Prospect, donc à part.

**Le tag de compatibilité par release est une métadonnée éditoriale, choisie à la main par l'auteur au moment de l'upload sur le site : ce n'est pas recalculé depuis le modinfo.json du fichier téléchargé.** Preuve dans le code d'édition d'une release, `edit-release.php` ligne 308 :

```php
'compatibleGameVersions' => empty($_POST['cgvs']) ? [] : array_flip(array_filter(array_map('compileSemanticVersion', $_POST['cgvs']))),
```

`$_POST['cgvs']` est une liste de cases à cocher sur le formulaire web (une par version de jeu connue). Il existe bien une pré-sélection automatique suggérée à l'auteur, dérivée du `Dependencies["game"]` du modinfo.json qu'il vient d'uploader (`edit-release.php` lignes 317-324, s'appuyant sur `findMinCompatibleGameVersion()`, `lib/modinfo.php` lignes 123-132) : elle coche par défaut *toutes* les versions de jeu connues à partir de la version minimale déclarée. Mais ce n'est qu'une pré-sélection modifiable avant soumission, jamais une contrainte forcée. Autrement dit : **il n'y a aucune garantie que les tags de version d'une release correspondent exactement, ni même correctement, à ce que le modinfo.json embarqué déclare.** C'est du déclaratif humain, avec les oublis que ça implique (un auteur qui oublie de cocher la nouvelle version de jeu après une mise à jour du moteur, par exemple).

Stocké en base dans `modReleaseCompatibleGameVersions(releaseId, gameVersion)`, une ligne par version taguée (`db/000_tables.sql` lignes 328-333), avec deux tables de cache dénormalisées pour les filtres de recherche (`modCompatibleGameVersionsCached`, `modCompatibleMajorGameVersionsCached`, lignes 335-351).

**Format exact tel qu'exposé par l'API** (`releases[].tags` en v1, `releases[].compatibleGameVersions` en v2) : un tableau de chaînes de version **complètes et exactes**, jamais de plage, jamais de wildcard. Échantillon réel (configlib 1.12.0) :

```json
["1.22.0-pre.1", "1.22.0-pre.2", "1.22.0-pre.3", "1.22.0-pre.4", "1.22.0-pre.5",
 "1.22.0-rc.1", "1.22.0-rc.2", "...", "1.22.0-rc.10", "1.22.0", "1.22.1"]
```

Une entrée par version de jeu individuellement cochée, y compris les pré-releases (`-pre.N`, `-rc.N`, `-dev.N`) si l'auteur les a cochées. Le nombre d'entrées peut être long (17 ici) : une release qui reste compatible sur plusieurs versions du jeu liste chaque version une par une, pas une plage `"1.20-1.22"`.

Ces chaînes sortent du même formateur que les versions de mod, `formatSemanticVersion()` (`lib/version.php` lignes 58-71), qui décode un entier 64 bits packé `Major(16) | Minor(16) | Patch(16) | Suffixe(16)` (détail lignes 3-37). Sans intérêt pour le client C#, **sauf** pour comprendre pourquoi `gameversions[].tagid` (section précédente) est un si grand nombre négatif : c'est directement `-` cette valeur compilée.

---

## Téléchargement des fichiers de release

Deux chemins, qui convergent vers la même URL finale (**vérifié en suivant la redirection avec `curl -I`**) :

**1. Lien direct (v1 `mainfile`, déjà l'URL finale)**, produit par `formatCdnDownloadUrl()` (`lib/cdn/bunny.php` lignes 166-171 en production) :

```
https://moddbcdn.vintagestory.at/{chemin_stockage}?dl={nom_fichier}
```

**2. Lien de tracking (v2 `fileUrl`, relatif)**, servi par `download.php` (racine du dépôt, 41 lignes), qui incrémente les compteurs de téléchargement puis redirige :

```php
header('Location: '. formatCdnDownloadUrl($file), true, HTTP_FOUND); // 302
```

Confirmé en direct sur `GET /download/111247/jsonpatcheslib_1.5.6.zip` :

```
HTTP/2 302
location: https://moddbcdn.vintagestory.at/jsonpatcheslib_1.5.6_....zip?dl=jsonpatcheslib_1.5.6.zip
```

`?fileid=` en query string marche aussi (compat historique avec l'ancien launcher du jeu, commentaire explicite dans `download.php` ligne 4). Aucune authentification requise sur aucun des deux chemins.

**Le CDN final est BunnyCDN**, confirmé par les en-têtes de réponse sur `moddbcdn.vintagestory.at` (`server: BunnyCDN-FR1-1349`, `cdn-pullzone`, `cdn-cache`, etc. ; `moddbcdn.vintagestory.at` est un nom d'hôte personnalisé en façade de la pull zone Bunny). Points utiles pour un client de téléchargement :

- `Content-Disposition: attachment; filename="..."` toujours présent (règle Bunny documentée dans `lib/cdn/bunny.php` lignes 21-29 : forcer le téléchargement plutôt qu'un affichage inline).
- `Accept-Ranges: bytes` présent → **les téléchargements reprenables (Range requests) sont possibles**, utile pour une reprise après coupure.
- **Aucune taille de fichier, aucun hash/checksum n'est exposé nulle part dans l'API JSON** (vérifié par recherche dans tout `lib/api/`, rien). Le champ `filesize` du type `ModFile` de VintagePack existe côté client mais reste toujours à `0`, jamais rempli depuis l'API : une confirmation indépendante que cette donnée n'existe simplement pas côté serveur. Pour connaître une taille avant de télécharger, il faut un `HEAD` sur l'URL CDN et lire `Content-Length` (vérifié : `configlib_1.12.0.zip` → 198885 octets, `jsonpatcheslib_1.5.6.zip` → 57242 octets, `ExtraInfo-v2.2.1.zip` → 80671 octets).
- `Cache-Control: public, max-age=2592000` (30 jours) sur les fichiers CDN : cohérent avec le commentaire du code disant que les fichiers sont immuables une fois uploadés (un nom de fichier sur le CDN inclut un hash du contenu, donc une nouvelle release = un nouveau chemin, jamais une réécriture en place).

---

## Gestion d'erreurs et rate limiting

**Le piège le plus important de toute cette API : sur v1, le vrai code HTTP ne reflète presque jamais l'erreur.** `lib/api/v1/functions.php` lignes 7-11 :

```php
function fail($statuscode)
{
	exit(json_encode(array("statuscode" => $statuscode)));
}
```

Pas d'appel à `http_response_code()`. PHP renvoie donc `200 OK` par défaut, quelle que soit la valeur de `$statuscode` encodée dans le corps. **Confirmé en direct sur trois cas différents** : `/api/mod/999999999` (id numérique inexistant), `/api/mod/this-mod-does-not-exist-xyz` (identifiant inexistant), `/api/changelogs` (endpoint retiré) → les trois répondent avec l'en-tête `HTTP/2 200`, et le seul indicateur d'échec est le champ JSON :

```json
{"statuscode":"404"}
```

`statuscode` est une **chaîne**, pas un nombre (`"404"`, `"410"`, jamais `404`/`410` nus), y compris pour les réponses de succès (`"200"`, toujours en chaîne aussi puisque `good()` écrase systématiquement le champ avec la valeur par défaut de son paramètre, fonctions.php ligne 14-18). **Un client C# ne doit jamais se fier à `HttpResponseMessage.IsSuccessStatusCode` sur `/api/*` (v1) : il faut désérialiser le corps et lire `statuscode`.**

`/api/v2/*` corrige ce défaut : `lib/api/v2.php` lignes 7-12 appelle bien `http_response_code($statuscode)`. Les codes d'erreur y sont donc fiables au sens HTTP classique.

**Aucun rate limiting applicatif trouvé dans le code**, ni dans `lib/` (recherche de `ratelimit`/`throttle`/`X-RateLimit` : rien), ni dans la config nginx (`docker/moddb.conf`, pas de `limit_req`/`limit_conn`). Le seul `Retry-After` du code sert à signaler un mode maintenance global en lecture seule (`lib/core.php` ligne 910, `lib/api/v2.php` lignes 38-47), pas un quota par client. Aucun en-tête `X-RateLimit-*` observé sur aucune des ~20 requêtes de cette session. Ça ne veut pas dire qu'il n'existe aucune limite en amont (Cloudflare/nginx au niveau infra, invisible depuis ce dépôt) : Prospect devrait quand même envoyer un `User-Agent` identifiable et espacer ses appels par prudence, mais rien dans le code ne force un backoff précis.

Autres cas d'erreur documentés depuis le code (non testés tous en direct par souci de volume de requêtes) : paramètre manquant sur `/api/updates` (pas de `@` dans une entrée) → 400 ; `/api/mod` sans id → 400 ; toute route non reconnue → 400 par défaut de fin de `switch` (`lib/api/v1/logic.php` ligne 114).

---

## Le format modinfo.json

### Échantillons réels

Trois mods **populaires et petits** téléchargés et vérifiés en taille avant de tirer quoi que ce soit (`HEAD` d'abord, taille confirmée par `Content-Length`, puis téléchargement) :

| Mod | Téléchargements | Taille du zip |
|---|---|---|
| Config lib (`configlib`) | 627 953 | 194 KiB |
| JSON Patches lib (`jsonpatcheslib`) | 204 295 | 56 KiB |
| Extra Info (`extrainfo`) | 174 994 | 79 KiB |

`modinfo.json` de **Config lib** (racine du zip) :

```json
{
    "type": "code",
    "name": "Config lib",
    "modid": "configlib",
    "version": "1.12.0",
    "description": "A universal place to configure your mods. Makes it possible for content mods to have configs too.",
    "authors": [ "Maltiez", "The Insanity God" ],
    "dependencies": {
        "vsimgui": "1.2.0"
    },
    "side" : "universal",
    "requiredOnClient": true,
    "requiredOnServer": false
}
```

`modinfo.json` de **JSON Patches lib** (dépendances vides, notez la casse et la ponctuation identiques au premier malgré des auteurs différents : indentation à 2 espaces ici contre 4 chez configlib, formatage clairement pas homogène d'un mod à l'autre) :

```json
{
  "type": "code",
  "name": "JSON Patches lib",
  "modid": "jsonpatcheslib",
  "version": "1.5.6",
  "description": "Implements more convenient syntax and more functionality for patching JSON assets.",
  "authors": [ "Maltiez" ],
  "dependencies": {

  },
  "side" : "universal",
  "requiredOnClient": true,
  "requiredOnServer": true
}
```

`modinfo.json` de **Extra Info** (le seul des trois avec `contributors`, et le seul à déclarer une dépendance sur `game`) :

```json
{
    "type": "code",
    "side": "client",
    "modid": "extrainfo",
    "name": "Extra Info",
    "description": "Useful information for handbook, blocks, items and entities",
    "authors": ["Craluminum2413 (Dana)"],
    "contributors": ["Novocain"],
    "version": "2.2.1",
    "dependencies": {
        "game": "1.22.0"
    }
}
```

Note en passant : la version dans `modinfo.json` d'Extra Info est `"2.2.1"` sans préfixe, alors que le nom du zip téléchargé est `ExtraInfo-v2.2.1.zip` (avec un `v`). **Ne jamais dériver la version depuis le nom de fichier : toujours lire le champ `version` du modinfo.json (ou `modversion` côté API), qui lui est toujours nu.**

### Table des champs (croisement wiki + doc XML de l'assembly du jeu + échantillons)

Le champ le plus fiable est la doc XML embarquée dans `util/VintagestoryAPI.xml` (documentation `///` du code source, compilée avec l'assembly officielle) : c'est la classe `Vintagestory.API.Common.ModInfo` elle-même, celle vers laquelle `modinfo.json` est désérialisé par le jeu (confirmé aussi par le wiki : « loaded into that class internally using `JsonConvert.DeserializeObject` »). Recoupée avec [wiki.vintagestory.at/Modding:Modinfo](https://wiki.vintagestory.at/Modding:Modinfo) et les trois échantillons ci-dessus.

| Champ | Présent dans les 3 échantillons ? | Défaut si absent | Remarques |
|---|---|---|---|
| `type` | Oui, toujours `"code"` | aucun | 3 valeurs possibles : `Theme`, `Content`, `Code` (`VintagestoryAPI.xml`, membre `EnumModType`). Observé en minuscules dans le JSON (`"code"`) alors que l'enum C# est `PascalCase` : la désérialisation est insensible à la casse. |
| `name` | Oui | aucun | Libre. |
| `modid` (ou `modId`, `Modid`...) | Oui (les 3) | dérivé de `name` en minuscules, sans espaces ni caractères spéciaux | **Optionnel** d'après la doc de la classe (`ModInfo.ModID`, `VintagestoryAPI.xml`). Validé par une règle précise documentée sur la même classe : non vide, commence par une lettre minuscule, ne contient que lettres minuscules et chiffres. |
| `version` | Oui | aucun | Doc XML : « optional » côté classe C#, mais en pratique quasi indispensable (un mod sans version publiable sur le ModDB n'a pas vraiment de sens, et le formulaire d'upload du site s'appuie dessus). |
| `networkVersion` | Non (aucun des 3) | valeur de `version` | Sert à invalider la compatibilité réseau indépendamment du numéro de version affiché. |
| `description` | Oui (les 3) | aucun | Optionnel. |
| `website` | Non | aucun | Optionnel. |
| `iconPath` | Non | tente `./modicon.png` à la racine avant d'abandonner | Optionnel. |
| `authors` | Oui (les 3) | `[]` | Toujours un tableau, même pour un seul auteur (vu dans les 3 échantillons, et explicite dans le wiki : « must be formatted as array even for single author »). |
| `contributors` | 1 sur 3 (Extra Info) | `[]` | Optionnel, absent chez les deux autres. |
| `side` | Oui (les 3) | `"Universal"` | 3 valeurs : `Server`, `Client`, `Universal`. **Observées en minuscules dans le JSON réel** (`"universal"`, `"client"`) alors que la doc écrit les valeurs en `PascalCase` : encore une fois insensible à la casse en lecture. |
| `requiredOnClient` | 2 sur 3 | **`true`** | Concerne uniquement les mods `side: universal`. Pas de valeur chez Extra Info (`side: client`, le champ n'a pas de sens dans ce cas). |
| `requiredOnServer` | 2 sur 3 | **`true`** | Idem. |
| `dependencies` | Oui (les 3, un vide) | `{}` | Voir ci-dessous, c'est le champ le plus important pour Prospect. |
| `textureSize` | Non | 32 | Uniquement pour les packs de texture qui modifient la texture du sol/herbe. Cas marginal. |

### `dependencies` : objet, pas tableau, et sémantique « version minimale »

`dependencies` est un **objet JSON `{modid: version}`**, pas un tableau d'objets `{id, version}`. Confirmé dans les trois échantillons (`{"vsimgui": "1.2.0"}`, `{}`, `{"game": "1.22.0"}`).

La sémantique de la valeur `version` est documentée explicitement et sans ambiguïté sur la classe `ModDependency` elle-même (`VintagestoryAPI.xml`, et confirmé par la version en ligne de la doc API sur `apidocs.vintagestory.at`) : c'est une **version minimale requise**, jamais une version exacte ni une contrainte de plage. Une valeur vide (`""`) signifie qu'aucune version particulière n'est requise. Le wiki confirme et ajoute que `"*"` est également accepté comme équivalent de « toute version ».

**Sur le format `"1.20.*"` évoqué dans la mission : je ne l'ai trouvé nulle part.** Ni dans les 3 échantillons réels, ni dans la doc de la classe `ModDependency`/`ModInfo` (aucune méthode de comparaison à wildcard documentée, seulement `ToString()`), ni sur la page wiki dédiée qui liste précisément ce qui est supporté (versions exactes comparées sémantiquement, `rc` > `pre` > `dev` pour les pré-releases, `*`/vide pour « toute version ») sans jamais mentionner de glob `X.Y.*`. Le côté ModDB lui-même n'accepterait de toute façon pas cette syntaxe pour ses propres versions : `compileSemanticVersion()` (`lib/version.php` ligne 22) attend strictement `^(\d+)\.(\d+)\.(\d+)(?:-(dev|pre|rc)\.(\d+))?$`, sans étoile possible. **Conclusion : traiter chaque entrée de `dependencies` comme une borne minimale (`>=`) à comparer sémantiquement, avec chaîne vide/`"*"` comme joker « toute version », et ne pas implémenter de parsing de wildcard `X.Y.*` en présumant qu'il existe.**

Deux identifiants spéciaux à connaître dans `dependencies`, qui ne sont **pas** de vrais mods à résoudre : `game` (version minimale du jeu requise, ex. `"game": "1.22.0"` chez Extra Info) et `survival`/`creative` (le mod dépend du contenu bundlé avec ces modes de jeu). Le ModDB lui-même les traite à part et les exclut de la résolution de dépendances mod-à-mod (`lib/relations.php` ligne 13, `IGNORED_AUTO_IDENTIFIERS`).

### `side` : deux vocabulaires différents pour le même concept, à ne pas confondre

Piège concret trouvé en croisant le code et les échantillons : **le `side` renvoyé par `/api/mod/{id}` (niveau fiche ModDB) et le `side` du `modinfo.json` (niveau fichier) n'utilisent pas le même vocabulaire, et ne sont même pas la même donnée.**

- `modinfo.json` : `Client` / `Server` / `Universal` (casse flexible, `Universal` par défaut), c'est ce que **le jeu** lit pour savoir où charger le mod.
- API ModDB (`mods.side`) : `client` / `server` / `both` (jamais `universal`), choisi **manuellement par l'auteur dans un menu déroulant** sur la fiche du mod (`edit-mod.php` lignes 111-115, défaut `both` à la création, ligne 74), complètement indépendant de ce que dit le fichier réellement uploadé. Rien dans le code ne synchronise automatiquement l'un depuis l'autre.

Correspondance conceptuelle (`Universal` ↔ `both`) mais pas garantie en pratique : un auteur pourrait cocher `client` sur sa fiche alors que son dernier modinfo.json dit `Universal`. Pour un détecteur fiable, la seule source correcte de `side` est le `modinfo.json` réellement présent dans l'archive téléchargée, pas le champ `side` de l'API.

---

## Implications pour le client C#

1. **Pas de pagination nulle part sur `/api/mods`** : le catalogue entier (7994 mods, 3,5 Mo) revient en un seul appel, filtré ou non. Mettre en cache localement avec un TTL raisonnable plutôt que de le refaire à chaque lancement ; ne jamais essayer d'implémenter un scroll infini basé sur des pages côté serveur, ça n'existe pas.

2. **Sur `/api/*` (v1), ignorer le code HTTP et lire le champ JSON `statuscode` (chaîne).** Un 404 applicatif renvoie un vrai `HTTP 200`. Modéliser une enveloppe de réponse générique `{ Statuscode: string, ... }` désérialisée dans tous les cas, avant même de regarder `response.IsSuccessStatusCode`. Seul `/api/v2/*` a des codes HTTP fiables.

3. **Types incohérents entre endpoints à modéliser explicitement**, pas avec des `int` partout : `tags[].tagid` est une chaîne, `gameversions[].tagid` est un `long` (peut sortir très loin hors de portée d'un `int32`, ex. `-281492156858370`), `v2 releases/latest.created` est un timestamp Unix numérique alors que tous les `created`/`lastreleased` de v1 sont des chaînes SQL `"YYYY-MM-DD HH:MM:SS"`.

4. **Les tags de version de jeu par release (`releases[].tags` en v1, `.compatibleGameVersions` en v2) sont la source à utiliser pour le détecteur de mise à jour, pas le `modinfo.json` embarqué dans le zip.** Ce sont des cases cochées à la main par l'auteur sur le site (`edit-release.php`), pré-suggérées depuis le modinfo.json mais jamais forcées : possibilité réelle (pas juste théorique) qu'un auteur ait oublié de cocher une version de jeu récente. Toujours des versions exactes en tableau, jamais de plage ni de wildcard à parser.

5. **`mainfile` (v1) est déjà l'URL CDN finale ; `fileUrl` (v2) est un chemin relatif `/download/{id}/{nom}` qui fait un redirect 302 vers cette même URL CDN.** Si le client utilise des endpoints v2, prévoir de préfixer par `https://mods.vintagestory.at` et de suivre la redirection (`HttpClient` le fait tout seul par défaut, mais vérifier que `AllowAutoRedirect` n'est pas désactivé par la config de résilience).

6. **Aucune taille de fichier ni checksum exposés par l'API.** Un `HEAD` sur l'URL CDN donne `Content-Length` (et `Accept-Ranges: bytes` est présent, donc les téléchargements repris par `Range` sont possibles côté BunnyCDN) : à faire avant tout téléchargement si Prospect veut afficher une taille ou vérifier l'espace disque disponible.

7. **Utiliser `/api/updates?mods=id1@v1,id2@v2` pour le check de version en masse** plutôt que d'appeler `/api/mod/{id}` une fois par mod installé : un seul appel, réponse compacte, ne contient que les mods réellement en retard. Attention : l'absence d'un mod dans la réponse ne distingue pas « à jour » de « `modidstr` non reconnu » ; comparer aux clés envoyées. `/api/v2/mods/install-information` est une alternative plus riche (raisonne par version de jeu cible, donne une release exacte + une recommandation), à ré-explorer avec `resolve-deps` sur un mod à dépendances non triviales avant de s'appuyer dessus.

8. **Ne jamais appeler `/api/authors` sans `?name=`** : sans filtre, l'endpoint renvoie la totalité des ~111 000 comptes du site (~4 Mo), vérifié en direct.

9. **`dependencies` du modinfo.json est un objet `{modid: version}` où `version` est une borne *minimale*** (`""`/`"*"` = aucune contrainte), pas une contrainte exacte ni un wildcard `X.Y.*` (syntaxe non documentée, non observée, et de toute façon incompatible avec le parseur de version du ModDB lui-même). Implémenter une comparaison `>=` sémantique (`rc` > `pre` > `dev` pour les pré-releases), pas un `Equals` ni un pattern matching sur `*`.

10. **`side` de l'API ModDB (`client`/`server`/`both`) et `side` du `modinfo.json` (`Client`/`Server`/`Universal`) sont deux données différentes, pas juste deux casses différentes du même champ** : la première est choisie à la main sur la fiche web et peut diverger du fichier réellement publié. Pour une décision fiable (afficher/filtrer par côté client-serveur), lire le `modinfo.json` téléchargé, pas le champ `side` de l'API.

11. **Champs nullables à couvrir dans les DTOs** : `logo`/`urlalias` `null` sur 30 à 40% des mods, `modidstrs` peut être vide (outils externes, ou release sans identifier) ou contenir plusieurs entrées (une fiche = plusieurs modid), `changelog` par release peut être `null`, les URLs optionnelles (`homepageurl` etc.) sont `""` plutôt que `null` en pratique côté API bien que nullable en base, `tags` peut être `[""]` sur `/api/mods` mais `[]` sur `/api/mod/{id}` pour le même cas « aucun tag ». Un désérialiseur strict (`System.Text.Json` avec `record` non nullable partout) plantera sur plusieurs de ces cas si les propriétés ne sont pas explicitement `string?`/`List<string>` avec valeur par défaut.

12. **`/api/mod/{id}` accepte indifféremment le `modid` numérique interne ou le `modidstr` du modinfo.json** (vérifié en direct, `/api/mod/1783` et `/api/mod/configlib` renvoient le même mod), mais pas le `urlalias` de la fiche web, qui est une troisième donnée distincte, non résolue par cet endpoint. Pour retrouver un mod à partir d'une dépendance déclarée dans un modinfo.json local, appeler directement `/api/mod/{modidstr}`.
