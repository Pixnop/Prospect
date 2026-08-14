using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Launching;
using Prospect.Desktop.Services;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.FirstRun;
using Prospect.Desktop.ViewModels.Home;
using Prospect.Desktop.ViewModels.Instance;
using Prospect.Desktop.ViewModels.Shell;
using Prospect.Desktop.ViewModels.Toasts;
using Prospect.Desktop.ViewModels.Versions;
using Prospect.Desktop.ViewModels.Wizard;
using Prospect.Desktop.Views.FirstRun;
using Prospect.Desktop.Views.Home;
using Prospect.Desktop.Views.Wizard;

using Shouldly;

namespace Prospect.Desktop.Tests.Journeys;

/// <summary>
/// PARCOURS 1 — premier contact. D'une installation vierge à une partie terminée proprement, sans
/// jamais sortir de l'application : l'écran de premier lancement s'affiche et propose une action,
/// le wizard crée la première instance en installant sa version au passage, le bouton Jouer devient
/// actif, le jeu démarre puis sort, et l'écran suit chacun de ces états.
/// </summary>
/// <remarks>
/// Ce qu'aucun test d'écran ne pouvait attraper et que celui-ci garde : la CONTINUITÉ. Chaque test
/// existant part d'un état déjà à moitié construit (une instance semée, une version posée à la main
/// sur le disque factice) ; ici rien n'est semé, tout est obtenu par les gestes de l'utilisateur,
/// et une seule rupture dans la chaîne fait tomber le parcours. Le lancement lui-même passe par le
/// conteneur réel (voir <see cref="TestServiceProviderFactory.CreateForJourney"/>), donc par le
/// vrai <c>GameLauncher</c>, le vrai <c>RunningInstanceTracker</c> et les vraies stratégies.
/// </remarks>
public sealed class FirstContactJourneyTests
{
    [AvaloniaFact]
    public async Task Journey_FirstRunToFinishedSession_NeverLeavesTheUserWithoutANextStep()
    {
        using var provider = TestServiceProviderFactory.CreateForJourney(out var fileSystem, out _, out var seams);
        var window = provider.GetRequiredService<MainWindow>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var home = provider.GetRequiredService<HomeViewModel>();
        var toasts = provider.GetRequiredService<IToastService>();
        window.Show();

        // ── Étape 1 : l'écran de premier lancement s'affiche, et il ORIENTE ───────────────
        shell.ShowFirstRunIfNeeded();
        window.Pump();

        var firstRun = shell.Overlay.Active.ShouldBeOfType<FirstRunScreenViewModel>();
        window.GetVisualDescendants().OfType<FirstRunScreenView>().ShouldNotBeEmpty();
        firstRun.Steps.ShouldContain(
            step => step.HasAction,
            "un écran d'accueil qui ne propose aucune action laisse l'utilisateur devant une liste de constats");

        var versionStep = firstRun.Steps.Where(step => step.ActionCommand == firstRun.GoToVersionsCommand).ShouldHaveSingleItem();
        versionStep.ActionLabel.ShouldNotBeNullOrWhiteSpace("un bouton sans libellé ne dit pas ce qu'il fait");

        // ── Étape 2 : l'action proposée mène vraiment quelque part ────────────────────────
        await firstRun.GoToVersionsCommand.ExecuteAsync(null);
        window.Pump();

        shell.Overlay.Active.ShouldBeNull();
        var versions = shell.CurrentPage.ShouldBeOfType<VersionsViewModel>();
        await versions.RefreshCommand.ExecuteAsync(null);
        window.Pump();
        versions.Available.ShouldNotBeEmpty("l'écran atteint depuis la checklist doit être rempli, pas vide");

        // ── Étape 3 : le wizard complet, depuis l'Accueil ─────────────────────────────────
        shell.ShowHome();
        home.NewInstanceCommand.Execute(null);
        window.Pump();

        var wizard = shell.Overlay.Active.ShouldBeOfType<WizardViewModel>();
        window.GetVisualDescendants().OfType<WizardView>().ShouldNotBeEmpty();
        await wizard.LoadVersionsCommand.ExecuteAsync(null);
        window.Pump();

        // Étape « nom » : le bouton suivant reste hors service tant que le nom est vide, et le
        // wizard le DIT plutôt que de laisser un bouton mort.
        wizard.IsNameStep.ShouldBeTrue();
        wizard.NextCommand.CanExecute(null).ShouldBeFalse();
        wizard.Name = "Première partie";
        wizard.NextCommand.CanExecute(null).ShouldBeTrue();
        wizard.NextCommand.Execute(null);
        window.Pump();

        // Étape « version » : celle du catalogue, pas encore installée.
        wizard.IsVersionStep.ShouldBeTrue();
        var choice = wizard.VersionChoices.Where(entry => entry.VersionText == "1.21.3").ShouldHaveSingleItem();
        choice.IsInstalled.ShouldBeFalse();
        choice.SelectCommand.Execute(null);
        wizard.NextCommand.Execute(null);
        window.Pump();

        wizard.IsIconStep.ShouldBeTrue();
        wizard.NextCommand.Execute(null);
        window.Pump();

        // Étape « résumé » : elle annonce ce qui va se passer, téléchargement compris.
        wizard.IsSummaryStep.ShouldBeTrue();
        wizard.SummaryNote.ShouldNotBeNullOrWhiteSpace("le résumé doit dire qu'une version va être téléchargée");

        await wizard.CreateCommand.ExecuteAsync(null);
        window.Pump();

        wizard.CreateError.ShouldBeNull();
        shell.Overlay.Active.ShouldBeNull();
        toasts.ShouldHaveToast(ToastTone.Success);

        // La version demandée est réellement installée, sentinelle comprise.
        var installedVersions = provider.GetRequiredService<Prospect.Core.GameVersions.IInstalledGameVersionRepository>();
        (await installedVersions.ScanAsync()).Installed
            .ShouldContain(entry => entry.Version.ToString() == "1.21.3", "le wizard installe la version choisie avant de créer l'instance");

        // ── Étape 4 : la carte apparaît, et elle s'ouvre ──────────────────────────────────
        home.Instances.Count.ShouldBe(1);
        window.GetVisualDescendants().OfType<InstanceCardView>().ShouldNotBeEmpty();
        var card = home.Instances[0];
        card.Name.ShouldBe("Première partie");

        shell.ShowInstanceDetail(card.Slug);
        var detail = shell.CurrentPage.ShouldBeOfType<InstanceDetailViewModel>();
        await detail.InitializeCommand.ExecuteAsync(null);
        window.Pump();

        // ── Étape 5 : Jouer est actif, et le clic démarre vraiment le jeu ─────────────────
        detail.PlayCommand.CanExecute(null).ShouldBeTrue("la version est installée : rien ne doit retenir le bouton Jouer");
        window.HasEnabledButton(JourneyHarness.ResourceText("Instance_Play")).ShouldBeTrue("l'action principale de la page doit être visible ET cliquable");

        var process = new FakeRunningProcess();
        seams.ProcessRunner.NextProcessFactory = _ => process;

        await detail.PlayCommand.ExecuteAsync(null);
        window.Pump();

        detail.ShowLaunchError.ShouldBeFalse(detail.LaunchErrorMessage ?? "aucune erreur attendue");
        detail.IsRunning.ShouldBeTrue();
        seams.ProcessRunner.StartRequests.ShouldHaveSingleItem();
        seams.ProcessRunner.StartRequests[0].Arguments.ShouldContain(argument => argument.StartsWith("--dataPath=", StringComparison.Ordinal));

        // L'écran dit « en cours », et l'action principale devient « Arrêter ».
        window.HasEnabledButton(JourneyHarness.ResourceText("Instance_Stop")).ShouldBeTrue("pendant une partie, la seule action possible sur l'instance doit être visible");
        detail.PlayCommand.CanExecute(null).ShouldBeFalse();

        // ── Étape 6 : sortie propre ──────────────────────────────────────────────────────
        var tracker = provider.GetRequiredService<RunningInstanceTracker>();
        process.CompleteWith(0);
        await window.WaitUntilAsync(() => !tracker.IsRunning(card.Slug), "le suivi du processus doit voir la sortie du jeu");
        await window.WaitUntilAsync(() => !detail.IsRunning, "et l'écran doit repasser à l'état arrêté");

        detail.PlayCommand.CanExecute(null).ShouldBeTrue("après la partie, on doit pouvoir relancer");
        window.HasEnabledButton(JourneyHarness.ResourceText("Instance_Play")).ShouldBeTrue();

        // Le journal du lancement existe : c'est ce que l'onglet Journal affichera.
        fileSystem.File.Exists(provider.GetRequiredService<GameLauncher>().GetLogFilePath(card.Slug)).ShouldBeTrue();

        window.Close();
    }
}