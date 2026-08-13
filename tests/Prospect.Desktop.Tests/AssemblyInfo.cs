using Xunit;

// Exécution strictement séquentielle de cet assembly.
//
// Deux états sont GLOBAUX au processus par conception, et cet assembly est le seul à les basculer :
// la variante de thème (Application.RequestedThemeVariant, que les scénarios de thème clair posent
// puis rétablissent) et la langue de l'interface (le dictionnaire fusionné dans
// Application.Resources et la table de Resources.UiText, voir sa remarque sur l'entorse assumée).
//
// Les tests [AvaloniaFact] sont déjà sérialisés entre eux par la session headless d'Avalonia, mais
// PAS avec les [Fact] ordinaires : un test de ViewModel qui affirme « Compte Vintage Story » peut
// s'exécuter pendant qu'un test de démarrage anglais tient la table anglaise. La suite est passée
// en local et sous Windows avant que la CI ne le montre sur macOS et Ubuntu, ce qui est exactement
// la façon dont ce genre de course se manifeste.
//
// Le parallélisme rapportait peu ici : les tests headless, majoritaires en temps, étaient déjà
// sérialisés. Le coût mesuré est de quelques secondes sur l'assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]