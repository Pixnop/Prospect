using Avalonia.Controls;
using Avalonia.Threading;

namespace Prospect.Desktop.Tests;

/// <summary>Fait avancer la boucle du dispatcher headless puis relance la mise en page, pour que les changements de ViewModel se reflètent avant une assertion sur l'arbre visuel.</summary>
internal static class HeadlessTestHelpers
{
    public static void Settle(this Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}