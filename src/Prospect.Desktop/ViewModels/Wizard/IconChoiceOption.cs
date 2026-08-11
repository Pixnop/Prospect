using CommunityToolkit.Mvvm.Input;

namespace Prospect.Desktop.ViewModels.Wizard;

/// <summary>
/// Une icône proposée à l'étape 3 du wizard (design/readme.md : aucune icône d'instance fournie
/// par le handoff, sous-ensemble curé des glyphes du design en attendant la personnalisation par
/// fichier). <see cref="WizardViewModel"/> reconstruit la liste entière à chaque sélection plutôt
/// que de rendre ce type observable, comme <see cref="WizardStepIndicatorViewModel"/>.
/// </summary>
public sealed class IconChoiceOption
{
    public IconChoiceOption(string key, string iconKey, string label, bool isSelected, Action<string> select)
    {
        ArgumentNullException.ThrowIfNull(select);

        Key = key;
        IconKey = iconKey;
        Label = label;
        IsSelected = isSelected;
        SelectCommand = new RelayCommand(() => select(key));
    }

    /// <summary>Forme courte persistée dans <c>instance.json</c> (préfixée <c>builtin:</c> par <see cref="WizardViewModel"/>).</summary>
    public string Key { get; }

    /// <summary>Clé résolue par <see cref="Prospect.Desktop.Converters.IconKeyToGeometryConverter"/>.</summary>
    public string IconKey { get; }

    public string Label { get; }

    public bool IsSelected { get; }

    public IRelayCommand SelectCommand { get; }
}