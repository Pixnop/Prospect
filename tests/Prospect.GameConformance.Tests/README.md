# Prospect.GameConformance.Tests

Cet étage de tests confronte les HYPOTHÈSES de Prospect au VRAI moteur Vintage Story, via
[Atlas](https://github.com/Pixnop/Atlas) (paquet `Pixnop.Atlas.XUnit`), un harnais qui boote un
vrai serveur Vintage Story headless à l'intérieur du process `dotnet test`. C'est le seul projet
du dépôt à qui ce rôle revient : partout ailleurs, Prospect simule le réseau et le système de
fichiers (`MockFileSystem`, `HttpMessageHandler` factices) précisément pour rester rapide et
déterministe. Ici c'est l'inverse assumé : simuler le moteur n'aurait aucun sens pour un projet
dont le rôle est de vérifier qu'on ne s'est pas raconté d'histoires sur son comportement réel.

Deux conséquences directement de ce rôle :

- **Système de fichiers réel.** Les tests créent de vraies instances, de vrais zips de mods, de
  vrais répertoires temporaires (nettoyés à la fin de chaque test, voir `Support/
  ConformanceTempDirectory.cs`) — jamais `MockFileSystem`. Un mock ne pourrait que rejouer ce
  qu'on suppose déjà vrai ; il ne peut pas prouver qu'on a raison.
- **Lent et opt-in par nature.** Démarrer un serveur Vintage Story prend plusieurs secondes par
  scénario. Ce coût est acceptable pour une poignée de tests qui tournent une fois par semaine,
  jamais pour la gate d'une pull request qui doit rester rapide.

## Opt-in : `PROSPECT_CONFORMANCE=1`

Aucun test de ce projet ne s'exécute par défaut. `ConformanceFactAttribute` (une variante maison
d'`Atlas.XUnit.AtlasScenarioAttribute`, scellée donc impossible d'en hériter directement — voir sa
documentation) marque chaque scénario `Skip` tant que la variable d'environnement
`PROSPECT_CONFORMANCE` ne vaut pas exactement `"1"`. `dotnet test` d'une solution qui contient ce
projet le voit donc « skipped » en une seconde, sans jamais tenter de localiser Vintage Story ni de
démarrer quoi que ce soit. Pour exécuter la suite pour de vrai :

```sh
VINTAGE_STORY=/chemin/vers/une/installation PROSPECT_CONFORMANCE=1 \
  dotnet test tests/Prospect.GameConformance.Tests -c Release
```

## Deuxième palier : `VINTAGE_STORY` à la compilation

Un détail moins visible, mais structurant : Atlas compile les scénarios contre les types du jeu
réel (`BlockPos`, `ICoreServerAPI`...), qui viennent d'un fichier `VintagestoryAPI.dll` d'une
installation locale, jamais d'un paquet NuGet (le jeu n'est pas redistribuable). Le
`.csproj` de ce projet ne référence donc ce fichier — et donc ne compile les scénarios réels — QUE
si la variable `VINTAGE_STORY` était définie AU MOMENT DU BUILD. Sans elle (la CI normale du
dépôt, `ci.yml`, ne la définit jamais), un unique test sentinelle toujours ignoré
(`EngineUnavailableTests`) compile à sa place, pour que `dotnet test` de toute la solution
continue de rapporter un résultat clair plutôt que de faire disparaître ce projet ou casser le
build des deux autres projets de test.

En pratique, ce deuxième opt-in n'a besoin d'être posé qu'une fois : `VINTAGE_STORY` (pointant
vers une installation réelle) ET `PROSPECT_CONFORMANCE=1` ensemble font tourner la suite pour de
vrai ; l'un des deux manquant, elle reste invisible ou ignorée sans jamais casser le reste de la
solution.

## Comment Atlas trouve le serveur

Atlas n'installe rien lui-même : il attend une installation Vintage Story déjà présente sur la
machine et lit son chemin dans la variable d'environnement `VINTAGE_STORY` (le dossier contenant
`VintagestoryAPI.dll`/`VintagestoryLib.dll`). C'est le workflow `.github/workflows/conformance.yml`
qui télécharge le serveur headless Linux officiel (`vs_server_linux-x64_<version>.tar.gz` depuis
`cdn.vintagestory.at`) et positionne cette variable avant de lancer les tests ; en local, c'est une
installation existante du jeu qui suffit.

## Les trois preuves

Chaque test documente, dans son commentaire XML, l'hypothèse précise qu'il prouve ou invalide :

- **`DisabledConventionTests`** — la plus importante : l'hypothèse laissée ouverte depuis la PR 7
  (voir `Prospect.Core.ModDb.IModStateConvention`), que renommer `<nom>.zip` en
  `<nom>.zip.disabled` suffit à faire ignorer un mod par le vrai `ModLoader`. Deux scénarios,
  jamais de suffixe codé en dur : le nom du fichier désactivé est calculé par
  `IModStateConvention` lui-même.
- **`DataPathLayoutTests`** — confronte les noms de sous-dossiers que le Core suppose sous le
  dataPath d'une instance (`Mods/`, `ModConfig/`, `Saves/`) à ceux que le moteur utilise
  réellement (`Vintagestory.API.Config.GamePaths`), puis ferme la boucle en vérifiant qu'une vraie
  instance `InstanceService` retrouve bien un monde déposé sous le nom qu'elle suppose.
- **`ModInfoParsingAgreementTests`** — confronte, sur des `modinfo.json` tordus mais légaux (casse
  des clés, commentaires, virgules terminales, side en minuscules, dépendances), ce que
  `Prospect.Core.ModDb.ModInfoParser` en tire à ce que le `ModLoader` réel en dit après
  chargement.

Une divergence, dans les trois cas, échoue avec un message qui nomme précisément le dossier ou le
champ fautif — jamais un simple « le test a échoué ».
