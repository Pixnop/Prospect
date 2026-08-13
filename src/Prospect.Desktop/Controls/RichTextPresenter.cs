using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Prospect.Core.ModDb;
using Prospect.Desktop.ViewModels.Mods;

namespace Prospect.Desktop.Controls;

/// <summary>
/// Rend une description de fiche ModDB : la moitié « contrôles » du dispositif dont
/// <see cref="HtmlRichTextParser"/> est la moitié « calcul ».
/// </summary>
/// <remarks>
/// <para>
/// Un arbre de contrôles construit en C# plutôt qu'en AXAML, parce que la forme du document n'est
/// pas connue à la compilation : une description enchaîne des paragraphes, des titres, des listes
/// imbriquées, du code et des images dans un ordre que seul l'auteur décide. Un
/// <c>ItemsControl</c> à <c>DataTemplate</c> par type de bloc y arriverait pour les blocs plats,
/// mais pas pour les listes imbriquées ni pour les enrichissements EN LIGNE (gras dans un lien,
/// code dans une puce), qui vivent dans la collection <c>Inlines</c> d'un <c>TextBlock</c> et
/// qu'aucun binding compilé ne sait remplir.
/// </para>
/// <para>
/// L'APPARENCE, elle, reste dans le design system : chaque contrôle produit ici porte une classe
/// (<c>richBody</c>, <c>richHeading</c>, <c>richCode</c>...) dont les couleurs, tailles et
/// espacements sont définis dans Styles/Controls/RichText.axaml. Ce fichier décide de la
/// structure, jamais des valeurs.
/// </para>
/// </remarks>
public sealed class RichTextPresenter : Decorator
{
    /// <summary>Description à rendre, ou <see langword="null"/>.</summary>
    public static readonly StyledProperty<RichTextDocumentViewModel?> DocumentProperty =
        AvaloniaProperty.Register<RichTextPresenter, RichTextDocumentViewModel?>(nameof(Document));

    /// <summary>Largeur maximale d'une illustration, en points.</summary>
    public static readonly StyledProperty<double> ImageMaxWidthProperty =
        AvaloniaProperty.Register<RichTextPresenter, double>(nameof(ImageMaxWidth), 640d);

    /// <summary>
    /// Hauteur maximale d'une illustration, en points. Une capture verticale sortie d'un téléphone
    /// occuperait sinon plusieurs écrans de défilement à elle seule.
    /// </summary>
    public static readonly StyledProperty<double> ImageMaxHeightProperty =
        AvaloniaProperty.Register<RichTextPresenter, double>(nameof(ImageMaxHeight), 420d);

    /// <summary>Retrait horizontal d'un niveau de liste imbriquée, en points.</summary>
    private const double NestingIndent = 18d;

    static RichTextPresenter()
        => DocumentProperty.Changed.AddClassHandler<RichTextPresenter>((presenter, _) => presenter.Rebuild());

    /// <inheritdoc cref="DocumentProperty" />
    public RichTextDocumentViewModel? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <inheritdoc cref="ImageMaxWidthProperty" />
    public double ImageMaxWidth
    {
        get => GetValue(ImageMaxWidthProperty);
        set => SetValue(ImageMaxWidthProperty, value);
    }

    /// <inheritdoc cref="ImageMaxHeightProperty" />
    public double ImageMaxHeight
    {
        get => GetValue(ImageMaxHeightProperty);
        set => SetValue(ImageMaxHeightProperty, value);
    }

    private void Rebuild()
    {
        if (Document is not { IsEmpty: false } document)
        {
            Child = null;

            return;
        }

        var stack = new StackPanel { Spacing = 10 };
        foreach (var control in BuildBlocks(document, document.Document.Blocks))
        {
            stack.Children.Add(control);
        }

        Child = stack;
    }

    private IEnumerable<Control> BuildBlocks(RichTextDocumentViewModel document, IEnumerable<RichTextBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block switch
            {
                RichTextParagraph paragraph => BuildText(document, paragraph.Runs, "richBody"),
                RichTextHeading heading => BuildText(document, heading.Runs, "richHeading", $"richHeading{Math.Clamp(heading.Level, 1, 6)}"),
                RichTextList list => BuildList(document, list),
                RichTextCodeBlock code => BuildCode(code),
                RichTextImage image => BuildImage(document, image),
                _ => BuildRule(),
            };
        }
    }

    private static Border BuildRule() => new Border { Classes = { "richRule" }, Height = 1 };

    private static Border BuildCode(RichTextCodeBlock code) => new Border
    {
        Classes = { "richCode" },
        Child = new TextBlock
        {
            Text = code.Text,
            Classes = { "richCodeText" },
            // Enroulé plutôt que débordant : une ligne de configuration plus large que la colonne
            // se ferait sinon rogner net par le dialogue, sans que rien ne le signale.
            TextWrapping = TextWrapping.Wrap,
        },
    };

    private StackPanel BuildImage(RichTextDocumentViewModel document, RichTextImage block)
    {
        var image = document.ImageFor(block);

        var picture = new Image
        {
            Stretch = Stretch.Uniform,
            // Le cache a déjà réduit l'image à la largeur d'usage : DownOnly interdit de la
            // réagrandir, ce qui redonnerait à l'écran le flou qu'on vient d'éviter en mémoire.
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = ImageMaxWidth,
            MaxHeight = ImageMaxHeight,
        };
        picture.Bind(Image.SourceProperty, new Binding(nameof(RichTextImageViewModel.Bitmap)) { Source = image });
        picture.Bind(IsVisibleProperty, new Binding(nameof(RichTextImageViewModel.HasBitmap)) { Source = image });

        // Repli permanent dans l'arbre visuel, masqué dès que l'image arrive : même dispositif que
        // le pictogramme générique d'une carte sans logo. Une image qui n'arrive jamais laisse son
        // texte de remplacement plutôt qu'un trou muet.
        var fallback = new TextBlock
        {
            Text = image.AlternateText,
            Classes = { "richImageFallback" },
            TextWrapping = TextWrapping.Wrap,
        };
        fallback.Bind(
            IsVisibleProperty,
            new Binding(nameof(RichTextImageViewModel.HasBitmap)) { Source = image, Converter = BoolConverters.Not });

        var panel = new StackPanel { Children = { picture, fallback } };

        if (image.Link is { } link)
        {
            panel.Cursor = new Cursor(StandardCursorType.Hand);
            panel.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                document.OpenLink(link);
            };
        }

        return panel;
    }

    private StackPanel BuildList(RichTextDocumentViewModel document, RichTextList list)
    {
        var stack = new StackPanel { Spacing = 4 };

        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

            var marker = new TextBlock
            {
                Classes = { "richBullet" },
                Text = list.IsOrdered
                    ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{index + 1}.")
                    : "•",
            };
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var content = BuildText(document, item.Runs, "richBody");
            content.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);

            stack.Children.Add(row);

            foreach (var child in BuildBlocks(document, item.Children))
            {
                // Le retrait porte le niveau d'imbrication : les listes de la fiche réelle de
                // Carry On descendent jusqu'à trois niveaux.
                child.Margin = new Thickness(NestingIndent, 2, 0, 0);
                stack.Children.Add(child);
            }
        }

        return stack;
    }

    private static LinkableTextBlock BuildText(RichTextDocumentViewModel document, IReadOnlyList<RichTextRun> runs, params string[] classes)
    {
        var text = new LinkableTextBlock { TextWrapping = TextWrapping.Wrap, LinkActivated = document.OpenLink };
        foreach (var className in classes)
        {
            text.Classes.Add(className);
        }

        text.SetRuns(runs);

        return text;
    }
}