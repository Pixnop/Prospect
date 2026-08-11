# 1. Cibler net10.0 plutôt que net8.0

Statut : accepté, 2026-08-11.

## Contexte

Le cahier des charges du launcher demande « .NET 8 ou plus récent », sans imposer de version
précise. Or .NET 8 sort de support fin novembre 2026, dans un peu plus de trois mois : démarrer
un projet neuf dessus revient à programmer une migration avant même la première version
publiable. .NET 10 est la version LTS courante, le SDK 10.0.110 est déjà installé sur la machine
de développement, et Avalonia 11.3 le supporte sans réserve.

## Décision

Prospect cible `net10.0` sur les quatre projets de la solution, épinglé dans `global.json` avec
`rollForward: latestFeature`.

## Conséquences

Le projet démarre avec plusieurs années de support devant lui au lieu de quelques mois, et
profite des dernières améliorations du runtime sans travail supplémentaire. La contrepartie est
une base d'utilisateurs potentiels légèrement plus étroite au lancement, le temps que .NET 10
se généralise sur les machines des joueurs : Vintage Story lui-même embarque son propre runtime
dans certaines versions, ce qui limite cet effet, et le launcher ne dépend de toute façon que du
runtime installé pour lui-même, pas pour le jeu qu'il lance.
