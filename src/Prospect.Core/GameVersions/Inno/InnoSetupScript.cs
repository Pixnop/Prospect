namespace Prospect.Core.GameVersions.Inno;

/// <summary>Méthode de compression déclarée par l'en-tête pour les données de l'installeur.</summary>
internal enum InnoCompression
{
    /// <summary>Aucune : les données sont stockées telles quelles.</summary>
    Stored = 0,

    /// <summary>Deflate.</summary>
    Zlib = 1,

    /// <summary>bzip2.</summary>
    BZip2 = 2,

    /// <summary>LZMA première génération.</summary>
    Lzma1 = 3,

    /// <summary>LZMA2, celle qu'emploie l'installeur de Vintage Story.</summary>
    Lzma2 = 4,
}

/// <summary>
/// Une entrée de la section <c>[Files]</c> : où le fichier doit atterrir, et quel bloc de données
/// il consomme.
/// </summary>
/// <param name="Destination">
/// Chemin de destination tel qu'écrit dans le script, constantes comprises (<c>{app}\Lib\x.dll</c>).
/// </param>
/// <param name="Location">
/// Index de l'entrée de données correspondante, ou <see cref="NoLocation"/> pour une entrée qui
/// n'en a pas (le désinstalleur, que Setup fabrique lui-même).
/// </param>
internal readonly record struct InnoFileEntry(string Destination, uint Location)
{
    /// <summary>Valeur qui marque une entrée sans données.</summary>
    public const uint NoLocation = 0xFFFFFFFFu;

    /// <summary>Vrai quand l'entrée désigne réellement un bloc de données.</summary>
    public bool HasData => Location != NoLocation;
}

/// <summary>
/// Une entrée de la table d'emplacements : la position et la taille d'un fichier dans le flux de
/// données, plus son empreinte.
/// </summary>
/// <param name="ChunkOffset">Décalage du bloc compressé, relatif au début de la zone de données.</param>
/// <param name="FileOffset">Décalage du fichier À L'INTÉRIEUR du bloc décompressé.</param>
/// <param name="FileSize">Taille du fichier une fois reconstitué.</param>
/// <param name="Sha256">Empreinte SHA-256 du fichier reconstitué, filtre appliqué.</param>
/// <param name="CallInstructionOptimized">
/// Vrai quand le compilateur a transformé les adresses des instructions <c>CALL</c> et <c>JMP</c>
/// pour mieux compresser l'exécutable : il faut défaire la transformation à la lecture.
/// </param>
/// <param name="Encrypted">Vrai quand le bloc est chiffré, cas que Prospect refuse.</param>
/// <param name="Compressed">Vrai quand le bloc est compressé avec la méthode de l'en-tête.</param>
internal readonly record struct InnoDataEntry(
    uint ChunkOffset,
    ulong FileOffset,
    ulong FileSize,
    byte[] Sha256,
    bool CallInstructionOptimized,
    bool Encrypted,
    bool Compressed);

/// <summary>
/// Ce que le script d'un installeur Inno Setup déclare, réduit à ce dont une extraction a besoin.
/// </summary>
/// <remarks>
/// <para>
/// Les deux blocs d'en-tête sont lus INTÉGRALEMENT, et pas seulement jusqu'aux champs utiles. Le
/// format n'a pas d'index : chaque enregistrement commence là où le précédent finit, donc atteindre
/// la table des fichiers demande d'avoir traversé sans erreur les langues, les messages, les types,
/// les composants, les tâches et les répertoires. La contrepartie est un contrôle inespéré : quand
/// tout a été traversé, le premier bloc doit se terminer EXACTEMENT, à l'octet près. Un reliquat ou
/// un dépassement signent une grille de lecture fausse, et l'extraction est abandonnée avant d'avoir
/// écrit quoi que ce soit.
/// </para>
/// <para>
/// Les sections <c>[Registry]</c>, <c>[Run]</c> et <c>[Icons]</c> sont traversées sans être
/// conservées : Prospect ne les REJOUE pas. C'est un choix, pas un manque, et il est documenté dans
/// docs/architecture.md : ne rien écrire hors du dossier de version est précisément ce qui fait
/// disparaître la boîte de dialogue de l'installeur officiel.
/// </para>
/// </remarks>
internal sealed class InnoSetupScript
{
    private InnoSetupScript(
        InnoSetupVersion version,
        InnoCompression compression,
        IReadOnlyList<InnoFileEntry> files,
        IReadOnlyList<InnoDataEntry> dataEntries)
    {
        Version = version;
        Compression = compression;
        Files = files;
        DataEntries = dataEntries;
    }

    /// <summary>Version du format lue dans la chaîne d'identification.</summary>
    public InnoSetupVersion Version { get; }

    /// <summary>Méthode de compression des blocs de données.</summary>
    public InnoCompression Compression { get; }

    /// <summary>Entrées de la section <c>[Files]</c>, dans l'ordre du script.</summary>
    public IReadOnlyList<InnoFileEntry> Files { get; }

    /// <summary>Table des emplacements, indexée par <see cref="InnoFileEntry.Location"/>.</summary>
    public IReadOnlyList<InnoDataEntry> DataEntries { get; }

    /// <summary>
    /// Lit les deux blocs d'en-tête à partir d'un flux d'installeur positionné sur la chaîne
    /// d'identification.
    /// </summary>
    /// <param name="stream">Flux de l'installeur.</param>
    /// <param name="headerOffset">Décalage de la chaîne d'identification.</param>
    public static InnoSetupScript Read(Stream stream, long headerOffset)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Seek(headerOffset, SeekOrigin.Begin);

        var id = new byte[InnoSetupVersion.IdLength];
        ReadExactly(stream, id);

        if (!InnoSetupVersion.TryParse(id, out var version))
        {
            throw InnoFormatException.Corrupt("aucune chaîne d'identification Inno Setup à l'emplacement annoncé");
        }

        if (!version.IsSupported)
        {
            throw InnoFormatException.Unsupported($"format de données {version}");
        }

        var primary = new InnoRecordReader(InnoBlockReader.Read(stream));
        var header = ReadPrimaryBlock(primary, version, out var files);

        var secondary = new InnoRecordReader(InnoBlockReader.Read(stream));
        var dataEntries = ReadDataEntries(secondary, version, header.DataEntryCount);

        return new InnoSetupScript(version, header.Compression, files, dataEntries);
    }

    private static HeaderCounts ReadPrimaryBlock(
        InnoRecordReader reader,
        InnoSetupVersion version,
        out IReadOnlyList<InnoFileEntry> files)
    {
        var header = ReadHeader(reader, version);

        Repeat(reader, header.LanguageCount, ReadLanguage);
        Repeat(reader, header.MessageCount, ReadMessage);
        Repeat(reader, header.PermissionCount, static r => r.SkipString());
        Repeat(reader, header.TypeCount, ReadType);
        Repeat(reader, header.ComponentCount, ReadComponent);
        Repeat(reader, header.TaskCount, ReadTask);
        Repeat(reader, header.DirectoryCount, ReadDirectory);

        var entries = new InnoFileEntry[header.FileCount];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = ReadFile(reader);
        }

        files = entries;

        Repeat(reader, header.IconCount, ReadIcon);
        Repeat(reader, header.IniEntryCount, ReadIni);
        Repeat(reader, header.RegistryEntryCount, ReadRegistry);
        Repeat(reader, header.DeleteEntryCount + header.UninstallDeleteEntryCount, ReadDelete);
        Repeat(reader, header.RunEntryCount + header.UninstallRunEntryCount, ReadRun);

        ReadWizardImages(reader);
        ReadWizardImages(reader);

        // Bibliothèque de décompression embarquée : présente seulement pour les méthodes qui ne
        // sont pas dans Setup lui-même. LZMA2 n'en fait pas partie.
        if (header.Compression is InnoCompression.BZip2 or InnoCompression.Zlib)
        {
            reader.SkipString();
        }

        if (reader.Remaining != 0)
        {
            throw InnoFormatException.Corrupt(
                $"{reader.Remaining} octets inattendus à la fin du bloc d'en-tête principal");
        }

        return header;
    }

    /// <remarks>
    /// L'ordre et la présence de chaque champ viennent des sources d'Inno Setup
    /// (<c>Projects/Src/Shared.Struct.pas</c>), recoupées avec le lecteur d'innoextract. Rien n'est
    /// deviné : ce qui n'est pas certain est refusé plus haut, par le filtre de version.
    /// </remarks>
    private static HeaderCounts ReadHeader(InnoRecordReader reader, InnoSetupVersion version)
    {
        // Trente-deux chaînes de description : noms, URL, chemins par défaut, mutex, expressions
        // d'architecture. Aucune ne sert à l'extraction, toutes doivent être traversées.
        for (var i = 0; i < 32; i++)
        {
            reader.SkipString();
        }

        if (version.HasCloseApplicationsFilterExcludes)
        {
            reader.SkipString();
        }

        // Licence, textes d'information et bytecode de la section [Code].
        for (var i = 0; i < 4; i++)
        {
            reader.SkipString();
        }

        var header = new HeaderCounts
        {
            LanguageCount = ReadCount(reader),
            MessageCount = ReadCount(reader),
            PermissionCount = ReadCount(reader),
            TypeCount = ReadCount(reader),
            ComponentCount = ReadCount(reader),
            TaskCount = ReadCount(reader),
            DirectoryCount = ReadCount(reader),
            FileCount = ReadCount(reader),
            DataEntryCount = ReadCount(reader),
            IconCount = ReadCount(reader),
            IniEntryCount = ReadCount(reader),
            RegistryEntryCount = ReadCount(reader),
            DeleteEntryCount = ReadCount(reader),
            UninstallDeleteEntryCount = ReadCount(reader),
            RunEntryCount = ReadCount(reader),
            UninstallRunEntryCount = ReadCount(reader),
        };

        reader.SkipWindowsVersionRange();

        if (!version.HasFlatWizardColours)
        {
            reader.Skip(8); // deux couleurs de fond, retirées par Inno Setup 6.4.0.1
        }

        reader.Skip(1);  // style d'assistant
        reader.Skip(8);  // pourcentages de redimensionnement
        reader.Skip(1);  // format alpha des images
        reader.Skip(4);  // début de l'empreinte du mot de passe
        reader.Skip(44); // sel de dérivation, itérations et nonce
        reader.Skip(8);  // espace disque supplémentaire requis
        reader.Skip(4);  // tranches par disque
        reader.Skip(1);  // mode de journal de désinstallation
        reader.Skip(1);  // avertissement de dossier existant
        reader.Skip(1);  // privilèges requis
        reader.ReadFlags(2); // surcharges de privilèges autorisées
        reader.Skip(1);  // affichage du dialogue de langue
        reader.Skip(1);  // détection de langue

        var compression = reader.ReadByte();

        reader.Skip(1); // page de dossier désactivée
        reader.Skip(1); // page de groupe désactivée
        reader.Skip(8); // taille affichée dans « Programmes et fonctionnalités »

        reader.ReadFlags(version.HeaderFlagCount);

        header.Compression = compression switch
        {
            (byte)InnoCompression.Stored => InnoCompression.Stored,
            (byte)InnoCompression.Zlib => InnoCompression.Zlib,
            (byte)InnoCompression.BZip2 => InnoCompression.BZip2,
            (byte)InnoCompression.Lzma1 => InnoCompression.Lzma1,
            (byte)InnoCompression.Lzma2 => InnoCompression.Lzma2,
            _ => throw InnoFormatException.Unsupported($"méthode de compression inconnue ({compression})"),
        };

        return header;
    }

    private static InnoDataEntry[] ReadDataEntries(
        InnoRecordReader reader,
        InnoSetupVersion version,
        int count)
    {
        var compact = version.HasCompactFileLocationRecord;
        var entries = new InnoDataEntry[count];

        for (var i = 0; i < count; i++)
        {
            reader.Skip(4); // première tranche
            reader.Skip(4); // dernière tranche
            var chunkOffset = reader.ReadUInt32();
            var fileOffset = reader.ReadUInt64();
            var fileSize = reader.ReadUInt64();
            reader.Skip(8); // taille du bloc compressé
            var sha256 = reader.Read(32).ToArray();
            reader.Skip(8); // horodatage
            reader.Skip(8); // version de fichier

            // Inno Setup 6.4.3 a retiré quatre drapeaux et l'octet de signature, et RENUMÉROTÉ les
            // survivants. Lire l'ancienne grille sur le nouveau format décalerait chaque
            // enregistrement de deux octets et attribuerait le filtre d'exécutable aux mauvais
            // fichiers.
            var flags = reader.ReadFlags(compact ? 5 : 9);
            if (!compact)
            {
                reader.Skip(1); // mode de signature, disparu en 6.4.3
            }

            var callOptimized = (flags & (compact ? 1UL << 2 : 1UL << 4)) != 0;
            var encrypted = (flags & (compact ? 1UL << 3 : 1UL << 6)) != 0;
            var compressed = (flags & (compact ? 1UL << 4 : 1UL << 7)) != 0;

            entries[i] = new InnoDataEntry(chunkOffset, fileOffset, fileSize, sha256, callOptimized, encrypted, compressed);
        }

        if (reader.Remaining != 0)
        {
            throw InnoFormatException.Corrupt(
                $"{reader.Remaining} octets inattendus à la fin du bloc des emplacements");
        }

        return entries;
    }

    /// <summary>
    /// Traverse <paramref name="count"/> enregistrements du même type.
    /// </summary>
    /// <remarks>
    /// Le format enchaîne une douzaine de tables de longueur variable, et chacune s'écrivait ici en
    /// boucle : douze boucles qui ne disent rien de plus que « et maintenant, celle-ci ». Les
    /// nommer une fois rend l'ORDRE des tables lisible d'un coup d'œil, et c'est cet ordre qui est
    /// la seule chose à ne pas se tromper.
    /// </remarks>
    private static void Repeat(InnoRecordReader reader, int count, Action<InnoRecordReader> read)
    {
        for (var i = 0; i < count; i++)
        {
            read(reader);
        }
    }

    private static void ReadMessage(InnoRecordReader reader)
    {
        reader.SkipString(); // nom
        reader.SkipString(); // valeur
        reader.Skip(4);      // langue
    }

    private static int ReadCount(InnoRecordReader reader)
    {
        var value = reader.ReadUInt32();

        // Un compte aberrant est le symptôme d'une lecture décalée, et boucler dessus reviendrait à
        // allouer des gigaoctets avant de s'en apercevoir.
        if (value > 4_000_000u)
        {
            throw InnoFormatException.Corrupt($"nombre d'entrées invraisemblable ({value})");
        }

        return (int)value;
    }

    private static void ReadCondition(InnoRecordReader reader)
    {
        // Composants, tâches, langues, expression de contrôle, puis les deux procédures encadrant
        // l'installation de l'entrée.
        for (var i = 0; i < 6; i++)
        {
            reader.SkipString();
        }
    }

    private static void ReadLanguage(InnoRecordReader reader)
    {
        for (var i = 0; i < 10; i++)
        {
            reader.SkipString();
        }

        reader.Skip(4);  // identifiant de langue
        reader.Skip(16); // quatre tailles de police
        reader.Skip(1);  // écriture de droite à gauche
    }

    private static void ReadType(InnoRecordReader reader)
    {
        for (var i = 0; i < 4; i++)
        {
            reader.SkipString();
        }

        reader.SkipWindowsVersionRange();
        reader.ReadFlags(1);
        reader.Skip(1); // type d'installation
        reader.Skip(8); // taille
    }

    private static void ReadComponent(InnoRecordReader reader)
    {
        for (var i = 0; i < 5; i++)
        {
            reader.SkipString();
        }

        reader.Skip(8); // espace disque supplémentaire
        reader.Skip(4); // niveau
        reader.Skip(1); // utilisé
        reader.SkipWindowsVersionRange();
        reader.ReadFlags(5);
        reader.Skip(8); // taille
    }

    private static void ReadTask(InnoRecordReader reader)
    {
        for (var i = 0; i < 6; i++)
        {
            reader.SkipString();
        }

        reader.Skip(4); // niveau
        reader.Skip(1); // utilisé
        reader.SkipWindowsVersionRange();
        reader.ReadFlags(5);
    }

    private static void ReadDirectory(InnoRecordReader reader)
    {
        reader.SkipString();
        ReadCondition(reader);
        reader.Skip(4); // attributs
        reader.SkipWindowsVersionRange();
        reader.Skip(2); // permission
        reader.ReadFlags(5);
    }

    private static InnoFileEntry ReadFile(InnoRecordReader reader)
    {
        reader.SkipString(); // source
        var destination = reader.ReadString();
        reader.SkipString(); // nom de police à installer
        reader.SkipString(); // nom fort d'assemblage
        ReadCondition(reader);
        reader.SkipWindowsVersionRange();

        var location = reader.ReadUInt32();

        reader.Skip(4); // attributs
        reader.Skip(8); // taille externe
        reader.Skip(2); // permission
        reader.ReadFlags(32);
        reader.Skip(1); // type d'entrée

        return new InnoFileEntry(destination, location);
    }

    private static void ReadIcon(InnoRecordReader reader)
    {
        for (var i = 0; i < 6; i++)
        {
            reader.SkipString();
        }

        ReadCondition(reader);
        reader.SkipString(); // identifiant de modèle utilisateur
        reader.Skip(16);     // CLSID d'activation des notifications
        reader.SkipWindowsVersionRange();
        reader.Skip(4); // index d'icône
        reader.Skip(4); // commande d'affichage
        reader.Skip(1); // fermeture à la sortie
        reader.Skip(2); // raccourci clavier
        reader.ReadFlags(6);
    }

    private static void ReadIni(InnoRecordReader reader)
    {
        for (var i = 0; i < 4; i++)
        {
            reader.SkipString();
        }

        ReadCondition(reader);
        reader.SkipWindowsVersionRange();
        reader.ReadFlags(5);
    }

    private static void ReadRegistry(InnoRecordReader reader)
    {
        reader.SkipString(); // clé
        reader.SkipString(); // nom de valeur
        reader.SkipString(); // valeur
        ReadCondition(reader);
        reader.SkipWindowsVersionRange();
        reader.Skip(4); // ruche
        reader.Skip(2); // permission
        reader.Skip(1); // type de valeur
        reader.ReadFlags(12);
    }

    private static void ReadDelete(InnoRecordReader reader)
    {
        reader.SkipString();
        ReadCondition(reader);
        reader.SkipWindowsVersionRange();
        reader.Skip(1); // cible
    }

    private static void ReadRun(InnoRecordReader reader)
    {
        for (var i = 0; i < 7; i++)
        {
            reader.SkipString();
        }

        ReadCondition(reader);
        reader.SkipWindowsVersionRange();
        reader.Skip(4); // commande d'affichage
        reader.Skip(1); // condition d'attente
        reader.ReadFlags(12);
    }

    private static void ReadWizardImages(InnoRecordReader reader)
    {
        var count = ReadCount(reader);
        for (var i = 0; i < count; i++)
        {
            reader.SkipString();
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count <= 0)
            {
                throw InnoFormatException.Corrupt("fin de fichier avant la chaîne d'identification");
            }

            read += count;
        }
    }

    private sealed class HeaderCounts
    {
        public InnoCompression Compression { get; set; }

        public int LanguageCount { get; init; }

        public int MessageCount { get; init; }

        public int PermissionCount { get; init; }

        public int TypeCount { get; init; }

        public int ComponentCount { get; init; }

        public int TaskCount { get; init; }

        public int DirectoryCount { get; init; }

        public int FileCount { get; init; }

        public int DataEntryCount { get; init; }

        public int IconCount { get; init; }

        public int IniEntryCount { get; init; }

        public int RegistryEntryCount { get; init; }

        public int DeleteEntryCount { get; init; }

        public int UninstallDeleteEntryCount { get; init; }

        public int RunEntryCount { get; init; }

        public int UninstallRunEntryCount { get; init; }
    }
}