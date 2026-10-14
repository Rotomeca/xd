namespace Xd.Core.Outils;

/// <summary>
/// Contexte d'un appel d'outil, fourni par l'orchestrateur (jamais par le modèle).
/// </summary>
/// <param name="DemandeUtilisateur">
/// Phrase de l'utilisateur à l'origine de l'appel, telle qu'il l'a dite ou tapée. Avec <see cref="ReponsesUtilisateur"/>,
/// seule source acceptable d'une valeur libre comme un chemin (invariant 5), et seulement après confirmation.
/// </param>
public sealed record ContexteAppel(string DemandeUtilisateur)
{
    /// <summary>Réponses libres de l'utilisateur pendant cette demande (ex. « oui mais dans Xd » à une confirmation).</summary>
    public IReadOnlyList<string> ReponsesUtilisateur { get; init; } = [];

    /// <summary>Toutes les paroles de l'utilisateur pour cette demande, de la plus ancienne à la plus récente.</summary>
    public IReadOnlyList<string> Paroles => [DemandeUtilisateur, .. ReponsesUtilisateur];
}
