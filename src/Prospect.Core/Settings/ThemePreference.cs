namespace Prospect.Core.Settings;

/// <summary>
/// Choix de thème persisté dans <c>prospect.json</c> (docs/architecture.md, « prospect.json
/// (réglages globaux, v1 minimale) »). <see cref="System"/> délègue à la préférence de l'OS via la
/// mécanique <c>PlatformSettings</c> d'Avalonia plutôt que de dupliquer une détection : c'est
/// <c>Prospect.Desktop.Services.ThemeService</c> (le seul point du Desktop à toucher
/// <c>Avalonia.Styling.ThemeVariant</c>) qui fait la conversion, ce type reste donc un pur choix
/// utilisateur, sans la moindre référence à Avalonia.
/// </summary>
public enum ThemePreference
{
    Dark,
    Light,
    System,
}