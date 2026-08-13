using System.Text.RegularExpressions;

using Prospect.Core.ModDb;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Lit le journal du dernier lancement d'une instance et en tire, par mod, ce que le JEU a dit de
/// lui : erreurs et avertissements qui lui sont attribuables, et références qu'il fait au contenu
/// d'un autre mod (docs/architecture.md, niveau 3 des dépendances, « intégrations non déclarées »).
/// Calcul pur : une suite de lignes entre, un rapport sort, rien n'est lu ni écrit sur le disque.
/// </summary>
/// <remarks>
/// <para>
/// Les formes reconnues viennent d'une session réelle du jeu (Vintage Story 1.22.6, journal client
/// et serveur), pas d'une spécification : le moteur écrit ses entrées sous la forme
/// <c>13.8.2026 22:12:06 [Server Error] &lt;message&gt;</c> sur la sortie standard (celle que
/// Prospect capture) et sous la forme <c>[Error]</c> sans côté dans ses propres fichiers de
/// journal. Une ligne qui ne porte pas d'entête est rattachée à l'entrée précédente si elle est
/// indentée (les trames d'une pile d'exception le sont), sinon elle est ignorée.
/// </para>
/// <para>
/// L'attribution est HEURISTIQUE et son résultat est informatif, jamais bloquant. Ce qu'elle sait
/// faire, dans l'ordre de fiabilité décroissante : le préfixe que le chargeur de mods pose
/// lui-même (<c>[carryon] …</c>, ou <c>[monmod-1.0.0.zip] …</c> quand le <c>modinfo.json</c> n'a
/// pas pu être lu), le domaine porté par les messages du chargeur de patches JSON
/// (<c>Patch 2 in carryon:patches/x.json …</c>), le nom de type d'un système de mod déclaré par le
/// journal lui-même (bloc <c>Mod 'carryon-2.0.0-pre.8.zip' (carryon):</c>), puis le rapprochement
/// d'un segment de nom de type avec un <c>modid</c> connu (<c>CarryOn.CarrySystem</c> →
/// <c>carryon</c>). Ce qu'elle ne sait PAS faire, et qu'il vaut mieux écrire que promettre : une
/// ligne qu'un mod écrit lui-même par l'API du jeu n'est marquée nulle part comme venant de lui,
/// elle reste donc non attribuée sauf si le mod a pris soin de la préfixer de son propre nom.
/// </para>
/// </remarks>
public static partial class GameLogAnalyzer
{
    /// <summary>
    /// Nombre de lignes lues au maximum. Un lancement se joue dans les premières centaines de
    /// lignes ; ce plafond borne le travail et la mémoire sur un journal qu'une session de plusieurs
    /// heures a fait grossir, au prix assumé d'ignorer ce qui arrive bien après le démarrage.
    /// </summary>
    public const int MaxLines = 20_000;

    /// <summary>Lignes d'exemple retenues par mod : de quoi montrer, pas de quoi recopier le journal.</summary>
    public const int MaxSamplesPerMod = 3;

    /// <summary>Longueur au-delà de laquelle une ligne d'exemple est coupée (une pile d'exception tient sur une ligne interminable).</summary>
    public const int MaxSampleLength = 220;

    /// <summary>Intégrations distinctes retenues au maximum, tous mods confondus.</summary>
    public const int MaxIntegrations = 200;

    // Entrées à problème qu'aucune règle n'a su attribuer au moment où elles ont été lues : elles
    // sont réessayées à la fin, quand le vocabulaire du journal est complet (le bloc qui nomme les
    // systèmes de chaque mod n'arrive qu'après les erreurs de chargement).
    private const int MaxDeferredEntries = 300;

    // Trames de pile gardées par entrée pour l'attribution : la première suffit presque toujours à
    // nommer le mod fautif, les suivantes descendent dans le moteur.
    private const int MaxContinuationLines = 4;

    /// <summary>
    /// Analyse les lignes d'un journal de lancement.
    /// </summary>
    /// <param name="lines">
    /// Lignes du journal, dans l'ordre. Consommées une seule fois et paresseusement : un appelant
    /// peut donc passer une lecture en flux plutôt que le fichier entier en mémoire.
    /// </param>
    /// <param name="knownMods">
    /// Mods installés aujourd'hui, s'ils sont connus : ils enrichissent le vocabulaire d'attribution
    /// (un mod dont le journal ne parle jamais reste reconnaissable par son nom de fichier). Le
    /// journal se suffit à lui-même quand ce paramètre est absent.
    /// </param>
    public static GameLogReport Analyze(IEnumerable<string> lines, IReadOnlyCollection<ModLogIdentity>? knownMods = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var vocabulary = new Vocabulary(knownMods);
        var accumulator = new Accumulator();
        var deferred = new List<DeferredEntry>();
        var pending = (Entry?)null;

        // Take plutôt qu'un compteur et un break : il cesse de tirer la source exactement au
        // plafond, là où une boucle qui compte aurait déjà lu la ligne de trop.
        foreach (var line in lines.Take(MaxLines))
        {
            if (line is null)
            {
                continue;
            }

            var header = EntryPattern().Match(line);
            if (header.Success)
            {
                Consume(pending, vocabulary, accumulator, deferred);
                pending = new Entry(SeverityOf(header.Groups["marker"].Value), header.Groups["message"].Value);
                continue;
            }

            if (pending is not null && IsContinuation(line))
            {
                pending.AddContinuation(line);
                continue;
            }

            // Ligne étrangère au format du jeu : l'entête que Prospect écrit en tête de journal, ou
            // le bavardage d'une bibliothèque native. Elle ferme l'entrée en cours sans rien lui
            // ajouter, plutôt que de se faire passer pour la suite de son message.
            Consume(pending, vocabulary, accumulator, deferred);
            pending = null;
        }

        Consume(pending, vocabulary, accumulator, deferred);

        foreach (var entry in deferred)
        {
            if (Attribute(entry.Message, entry.Continuations, vocabulary) is { } modId)
            {
                accumulator.Add(modId, entry.Severity, entry.Message);
            }
        }

        return new GameLogReport(accumulator.BuildMods(), accumulator.BuildIntegrations(), vocabulary.Observed);
    }

    /// <summary>
    /// Vrai si <paramref name="line"/> prolonge l'entrée précédente plutôt que d'en ouvrir une
    /// nouvelle : le jeu indente les trames de pile (<c>   at Machin.Truc()</c>) et les blocs
    /// détaillés, et n'écrit jamais d'entête sur ces lignes-là.
    /// </summary>
    private static bool IsContinuation(string line)
        => line.Length > 0 && char.IsWhiteSpace(line[0]) && line.AsSpan().TrimStart().Length > 0;

    // Une entrée complète : mise à jour du vocabulaire (elle peut nommer des mods), extraction des
    // références inter-domaines, puis attribution si elle rapporte un problème.
    private static void Consume(Entry? entry, Vocabulary vocabulary, Accumulator accumulator, List<DeferredEntry> deferred)
    {
        if (entry is null)
        {
            return;
        }

        vocabulary.Observe(entry.Message, accumulator);
        CollectIntegrations(entry.Message, accumulator);

        if (entry.Severity == GameLogSeverity.Info)
        {
            return;
        }

        if (Attribute(entry.Message, entry.Continuations, vocabulary) is { } modId)
        {
            accumulator.Add(modId, entry.Severity, entry.Message);

            return;
        }

        if (deferred.Count < MaxDeferredEntries)
        {
            deferred.Add(new DeferredEntry(entry.Severity, entry.Message, entry.Continuations));
        }
    }

    // Les seules intégrations que le journal RÉVÈLE sont les références qui ont échoué : un patch
    // appliqué avec succès ne laisse aucune ligne, seulement un compte agrégé en fin de chargement.
    // Ce que le journal apporte est donc la référence MANQUANTE ; les intégrations qui fonctionnent
    // viennent de l'analyse statique des archives (voir ModIntegrationScanner).
    private static void CollectIntegrations(string message, Accumulator accumulator)
    {
        var missing = PatchMissingFilePattern().Match(message);
        if (!missing.Success)
        {
            return;
        }

        var source = missing.Groups["source"].Value;
        var target = missing.Groups["target"].Value;
        if (IsForeignDomain(source, target))
        {
            accumulator.AddIntegration(new ModIntegration(source, target, ModIntegrationNature.Missing, Truncate(message)));
        }
    }

    private static bool IsForeignDomain(string source, string target)
        => !string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
            && !ModInfoParser.IsSpecialDependencyId(target)
            && !ModInfoParser.IsSpecialDependencyId(source);

    private static string? Attribute(string message, IReadOnlyList<string> continuations, Vocabulary vocabulary)
    {
        // 1. Le préfixe posé par le chargeur de mods lui-même : « [carryon] … ». C'est la seule
        //    marque que le moteur écrit dans l'intention de désigner un mod, donc la première à lire.
        //    Un mod dont le modinfo.json n'a pas pu être lu n'a pas de modid à donner : le jeu le
        //    nomme alors par son ARCHIVE (« [monmod-1.0.0.zip] »), et ce mod-là n'apparaîtra dans
        //    aucune des listes du journal, puisqu'il n'a pas été chargé. Un jeton qui se termine
        //    par .zip est donc accepté tel quel : rien d'autre dans ce journal n'a cette forme.
        var bracket = BracketPrefixPattern().Match(message);
        if (bracket.Success && vocabulary.ResolveOrDeclareArchive(bracket.Groups["token"].Value) is { } bracketed)
        {
            return bracketed;
        }

        // 2. Les messages du chargeur de patches JSON portent le domaine du mod qui a écrit le
        //    patch, ce qui l'identifie sans ambiguïté même si rien d'autre ne l'a encore nommé.
        var patch = PatchOwnerPattern().Match(message);
        if (patch.Success)
        {
            var domain = patch.Groups["source"].Value;
            if (vocabulary.Resolve(domain) is { } known)
            {
                return known;
            }

            if (!ModInfoParser.IsSpecialDependencyId(domain))
            {
                return domain;
            }
        }

        // 3. et 4. Les noms de type : ceux que le journal a explicitement rattachés à un mod, puis à
        //    défaut ceux dont un segment est exactement un modid connu.
        if (ResolveByTypeName(message, vocabulary) is { } typed)
        {
            return typed;
        }

        foreach (var continuation in continuations)
        {
            if (ResolveByTypeName(continuation, vocabulary) is { } fromStack)
            {
                return fromStack;
            }
        }

        // 5. Le préfixe qu'un mod se donne à lui-même (« CarryOn: … »), retenu seulement quand il
        //    correspond exactement à une identité connue : sans cette exigence, « Error: … » ou
        //    « Warning: … » deviendraient des noms de mods.
        var self = SelfPrefixPattern().Match(message);

        return self.Success ? vocabulary.Resolve(self.Groups["name"].Value) : null;
    }

    private static string? ResolveByTypeName(string text, Vocabulary vocabulary)
    {
        string? bySegment = null;

        foreach (Match match in DottedIdentifierPattern().Matches(text))
        {
            var identifier = match.Value;
            if (vocabulary.ResolveSystemType(identifier) is { } declared)
            {
                return declared;
            }

            // Le segment le PLUS LONG gagne : « CarryOn.CarryOnLib.CarryOnLibSystem » contient à la
            // fois « CarryOn » (le mod carryon) et « CarryOnLib » (le mod carryonlib), et c'est le
            // second qui décrit ce type. Deux mods d'un même auteur partagent souvent leur racine.
            foreach (var segment in identifier.Split('.'))
            {
                if (vocabulary.Resolve(segment) is { } candidate && segment.Length > (bySegment?.Length ?? 0))
                {
                    bySegment = candidate;
                }
            }
        }

        return bySegment;
    }

    // Le marqueur vaut « Server Error », « Client Warning », « Error », « VerboseDebug »… : c'est
    // son dernier mot qui porte le niveau, le premier ne dit que de quel côté vient la ligne.
    private static GameLogSeverity SeverityOf(string marker)
    {
        var level = marker.AsSpan().Trim();
        var space = level.LastIndexOf(' ');
        if (space >= 0)
        {
            level = level[(space + 1)..];
        }

        if (level.Equals("Error", StringComparison.OrdinalIgnoreCase) || level.Equals("Fatal", StringComparison.OrdinalIgnoreCase))
        {
            return GameLogSeverity.Error;
        }

        return level.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? GameLogSeverity.Warning : GameLogSeverity.Info;
    }

    private static string Truncate(string line)
    {
        var trimmed = line.Trim();

        return trimmed.Length <= MaxSampleLength ? trimmed : string.Concat(trimmed.AsSpan(0, MaxSampleLength), "…");
    }

    [GeneratedRegex(@"^\d{1,2}\.\d{1,2}\.\d{4} \d{1,2}:\d{2}:\d{2}(?:\.\d+)? \[(?<marker>[^\]]{1,32})\]\s?(?<message>.*)$")]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"^\[(?<token>[^\[\]]{1,80})\]\s*")]
    private static partial Regex BracketPrefixPattern();

    [GeneratedRegex(@"^Patch \d+ in (?<source>[\w.\-]+):\S* ?: File (?<target>[\w.\-]+):\S* not found")]
    private static partial Regex PatchMissingFilePattern();

    [GeneratedRegex(@"^(?:Patch \d+(?: \(target: [\w.\-]+:[^)]*\))? in |Patch file |Failed loading patches file )(?<source>[\w.\-]+):")]
    private static partial Regex PatchOwnerPattern();

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+")]
    private static partial Regex DottedIdentifierPattern();

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9 _.\-]{1,29}): \S")]
    private static partial Regex SelfPrefixPattern();

    [GeneratedRegex(@"^Mods, sorted by dependency: (?<list>.+)$")]
    private static partial Regex SortedModsPattern();

    [GeneratedRegex(@"^\s*Mod '(?<file>[^']+)' \((?<modid>[\w.\-]+)\):\s*$")]
    private static partial Regex SystemsBlockModPattern();

    [GeneratedRegex(@"^\s+(?<type>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*$")]
    private static partial Regex SystemsBlockTypePattern();

    [GeneratedRegex(@"mod@(?<file>[^,]+\.zip)")]
    private static partial Regex ExternalOriginPattern();

    // Une entrée en cours de lecture : son entête a été reconnu, ses lignes de continuation
    // arrivent peut-être encore.
    private sealed class Entry(GameLogSeverity severity, string message)
    {
        private List<string>? _continuations;

        public GameLogSeverity Severity { get; } = severity;

        public string Message { get; } = message;

        public IReadOnlyList<string> Continuations => _continuations ?? (IReadOnlyList<string>)[];

        public void AddContinuation(string line)
        {
            _continuations ??= new List<string>(MaxContinuationLines);
            if (_continuations.Count < MaxContinuationLines)
            {
                _continuations.Add(line);
            }
        }
    }

    private sealed record DeferredEntry(GameLogSeverity Severity, string Message, IReadOnlyList<string> Continuations);

    /// <summary>
    /// Tout ce que le journal a permis d'apprendre sur QUI est un mod : identifiants, noms de
    /// fichiers, noms affichés, et noms de types de systèmes. Construit au fil de la lecture, parce
    /// que le journal se présente lui-même avant de parler des mods qu'il charge.
    /// </summary>
    private sealed class Vocabulary
    {
        private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _systemTypes = new(StringComparer.Ordinal);
        private readonly List<string> _observed = [];
        private string? _currentSystemsMod;

        public Vocabulary(IReadOnlyCollection<ModLogIdentity>? knownMods)
        {
            foreach (var identity in knownMods ?? [])
            {
                Declare(identity.ModId);
                Alias(identity.FileName, identity.ModId);
                Alias(identity.DisplayName, identity.ModId);
            }
        }

        public IReadOnlyList<string> Observed => _observed;

        public string? Resolve(string token) => _tokens.GetValueOrDefault(token.Trim());

        /// <summary>
        /// Comme <see cref="Resolve"/>, mais accepte aussi un nom d'archive jamais vu : c'est
        /// ainsi que le jeu désigne un mod qu'il n'a pas su lire, et un tel mod ne sera nommé
        /// nulle part ailleurs dans le journal.
        /// </summary>
        public string? ResolveOrDeclareArchive(string token)
        {
            var trimmed = token.Trim();

            return Resolve(trimmed) ?? (trimmed.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? Declare(trimmed) : null);
        }

        public string? ResolveSystemType(string typeName) => _systemTypes.GetValueOrDefault(typeName);

        public void Observe(string message, Accumulator accumulator)
        {
            var sorted = SortedModsPattern().Match(message);
            if (sorted.Success)
            {
                foreach (var modId in sorted.Groups["list"].Value.Split(','))
                {
                    var trimmed = modId.Trim();
                    if (trimmed.Length > 0 && !ModInfoParser.IsSpecialDependencyId(trimmed))
                    {
                        Declare(trimmed);
                    }
                }

                return;
            }

            // Les origines externes ne donnent que des noms d'archives : ils deviennent des clés
            // reconnaissables, mais pas des identités à part entière : le bloc des systèmes, plus
            // bas, les reliera à leur modid.
            foreach (Match origin in ExternalOriginPattern().Matches(message))
            {
                Alias(origin.Groups["file"].Value.Trim(), origin.Groups["file"].Value.Trim());
            }

            var blockMod = SystemsBlockModPattern().Match(message);
            if (blockMod.Success)
            {
                var modId = blockMod.Groups["modid"].Value;
                var file = blockMod.Groups["file"].Value;
                _currentSystemsMod = ModInfoParser.IsSpecialDependencyId(modId) ? null : Declare(modId);

                // Le nom de fichier a pu servir de clé avant que ce bloc ne le relie à son modid
                // (une erreur de chargement le nomme ainsi) : les deux comptes se rejoignent ici,
                // sans quoi le même mod apparaîtrait deux fois dans le rapport.
                if (_currentSystemsMod is { } canonical && !string.Equals(file, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    accumulator.Merge(file, canonical);
                    Alias(file, canonical, replace: true);
                }

                return;
            }

            var blockType = SystemsBlockTypePattern().Match(message);
            if (blockType.Success && _currentSystemsMod is { } owner)
            {
                _systemTypes[blockType.Groups["type"].Value] = owner;

                return;
            }

            // Toute autre ligne referme le bloc : ses lignes de types sont contiguës.
            if (message.Length > 0)
            {
                _currentSystemsMod = null;
            }
        }

        private string Declare(string modId)
        {
            if (_tokens.TryGetValue(modId, out var existing))
            {
                return existing;
            }

            _tokens[modId] = modId;
            _observed.Add(modId);

            return modId;
        }

        private void Alias(string? token, string modId, bool replace = false)
        {
            if (token is { Length: > 0 } && (replace || !_tokens.ContainsKey(token)))
            {
                _tokens[token] = modId;
            }
        }
    }

    /// <summary>Compteurs et échantillons par mod, plus les intégrations dédoublonnées.</summary>
    private sealed class Accumulator
    {
        private readonly Dictionary<string, ModTally> _mods = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ModIntegration> _integrations = [];

        public void Add(string modId, GameLogSeverity severity, string message)
        {
            if (!_mods.TryGetValue(modId, out var tally))
            {
                tally = new ModTally(modId);
                _mods[modId] = tally;
            }

            tally.Add(severity, message);
        }

        public void Merge(string fromKey, string toKey)
        {
            if (!_mods.Remove(fromKey, out var source))
            {
                return;
            }

            if (_mods.TryGetValue(toKey, out var target))
            {
                target.Absorb(source);

                return;
            }

            source.Rename(toKey);
            _mods[toKey] = source;
        }

        public void AddIntegration(ModIntegration integration)
        {
            if (_integrations.Count >= MaxIntegrations)
            {
                return;
            }

            var known = _integrations.Any(existing
                => string.Equals(existing.SourceModId, integration.SourceModId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.TargetModId, integration.TargetModId, StringComparison.OrdinalIgnoreCase)
                && existing.Nature == integration.Nature);

            if (!known)
            {
                _integrations.Add(integration);
            }
        }

        public IReadOnlyList<ModLogInsight> BuildMods()
            => [.. _mods.Values.Select(tally => tally.Build()).OrderByDescending(insight => insight.Severity).ThenBy(insight => insight.ModId, StringComparer.OrdinalIgnoreCase)];

        public List<ModIntegration> BuildIntegrations() => _integrations;

        private sealed class ModTally(string modId)
        {
            private readonly List<string> _samples = [];

            public string ModId { get; private set; } = modId;

            public int Errors { get; private set; }

            public int Warnings { get; private set; }

            public void Add(GameLogSeverity severity, string message)
            {
                if (severity == GameLogSeverity.Error)
                {
                    Errors++;
                }
                else
                {
                    Warnings++;
                }

                if (_samples.Count < MaxSamplesPerMod)
                {
                    _samples.Add(Truncate(message));
                }
            }

            public void Absorb(ModTally other)
            {
                Errors += other.Errors;
                Warnings += other.Warnings;
                foreach (var sample in other._samples)
                {
                    if (_samples.Count < MaxSamplesPerMod)
                    {
                        _samples.Add(sample);
                    }
                }
            }

            public void Rename(string modId) => ModId = modId;

            public ModLogInsight Build() => new(ModId, Errors, Warnings, _samples);
        }
    }
}