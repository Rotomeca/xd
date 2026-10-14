using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.EtatMachine;

/// <summary>Aspect de la machine demandé à <c>etat_machine</c>.</summary>
public enum AspectMachine
{
    /// <summary>Charge du processeur.</summary>
    Cpu,

    /// <summary>Mémoire vive.</summary>
    Ram,

    /// <summary>Espace disque.</summary>
    Disque,

    /// <summary>Conteneurs en marche.</summary>
    Conteneurs,
}

/// <summary>Mémoire vive, en octets.</summary>
/// <param name="Totale">Mémoire physique totale.</param>
/// <param name="Disponible">Mémoire utilisable sans libérer de cache.</param>
public sealed record MemoireMachine(long Totale, long Disponible);

/// <summary>Espace d'un disque, en octets.</summary>
/// <param name="Nom">Lecteur (Windows) ou point de montage (Linux).</param>
/// <param name="Total">Taille totale.</param>
/// <param name="Libre">Espace libre pour l'utilisateur courant.</param>
public sealed record EspaceDisque(string Nom, long Total, long Libre);

/// <summary>Conteneur en marche.</summary>
/// <param name="Nom">Nom du conteneur.</param>
/// <param name="Image">Image d'origine.</param>
/// <param name="Statut">Statut lisible (ex. « Up 2 hours »).</param>
public sealed record Conteneur(string Nom, string Image, string Statut);

/// <summary>
/// Métriques de la machine, lues via les API de la plateforme (jamais via un shell). Une implémentation par OS.
/// </summary>
public interface IMesuresMachine
{
    /// <summary>Charge du processeur en pourcentage (0 à 100), mesurée sur un court intervalle.</summary>
    Task<IResult<double>> MesurerProcesseurAsync(CancellationToken annulation);

    /// <summary>Mémoire vive totale et disponible.</summary>
    IResult<MemoireMachine> MesurerMemoire();

    /// <summary>Disques fixes de la machine.</summary>
    IResult<IReadOnlyList<EspaceDisque>> MesurerDisques();
}

/// <summary>
/// Liste des conteneurs en marche, en lecture seule.
/// </summary>
public interface ISourceConteneurs
{
    /// <summary>Conteneurs en marche, ou une erreur si le moteur de conteneurs est inaccessible.</summary>
    Task<IResult<IReadOnlyList<Conteneur>>> ListerAsync(CancellationToken annulation);
}
