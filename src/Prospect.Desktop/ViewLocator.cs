using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Prospect.Desktop;

/// <summary>
/// Résout un ViewModel vers sa vue par convention de nom :
/// <c>Prospect.Desktop.ViewModels.Home.HomeViewModel</c> devient
/// <c>Prospect.Desktop.Views.Home.HomeView</c> (un unique remplacement textuel "ViewModel" →
/// "View" transforme aussi bien le segment de namespace "ViewModels" que le suffixe de classe).
/// Enregistré globalement dans App.axaml (<c>Application.DataTemplates</c>) : la page courante du
/// shell, le panneau modal (wizard, dialogues de carte) et les pages placeholder se résolvent tous
/// de la même façon, sans DataTemplate dédié à écrire pour chacun (docs/architecture.md : « les
/// vues se résolvent par un ViewLocator simple, convention VM → View »).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private const string ViewModelNamespacePrefix = "Prospect.Desktop.ViewModels";

    // Les deux textes de secours ci-dessous ne passent volontairement pas par Resources/UiText.cs,
    // seul endroit du Desktop qui échappe à la traduction : ce ne sont pas des messages produit mais
    // des diagnostics de câblage, qui nomment un type .NET et n'apparaissent que si une vue manque à
    // la compilation. Les traduire donnerait à UiText deux entrées qu'aucun joueur ne lira jamais.
    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "(aucun contenu)" };
        }

        var viewModelTypeName = param.GetType().FullName!;
        var viewTypeName = viewModelTypeName.Replace("ViewModel", "View", StringComparison.Ordinal);
        var viewType = Type.GetType(viewTypeName);

        if (viewType is not null && Activator.CreateInstance(viewType) is Control control)
        {
            return control;
        }

        return new TextBlock { Text = $"Vue introuvable : {viewTypeName}" };
    }

    public bool Match(object? data) => data?.GetType().Namespace?.StartsWith(ViewModelNamespacePrefix, StringComparison.Ordinal) == true;
}