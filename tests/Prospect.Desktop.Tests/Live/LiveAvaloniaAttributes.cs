using Prospect.Tests.Live;

using Xunit.Sdk;

namespace Prospect.Desktop.Tests.Live;

/// <summary>
/// Un test live qui a besoin du fil UI Avalonia headless : combine l'opt-in réseau de
/// <see cref="LiveOptIn"/> et le découvreur d'<c>Avalonia.Headless.XUnit.AvaloniaFactAttribute</c>.
/// </summary>
/// <remarks>
/// <c>AvaloniaFactAttribute</c> est scellée : impossible d'en hériter pour surcharger <c>Skip</c>.
/// Cet attribut la MIROITE donc, exactement comme <c>ConformanceFactAttribute</c> miroite
/// <c>AtlasScenarioAttribute</c> : même classe de base <see cref="FactAttribute"/>, et le même
/// découvreur désigné par son nom de type et d'assembly. XUnit lit <c>Skip</c> par son nom au
/// moment de la découverte, donc un motif non nul suffit à ce qu'aucune fenêtre ne soit créée et
/// qu'aucune requête ne parte.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")]
public sealed class LiveAvaloniaFactAttribute : FactAttribute
{
    /// <summary>Construit l'attribut, en lisant l'opt-in immédiatement.</summary>
    public LiveAvaloniaFactAttribute() => Skip = LiveOptIn.SkipReason;
}

/// <summary>
/// Variante paramétrée de <see cref="LiveAvaloniaFactAttribute"/>, miroir d'
/// <c>Avalonia.Headless.XUnit.AvaloniaTheoryAttribute</c> (scellée elle aussi). Quand
/// <c>Skip</c> est posé, <c>TheoryDiscoverer</c> rend un unique cas ignoré sans jamais énumérer
/// les données : les fiches réelles ne sont même pas nommées tant que l'opt-in est absent.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaTheoryDiscoverer", "Avalonia.Headless.XUnit")]
public sealed class LiveAvaloniaTheoryAttribute : TheoryAttribute
{
    /// <summary>Construit l'attribut, en lisant l'opt-in immédiatement.</summary>
    public LiveAvaloniaTheoryAttribute() => Skip = LiveOptIn.SkipReason;
}