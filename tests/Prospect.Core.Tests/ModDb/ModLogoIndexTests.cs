using Prospect.Core.ModDb;

using Shouldly;

namespace Prospect.Core.Tests.ModDb;

/// <summary>
/// La table qui relie un identifiant de fiche à son logo. Du calcul pur : ce qui se vérifie ici
/// est ce que les écrans qui ne connaissent qu'un identifiant obtiendront, et surtout ce qu'ils
/// n'obtiendront PAS.
/// </summary>
public sealed class ModLogoIndexTests
{
    [Fact]
    public void Build_indexe_les_fiches_qui_annoncent_un_logo()
    {
        var index = ModLogoIndex.Build([
            Summary(792, "https://moddbcdn.vintagestory.at/betterruins.png"),
            Summary(890, "https://moddbcdn.vintagestory.at/CarryOnLogo.png"),
        ]);

        index.Count.ShouldBe(2);
        index.Find(792).ShouldBe(new Uri("https://moddbcdn.vintagestory.at/betterruins.png"));
        index.Find(890).ShouldBe(new Uri("https://moddbcdn.vintagestory.at/CarryOnLogo.png"));
    }

    /// <summary>
    /// Un tiers du catalogue réel n'a pas de logo (docs/research/moddb-api.md). Ces fiches-là
    /// n'entrent pas dans la table : les y faire entrer avec une valeur nulle lui ferait porter
    /// deux mille cinq cents entrées qui ne répondent jamais rien.
    /// </summary>
    [Fact]
    public void Build_ignore_les_fiches_sans_logo()
    {
        var index = ModLogoIndex.Build([
            Summary(1783, logoUrl: null),
            Summary(792, "https://moddbcdn.vintagestory.at/betterruins.png"),
        ]);

        index.Count.ShouldBe(1);
        index.Find(1783).ShouldBeNull();
    }

    [Fact]
    public void Find_rend_null_pour_une_fiche_absente_du_catalogue()
        => ModLogoIndex.Build([Summary(792, "https://moddbcdn.vintagestory.at/betterruins.png")])
            .Find(4687)
            .ShouldBeNull();

    [Fact]
    public void Empty_ne_repond_jamais_rien()
    {
        ModLogoIndex.Empty.Count.ShouldBe(0);
        ModLogoIndex.Empty.Find(792).ShouldBeNull();
    }

    /// <summary>
    /// L'API ne peut pas rendre deux fois le même identifiant (c'est une clé primaire), mais cette
    /// table décore : elle absorbe plutôt que de faire tomber l'écran qui l'a demandée.
    /// </summary>
    [Fact]
    public void Build_absorbe_un_identifiant_en_double_sans_lever()
    {
        var index = ModLogoIndex.Build([
            Summary(792, "https://moddbcdn.vintagestory.at/premier.png"),
            Summary(792, "https://moddbcdn.vintagestory.at/second.png"),
        ]);

        index.Count.ShouldBe(1);
        index.Find(792).ShouldBe(new Uri("https://moddbcdn.vintagestory.at/premier.png"));
    }

    [Fact]
    public void Build_refuse_une_liste_nulle()
        => Should.Throw<ArgumentNullException>(() => ModLogoIndex.Build(null!));

    private static ModDbModSummary Summary(int modId, string? logoUrl) => new()
    {
        ModId = modId,
        Name = $"Mod {modId}",
        LogoUrl = logoUrl is null ? null : new Uri(logoUrl),
    };
}