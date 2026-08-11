namespace Prospect.Desktop.ViewModels.Wizard;

/// <summary>
/// État figé d'une pastille du bandeau d'étapes du wizard (design/ui_kits/launcher/screen-wizard.jsx,
/// <c>STEPS</c>) à un instant donné. <see cref="WizardViewModel"/> reconstruit la liste entière à
/// chaque changement d'étape plutôt que de rendre ce type observable : quatre éléments, une
/// reconstruction est plus simple à lire et à tester qu'une notification en cascade.
/// </summary>
public sealed class WizardStepIndicatorViewModel(int number, string label, bool isDone, bool isCurrent)
{
    /// <summary>Rang affiché dans la pastille (1-based), masqué par la coche quand <see cref="IsDone"/>.</summary>
    public int Number { get; } = number;

    public string Label { get; } = label;

    public bool IsDone { get; } = isDone;

    public bool IsCurrent { get; } = isCurrent;

    public bool IsUpcoming { get; } = !isDone && !isCurrent;
}