namespace Prospect.Desktop.ViewModels.Migration;

/// <summary>Étape du dialogue de flux d'adoption (voir <see cref="AdoptVslViewModel"/>).</summary>
public enum AdoptVslStep
{
    /// <summary>Liste des installations et moteurs détectés, cases à cocher.</summary>
    Selection,

    /// <summary>Adoption en cours : installations d'abord, puis moteurs.</summary>
    Adopting,

    /// <summary>Rapport final, groupé par catégorie.</summary>
    Report,
}