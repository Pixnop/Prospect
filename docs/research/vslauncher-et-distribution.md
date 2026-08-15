# VS Launcher et distribution de Vintage Story : notes pour Prospect

## Méthode

Dépôt étudié : [`XurxoMF/vs-launcher`](https://github.com/XurxoMF/vs-launcher), trouvé via l'API de recherche GitHub (`vs-launcher vintage story`). Vérification faite via l'API REST : `archived: true`, langage TypeScript (Electron), description « Unofficial launcher and version manager for Vintage Story », dernier push le 2026-06-27T08:31:43Z, 63 étoiles. Le dépôt correspond bien au launcher communautaire décrit dans la mission.

Clone shallow (`--depth 1`) dans `/tmp/claude-1000/-home-lfievet-dev/c50ac529-7309-4418-9a4c-cbf471fc0951/scratchpad/vs-launcher`. Tous les chemins `src/...` cités plus bas sont relatifs à ce dépôt.

Vérifications live faites le 2026-08-10 en anonyme (User-Agent `Prospect-research`, six requêtes espacées : deux `GET` sur les manifestes JSON, trois `HEAD` sur des URLs de build, un `GET` sur `lateststable.txt`). Aucune tentative de connexion, aucun identifiant posté, aucun binaire de jeu réellement téléchargé.

Note en passant : la documentation de VS Launcher elle-même indique que le projet a un successeur, **Rustory** (`XurxoMF/rustory`, même auteur, dépôt actif et non archivé). Ça n'a pas été creusé ici (hors périmètre de cette recherche) mais ça vaut le coup d'œil si on veut comparer deux générations d'implémentation communautaire.

---

## a) Authentification au compte vintagestory.at

Le login se fait en deux temps contre un seul endpoint, sans OAuth ni cookie de session HTTP : c'est un POST classique qui renvoie un blob JSON que le launcher recopie tel quel dans sa config.

`src/renderer/src/components/ui/SessionButton.tsx` (lignes 39-65) :

```ts
const preLogin = await window.api.netManager.postUrl("https://auth3.vintagestory.at/v2/gamelogin", { email, password })

if (preLogin["valid"] == 0) {
  const reason = preLogin["reason"]

  if (reason == "requiretotpcode") {
    const fullLogin = await window.api.netManager.postUrl("https://auth3.vintagestory.at/v2/gamelogin", { email, password, preLoginToken: preLogin["prelogintoken"], twofacode })

    if (fullLogin["valid"] == 0 && fullLogin["reason"] == "wrongtotpcode") return addNotification(t("features.config.wrongtwofa"), "error")

    await saveLogin(fullLogin)
  } else if (reason == "invalidemailorpassword") {
    addNotification(t("features.config.invalidEmailPass"), "error")
  }
} else {
  await saveLogin(preLogin)
}
```

La requête réelle part côté process principal Electron, pas côté renderer (contournement CORS/CSP classique). `src/ipc/handlers/netHandlers.ts` (lignes 36-54) :

```ts
ipcMain.handle(IPC_CHANNELS.NET_MANAGER.VS_LOGIN, async (_event, url, body: { email: string; password: string; twofacode?: string; preLoginToken?: string }): Promise<string> => {
  const reqData = new URLSearchParams()
  reqData.append("email", body.email)
  reqData.append("password", body.password)
  reqData.append("totpcode", body.twofacode ?? "")
  reqData.append("prelogintoken", body.preLoginToken ?? "")

  const request = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: reqData
  })

  return await request.json()
})
```

Donc concrètement :
- **Endpoint** : `POST https://auth3.vintagestory.at/v2/gamelogin`
- **Payload** : `application/x-www-form-urlencoded`, champs `email`, `password`, `totpcode` (vide si pas de 2FA), `prelogintoken` (vide au premier appel)
- **Premier appel** (sans 2FA) : si le compte n'a pas de 2FA activée, la réponse contient directement les infos de session (`valid: 1`). Si 2FA activée, réponse `valid: 0`, `reason: "requiretotpcode"`, plus un `prelogintoken` à réinjecter
- **Deuxième appel** (avec 2FA) : mêmes champs + `preLoginToken` (le token du premier appel) + `totpcode` (le code à 6 chiffres). Échec possible : `reason: "wrongtotpcode"`. Autre échec possible dès le premier appel : `reason: "invalidemailorpassword"`

**Forme de la réponse en cas de succès**, reconstruite dans `saveLogin` (`SessionButton.tsx` lignes 73-90) :

```ts
const newAccount: AccountType = {
  email: email,
  playerName: data["playername"],
  playerUid: data["uid"],
  playerEntitlements: data["entitlements"],
  sessionKey: data["sessionkey"],
  sessionSignature: data["sessionsignature"],
  mptoken: data["mptoken"],
  hostGameServer: data["hasgameserver"]
}
```

Donc le JSON serveur contient au minimum : `valid`, `playername`, `uid`, `entitlements`, `sessionkey`, `sessionsignature`, `mptoken`, `hasgameserver`. Pas de cookie HTTP, pas de JWT classique : c'est une paire `sessionkey`/`sessionsignature` maison plus un `mptoken` (probablement le ticket utilisé pour l'auth multijoueur).

**Durée de vie** : rien dans le code ne gère d'expiration, de refresh token ou de re-login automatique (recherche de `expire`/`refresh`/`ttl` dans tout `src/` : aucun résultat pertinent). La session est traitée comme permanente côté launcher ; c'est confirmé par `docs/important-info/roadmap.md`, qui liste comme fonctionnalité déjà livrée : *« Permanent session : When you start a new Installation you'll have to manually login. If you play on multiple Installations this is a pain. I'll try to copy the session from one Installation to the others so you don't have to do this. »* Un seul compte est stocké globalement dans la config (`config.account`), partagé par toutes les Installations, jusqu'à déconnexion manuelle.

**Stockage** : dans le fichier de config JSON en clair de l'application, sans chiffrement (aucune trace de `safeStorage`, `keytar` ou API de coffre-fort dans tout le repo). `src/config/configManager.ts` sérialise tout `ConfigType` (qui inclut `account: AccountType | null`) via `fse.writeJSON(configPath, cleanedConfig)`, `configPath = join(app.getPath("userData"), "config.json")`. Sur Linux ça correspond à `~/.config/VSLauncher/config.json` (confirmé par `docs/get-started/installation/linux.md`, section migration Flatpak). Le secret complet (email, `sessionKey`, `sessionSignature`, `mptoken`, `playerUid`, entitlements) est donc lisible par n'importe quel process ayant accès au dossier de config de l'utilisateur.

Le compte n'est *pas* utilisé pour parler au réseau (pas de header `Authorization` envoyé nulle part) : il sert uniquement à préremplir un fichier de settings du jeu au moment du lancement, voir point d).

---

## b) Listing des versions du jeu

`src/renderer/src/features/versions/pages/AddVersion.tsx` (lignes 31-53), avec le commentaire des auteurs eux-mêmes qualifiant l'API de publique :

```ts
// Official public API: https://api.vintagestory.at/{stable,unstable}.json
// Shape: { [version]: { [platform]: { urls: { cdn, local }, ... } } }
type RawPlatform = { urls: { cdn: string; local: string } }
type RawVersions = Record<string, Record<string, RawPlatform>>
const VS_API = "https://api.vintagestory.at"

function deriveType(version: string): DownloadableGameVersionTypeType["type"] {
  if (version.includes("-rc")) return "rc"
  if (version.includes("-pre")) return "pre"
  return "stable"
}

function parseGameVersions(stable: RawVersions, unstable: RawVersions): DownloadableGameVersionTypeType[] {
  return Object.entries({ ...unstable, ...stable })
    .map(([version, p]) => ({
      version,
      type: deriveType(version),
      windows: p.windows?.urls.cdn ?? "",
      linux: p.linux?.urls.cdn ?? "",
      mac: (p["mac-arm64"] ?? p["mac-x64"])?.urls.cdn ?? ""
    }))
    .sort((a, b) => compareVersions(b.version, a.version))
}
```

Le launcher fait deux `GET` simples (via `axios`, sans en-tête d'auth) sur `https://api.vintagestory.at/stable.json` et `https://api.vintagestory.at/unstable.json`, fusionne les deux dictionnaires, et déduit le type (stable/rc/pre) à partir du nom de version plutôt que d'un champ dédié.

**Structure réelle du JSON** (vérifiée en live, pas seulement déduite du type TypeScript ci-dessus, qui d'ailleurs ignore plusieurs champs présents) :

```json
{
  "1.22.6": {
    "windows":      { "filename": "vs_install_win-x64_1.22.6.exe",       "filesize": "570.4 MB", "md5": "0ca071fa...", "urls": { "cdn": "https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.22.6.exe", "local": "https://account.vintagestory.at/files/stable/vs_install_win-x64_1.22.6.exe" }, "latest": 1 },
    "windowsupdate":{ "filename": "vs_update_win-x64_1.22.6.exe",        "filesize": "107.3 MB", "md5": "...", "urls": {...}, "latest": 1 },
    "linux":        { "filename": "vs_client_linux-x64_1.22.6.tar.gz",   "filesize": "590.5 MB", "md5": "c00c436c...", "urls": {...}, "latest": 1 },
    "linuxserver":  { "filename": "vs_server_linux-x64_1.22.6.tar.gz",   "filesize": "51.4 MB",  "md5": "...", "urls": {...}, "latest": 1 },
    "windowsserver":{ "filename": "vs_server_win-x64_1.22.6.zip",        "filesize": "61.4 MB",  "md5": "...", "urls": {...}, "latest": 1 },
    "mac-x64":      { "filename": "vs_client_osx-x64_1.22.6.tar.gz",     "filesize": "613.8 MB", "md5": "...", "urls": {...}, "latest": 1 },
    "mac-arm64":    { "filename": "vs_client_osx-arm64_1.22.6.tar.gz",   "filesize": "608.1 MB", "md5": "...", "urls": {...}, "latest": 1 }
  }
}
```

Points clés :
- `stable.json` contenait 52 versions au moment du test (de `1.9.14` à `1.22.6`) ; `unstable.json` en contenait 41, nommées `X.Y.Z-rc.N` (release candidates numérotées, pas de suffixe `-pre` observé dans le lot actuel malgré ce que gère `deriveType`)
- Chaque plateforme a : `filename`, `filesize` (chaîne lisible humaine du style « 590.5 MB », pas un nombre d'octets), `md5` (un seul hash, pas de sha256/sha1), `urls.cdn` et `urls.local` (deux miroirs, voir point c), `latest` (flag 0/1)
- Il n'y a **pas** de build Windows portable en zip : uniquement un installeur Inno Setup (`windows`) et un installeur incrémental (`windowsupdate`). Pas de clé `mac` unique : deux clés séparées `mac-x64`/`mac-arm64`, VS Launcher prenant arbitrairement `mac-arm64` en priorité si les deux existent
- Complément : `GET https://api.vintagestory.at/lateststable.txt` renvoie juste la version en texte brut (`1.22.6` au moment du test), cohérent avec la clé la plus haute de `stable.json`. Le code du launcher ne l'utilise pas mais l'endpoint existe et est public

Le type TypeScript du launcher (`RawPlatform = { urls: { cdn, local } }`) ne modélise même pas les champs `md5`/`filesize`, ce qui recoupe l'absence totale de vérification d'intégrité constatée au point suivant.

---

## c) Téléchargement des builds

**Aucune authentification n'est nécessaire ni même possible** dans le flux de téléchargement : `src/ipc/workers/downloadWorker.ts` fait un `GET` `axios` brut, sans le moindre header custom :

```ts
axios({ url, method: "GET", responseType: "stream" })
  .then(({ data, headers }) => { /* ... écriture en stream sur disque, suivi de progression ... */ })
```

Les URLs utilisées sont directement celles du champ `urls.cdn` du JSON (`AddVersion.tsx` ligne 101 : `const url = os === "win32" ? version.windows : os === "darwin" ? version.mac : version.linux`), donc de la forme :

- `https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.22.6.tar.gz` (Linux)
- `https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.22.6.exe` (Windows, installeur)
- `https://cdn.vintagestory.at/gamefiles/stable/vs_client_osx-arm64_1.22.6.tar.gz` (macOS, arm64 en priorité)
- Même chemin sous `gamefiles/unstable/...` pour les canaux RC

Le second miroir jamais utilisé par VS Launcher, `urls.local` (`https://account.vintagestory.at/files/stable/...`), pointe vers le portail « Client Area » officiel (`https://account.vintagestory.at/`) mentionné dans `docs/get-started/usage/game-client/install-vintage-story.md` comme méthode d'installation manuelle alternative.

**Vérification live (anonyme, HEAD uniquement)** : les deux miroirs répondent `200` sans redirection ni challenge d'auth.

```
HEAD https://cdn.vintagestory.at/gamefiles/stable/vs_client_linux-x64_1.22.6.tar.gz
→ HTTP/2 200, server: BunnyCDN-FR1-1218, content-length: 619177967, accept-ranges: bytes

HEAD https://account.vintagestory.at/files/stable/vs_client_linux-x64_1.22.6.tar.gz
→ HTTP/2 200, server: nginx, content-length: 619177967, content-disposition: attachment, accept-ranges: bytes

HEAD https://cdn.vintagestory.at/gamefiles/stable/vs_install_win-x64_1.22.6.exe
→ HTTP/2 200, server: BunnyCDN-FR1-1218, content-length: 598116392
```

Les tailles observées collent aux `filesize` annoncés dans le JSON (619 177 967 octets ≈ 590,5 MB ; 598 116 392 octets ≈ 570,4 MB), ce qui confirme que le manifeste est fiable. `cdn.vintagestory.at` est un edge BunnyCDN public (cache-control 30 jours) ; `account.vintagestory.at` est une origine nginx classique avec `content-disposition: attachment`. Les deux sont anonymes.

---

## d) Structure d'une installation par OS et commande de lancement

VS Launcher sépare strictement deux notions (définition officielle dans `docs/get-started/usage/concepts.md`) :
- **Version** : les fichiers moteur bruts (assets, code, exécutables), partagés, téléchargés une fois par version de jeu
- **Installation** : un dossier de données (`--dataPath`) contenant mondes, configs, mods, sauvegardes ; référence une Version par son numéro

**Post-téléchargement, par OS** (`AddVersion.tsx` lignes 117-150 et `src/ipc/handlers/pathsHandlers.ts`) :

- **Windows** : le fichier téléchargé est un installeur Inno Setup, pas une archive. `pathsHandlers.ts` lignes 139-165, avec le commentaire des mainteneurs :

```ts
// ponytail: Windows-only. The official VS download is an Inno Setup installer (no portable zip exists,
// and no released tool can crack current Inno Setup), so we run it silently into the version folder.
// /CURRENTUSER keeps it UAC-free; /DIR drops the game straight into outputPath.
const installer = spawn(exePath, ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/NOICONS", `/DIR=${outputPath}`], { windowsHide: true })
```

- **Linux/macOS** : le `.tar.gz` est extrait avec 7-Zip embarqué (`node-7z` + binaire `7za` fourni par le package `7zip-bin`, `src/ipc/workers/extractWorker.ts`), puis le launcher force `chmod 755` récursivement sur tout le dossier extrait (`TaskManagerContext.tsx` ligne 183 : `window.api.pathsManager.changePerms([outputPath], 0o755)`), sans quoi le binaire natif Linux n'a pas le bit exécutable après extraction

**Fichiers clés détectés dans le dossier Version** (`gameHandlers.ts` lignes 22-71, logique de détection identique dans `LOOK_FOR_A_GAME_VERSION` lignes 147-204) :
- Linux : `Vintagestory` (binaire natif sans extension) en priorité ; à défaut `Vintagestory.exe` (ancien build .NET Framework/Mono)
- Windows : `Vintagestory.exe` uniquement
- macOS : le launcher télécharge et extrait bien les builds `mac-x64`/`mac-arm64`, mais `EXECUTE_GAME` retourne `false` immédiatement avec le message *« MacOS platform detected. Not yet supported »* (ligne 66). Autrement dit : téléchargement possible, lancement non implémenté, à la date de l'archivage

**Commande de lancement exacte** (`gameHandlers.ts` lignes 22-64) :

```ts
// Linux, binaire natif trouvé
command = join(version.path, "Vintagestory")
params = [`--dataPath=${installation.path}`, installation.startParams]
if (installation.mesaGlThread) env = { ...env, MESA_GLTHREAD: "true" }

// Linux, fallback ancien build .exe
command = "mono"
params = [join(version.path, "Vintagestory.exe"), `--dataPath=${installation.path}`, installation.startParams]

// Windows
command = join(version.path, "Vintagestory.exe")
params = [`--dataPath=${installation.path}`, installation.startParams]
```

Puis `spawn(command, params, { env })`. Détail à noter pour l'implémentation C# : `installation.startParams` (les paramètres additionnels saisis par l'utilisateur, cf. lien vers `wiki.vintagestory.at/Client_startup_parameters` dans `AddInstallation.tsx`) est passé comme **un seul élément** du tableau `params`, pas retokenisé en plusieurs `argv`. Comme `spawn()` avec un tableau ne passe pas par un shell, c'est potentiellement fragile si l'utilisateur saisit plusieurs flags séparés par des espaces.

**Injection de la session de compte** : elle ne passe *pas* par la ligne de commande, mais par un fichier écrit juste avant de lancer le process (`gameHandlers.ts` lignes 73-114) :

```ts
const clientsettingsPath = join(installation.path, "clientsettings.json")
// ... si le fichier n'existe pas, le crée avec stringSettings ; sinon met à jour in-place :
clientsettings["stringSettings"]["mptoken"] = account.mptoken
clientsettings["stringSettings"]["sessionkey"] = account.sessionKey
clientsettings["stringSettings"]["sessionsignature"] = account.sessionSignature
clientsettings["stringSettings"]["useremail"] = account.email
clientsettings["stringSettings"]["entitlements"] = account.playerEntitlements
clientsettings["stringSettings"]["playeruid"] = account.playerUid
clientsettings["stringSettings"]["playername"] = account.playerName
clientsettings["stringSettings"]["hostgameserver"] = account.hostGameServer
```

C'est donc le jeu lui-même (pas le launcher) qui lit `clientsettings.json` au démarrage pour se considérer connecté, typiquement pour le multijoueur/les entitlements. Si `account` est `null` (pas connecté), ce bloc est simplement sauté et le jeu démarre en mode non authentifié.

**Chemins par défaut documentés** (`docs/get-started/usage/game-client/vintage-story-is-already-installed.md`, valables hors VS Launcher aussi, ce sont les emplacements par défaut du jeu natif) :
- Version : `C:/Users/<user>/AppData/Roaming/Vintagestory` (Windows) / `~/.local/share/Vintagestory/` (Linux)
- Installation/data : `C:/Users/<user>/AppData/Roaming/VintagestoryData` (Windows) / `~/.config/VintagestoryData/` (Linux)

---

## e) Runtime .NET

C'est le point le plus surprenant : **VS Launcher ne fait rigoureusement rien vis-à-vis du runtime .NET**. Recherche exhaustive de `dotnet`/`.NET`/`runtime` dans `src/` : les seules occurrences sont les deux branches `command = "mono"` déjà citées au point d) (fallback pour les vieux builds `.exe` sur Linux). Aucune détection de version installée, aucun téléchargement, aucun bundling, aucun message d'erreur dédié si le runtime manque (l'échec de `spawn()` remonterait juste comme une erreur générique de lancement de process).

La responsabilité est intégralement reportée sur l'utilisateur, via la documentation, et les mainteneurs le disent explicitement dans `docs/get-started/installation/linux.md` :

> *« VS Launcher does not need any dependencies to work, but Vintage Story does. I wanted to make this process automatic upon game launch but Linux has a lot of distros and I can't personalize it to work on all of them so you'll have to do it manually. »*

Les instructions manuelles demandent d'installer **plusieurs majors .NET en parallèle**, pas une seule version fixe (Debian/Ubuntu, `docs/get-started/installation/linux.md`) :

```sh
sudo ./dotnet-install.sh --channel 7.0 --install-dir /usr/lib/dotnet
sudo ./dotnet-install.sh --channel 8.0 --install-dir /usr/lib/dotnet
sudo ./dotnet-install.sh --channel 10.0 --install-dir /usr/lib/dotnet
```

Plus `mono-complete` et `libopenal-dev`, plus (souvent oublié) relever la limite mémoire virtuelle : `sudo sysctl -w vm.max_map_count=262144`. Sur Arch : `sudo pacman -S dotnet-runtime-7.0 dotnet-runtime-8.0 dotnet-runtime glibc openal opengl-driver mono`. Sur Windows, la doc renvoie carrément vers trois installeurs SDK Microsoft séparés (.NET 7, 8 et 10) à télécharger et exécuter manuellement.

Autrement dit : selon la version du jeu installée, il faut potentiellement .NET 7, 8 *ou* 10 (le launcher ne fait pas non plus le lien entre « version de jeu X » et « runtime requis Y », c'est laissé à l'utilisateur de deviner/tout installer). Même les builds AppImage/Flatpak du launcher lui-même ne bundlent pas .NET pour le jeu (la doc note explicitement que le Flatpak « n'est pas packagé avec .NET » et que c'est un problème connu non résolu au moment de l'archivage).

---

## f) Gestion des mods et du dataPath

**Modèle Version/Installation** : confirmé par les types (`src/global.d.ts`). `GameVersionType = { version, path }` (le moteur partagé) est distinct de `InstallationType` (le profil) :

```ts
type InstallationType = {
  id: string; name: string; icon: string; path: string; version: string
  startParams: string; backupsLimit: number; backupsAuto: boolean; compressionLevel: number
  backups: BackupType[]; lastTimePlayed: number; totalTimePlayed: number
  mesaGlThread: boolean; envVars: string
  _modsCount?: number; _playing?: boolean; _backuping?: boolean; _restoringBackup?: boolean; _updatingMods?: boolean
}
```

**Dossiers par défaut** (`src/config/configManager.ts`, racine = `app.getPath("appData")`, soit `~/.config` sur Linux ou `%AppData%\Roaming` sur Windows) :
- `VSLInstallations/` — les profils/instances
- `VSLGameVersions/` — les moteurs partagés
- `VSLBackups/` — les sauvegardes compressées
- Le fichier `config.json` lui-même vit à part, dans `app.getPath("userData")` (`~/.config/VSLauncher/config.json` sur Linux)

**Mods** : stockés à plat dans `<installation.path>/Mods/*.zip`, un fichier zip par mod, nommé `<modidstr>-<modversion>.zip`. Confirmé par `src/renderer/src/features/mods/hooks/useInstallMod.ts` :

```ts
const installPath = await window.api.pathsManager.formatPath([path, "Mods"])
if (oldMod) await window.api.pathsManager.deletePath(oldMod.path)
startDownload(/* ... */, release.mainfile, installPath, `${release.modidstr}-${release.modversion}`, /* ... */)
```

Le listing des mods installés (`src/ipc/handlers/modsHandlers.ts`) ouvre chaque `.zip` du dossier `Mods/` avec `yauzl`, cherche une entrée `modinfo.json` (parsée en JSON5, tolérant, avec repli sur plusieurs variantes de casse : `modid`/`Modid`/`ModID`/`modID`/`modId`) et une entrée `modicon.png` pour l'illustration.

**Activation/désactivation de mods : aucun mécanisme n'existe.** Recherche explicite de `toggle`/`enable`/`disable` dans les pages de gestion de mods (`ManageMods.tsx`, `ListMods.tsx`) : rien. Le launcher ne propose qu'**installer**, **mettre à jour** (= supprimer l'ancien zip + télécharger le nouveau) et **supprimer**. Pas de renommage `.disabled`, pas de sous-dossier séparé pour les mods désactivés. C'est un vrai manque fonctionnel par rapport à des launchers comme Prism Launcher.

**Import/export de modpacks** existe en revanche : un simple JSON `{ name, gameVersion, mods: [{modid, version}] }` exporté/importé via une boîte de dialogue fichier (`modsHandlers.ts` lignes 32-89), sans mécanisme de résolution de dépendances embarqué au-delà de ça.

**Sauvegardes** : fonctionnalité séparée, compresse tout le dossier Installation vers `backupsFolder` via le même binaire 7-Zip embarqué, à un niveau de compression configurable par Installation (`compressionLevel`), avec limite (`backupsLimit`) et déclenchement automatique optionnel avant chaque lancement (`backupsAuto`).

---

## Verdict : l'authentification est-elle réellement nécessaire pour télécharger le jeu ?

**Non.** À aucun moment du flux d'installation ou de mise à jour (listing des versions, téléchargement, extraction/installation) le code de VS Launcher n'envoie de secret d'authentification, et les vérifications live le confirment de bout en bout :

| Étape | URL | Auth envoyée par le code ? | Résultat live anonyme |
|---|---|---|---|
| Listing versions | `api.vintagestory.at/{stable,unstable}.json` | Non | `200`, JSON complet (52 + 41 versions) |
| Version courante | `api.vintagestory.at/lateststable.txt` | Non | `200`, `1.22.6` |
| Téléchargement CDN | `cdn.vintagestory.at/gamefiles/stable/...` | Non | `200` anonyme (BunnyCDN, tailles exactes) |
| Téléchargement miroir | `account.vintagestory.at/files/stable/...` | Non | `200` anonyme (nginx, `content-disposition: attachment`) |

Le seul endroit où l'authentification intervient dans tout le launcher, c'est *après* l'installation : au moment de lancer le jeu, si un compte est connecté, ses tokens sont écrits dans `clientsettings.json` du dossier Installation pour que le *jeu* (pas le launcher) s'authentifie lui-même en multijoueur/entitlements. Le téléchargement du client, lui, est un fichier statique public servi par un CDN commercial (BunnyCDN) plus un miroir nginx, sans aucun contrôle d'accès observé. Les commentaires des mainteneurs eux-mêmes qualifient `api.vintagestory.at` d'« Official public API ».

---

## Implications pour Prospect

1. **Ne pas gater le téléchargement derrière l'auth.** Le flux « lister les versions → télécharger → installer » doit fonctionner à froid, sans compte connecté ; ne demander l'auth que juste avant de lancer le process, pour écrire les infos de session dont le jeu a besoin en multijoueur.

2. **Reproduire le contrat exact de `POST https://auth3.vintagestory.at/v2/gamelogin`** : `application/x-www-form-urlencoded`, champs `email`/`password`/`totpcode`/`prelogintoken`, gérer explicitement les trois `reason` possibles (`requiretotpcode`, `invalidemailorpassword`, `wrongtotpcode`) comme une petite machine à états à deux passes.

3. **Stocker le secret de session mieux que VS Launcher.** Eux le mettent en JSON en clair dans le dossier de config utilisateur. En C#, préférer DPAPI (Windows), Secret Service/libsecret (Linux) ou Keychain (macOS) via une abstraction, ou a minima un fichier séparé avec permissions restreintes — ne pas mélanger le secret de session avec le reste de la config applicative.

4. **Vérifier le MD5 fourni par l'API après téléchargement.** VS Launcher expose le champ `md5` dans son propre manifeste JSON mais ne le vérifie jamais (son type TypeScript ne modélise même pas le champ). C'est une amélioration simple et gratuite à faire en C# (`System.Security.Cryptography.MD5`) pour détecter un téléchargement corrompu avant extraction.

5. **Séparer clairement « Version du jeu » (moteur partagé, immutable) et « Instance » (profil/dataPath/mods/paramètres)** dès le modèle de données, comme le fait VS Launcher (et Prism Launcher) : une Version peut être référencée par plusieurs Instances, elle n'est jamais couplée à une seule.

6. **Prévoir trois stratégies post-téléchargement différentes selon l'OS** : Windows = lancer l'installeur Inno Setup en silencieux (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /DIR=<cible>`, pas de build portable disponible côté officiel) ; Linux/macOS = extraire le `.tar.gz` puis forcer explicitement les permissions d'exécution (`chmod 755` récursif, ou au minimum sur le binaire principal) sinon rien ne se lance.

   > **Dépassé côté Windows.** L'affirmation « no released tool can crack current Inno Setup » citée plus haut est celle des mainteneurs de VS Launcher, et elle valait pour les outils publiés. Prospect lit désormais le format lui-même et n'exécute plus l'installeur : voir docs/architecture.md, « La boîte "ancienne version détectée" : pourquoi elle disparaît ». L'exécution silencieuse décrite ici reste le repli.

7. **Détecter le runtime .NET installé plutôt que de tout reporter sur l'utilisateur.** VS Launcher ne fait aucune détection ni installation, et ses propres mainteneurs documentent ça comme un point de friction connu et jamais résolu. Prospect, étant lui-même en C#/.NET, est bien placé pour faire mieux : détecter les runtimes présents (`dotnet --list-runtimes` ou inspection de `dotnet/shared/Microsoft.NETCore.App`) et proposer une installation guidée ou automatique du runtime manquant.

8. **Ne pas supposer un seul major .NET.** La doc de VS Launcher demande d'installer .NET 7, 8 *et* 10 en parallèle selon la version du jeu utilisée. La détection de runtime doit être associée à la version de jeu installée, pas être une vérification globale unique au démarrage de l'app.

9. **Construire les arguments de lancement comme un vrai tableau, pas une chaîne collée.** VS Launcher passe les paramètres additionnels de l'utilisateur comme un seul élément d'`argv`, ce qui est fragile dès qu'on veut plusieurs flags. En C#, tokeniser proprement la chaîne saisie avant de peupler `ProcessStartInfo.ArgumentList`.

10. **Implémenter un vrai mécanisme d'activation/désactivation des mods**, absent chez VS Launcher (qui n'a qu'installer/mettre à jour/supprimer). Une approche simple : suffixe `.disabled` sur le zip, ou sous-dossier `Mods/disabled/` déplacé/remis en place à l'activation. C'est un manque direct et documenté du concurrent.

11. **Prévoir un fallback de miroir de téléchargement.** L'API officielle expose déjà deux URLs par build (`urls.cdn` sur BunnyCDN et `urls.local` sur `account.vintagestory.at`), mais VS Launcher n'utilise que la première et n'a aucun repli automatique si elle échoue. Basculer sur le second miroir en cas d'échec/timeout serait une amélioration simple à moindre coût.

12. **Traiter macOS comme un vrai OS cible dès le modèle, même si le lancement natif est reporté.** VS Launcher télécharge et extrait les builds `mac-x64`/`mac-arm64` depuis longtemps mais n'a jamais implémenté le lancement (retour `false` immédiat, « not yet supported »), ce qui laisse ses utilisateurs mac sans solution des années après. Autant anticiper la détection/l'extraction mac dès le début côté Prospect, quitte à retarder seulement le bouton « Jouer ».
