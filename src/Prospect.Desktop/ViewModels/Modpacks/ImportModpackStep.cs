namespace Prospect.Desktop.ViewModels.Modpacks;

/// <summary>
/// Étape du dialogue de flux d'import (design : un seul panneau qui change de contenu, voir
/// <see cref="ImportModpackViewModel"/>). Distinct de <c>Prospect.Core.Modpacks.ModpackImportPhase</c>,
/// qui décrit l'avancement PENDANT <see cref="Importing"/>, pas les étapes du dialogue lui-même.
/// </summary>
public enum ImportModpackStep
{
    /// <summary>Lecture et validation du manifest en cours.</summary>
    Loading,

    /// <summary>Aperçu affiché : nom, version de jeu, nombre de mods, nom d'instance modifiable.</summary>
    Preview,

    /// <summary>Import en cours : version de jeu si besoin, puis mods un par un.</summary>
    Importing,

    /// <summary>Rapport final, groupé par catégorie.</summary>
    Report,

    /// <summary>Le manifest n'a pas pu être lu, ou l'import a échoué en bloc.</summary>
    Failed,
}