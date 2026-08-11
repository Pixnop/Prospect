namespace Prospect.Core.Instances;

/// <summary>
/// Levée quand le nom fourni à <see cref="InstanceService.CreateAsync"/> ou
/// <see cref="InstanceService.RenameAsync"/> est vide ou ne contient que des espaces. Le nom
/// affiché d'une instance ne peut pas être absent : c'est lui qui sert de base au slug de dossier
/// à la création (voir <see cref="InstanceSlugGenerator"/>).
/// </summary>
public sealed class InstanceNameInvalidException : Exception
{
    private const string DefaultMessage = "Le nom d'une instance ne peut pas être vide ou ne contenir que des espaces.";

    /// <summary>Constructeur standard, sans nom particulier.</summary>
    public InstanceNameInvalidException()
        : base(DefaultMessage)
    {
        AttemptedName = string.Empty;
    }

    /// <summary>Construit l'exception pour le nom refusé.</summary>
    public InstanceNameInvalidException(string attemptedName)
        : base(DefaultMessage)
    {
        AttemptedName = attemptedName;
    }

    /// <summary>Construit l'exception pour le nom refusé, avec une cause interne.</summary>
    public InstanceNameInvalidException(string attemptedName, Exception innerException)
        : base(DefaultMessage, innerException)
    {
        AttemptedName = attemptedName;
    }

    /// <summary>Nom refusé (vide, ou uniquement des espaces).</summary>
    public string AttemptedName { get; }
}