using System;

using Avalonia;

namespace Prospect.Desktop;

// Statique : la classe ne contient que des membres statiques, ce que Sonar (S1118) exige de
// traduire soit par un constructeur privé, soit par une classe statique ; la seconde option est
// la plus idiomatique ici et rend `sealed` redondant (une classe statique l'est déjà).
static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}