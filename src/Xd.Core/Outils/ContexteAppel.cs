namespace Xd.Core.Outils;

/// <summary>
/// Contexte d'un appel d'outil, fourni par l'orchestrateur (jamais par le modèle).
/// </summary>
/// <param name="DemandeUtilisateur">
/// Phrase de l'utilisateur à l'origine de l'appel, telle qu'il l'a dite ou tapée. Seule source acceptable
/// d'une valeur libre comme un chemin (invariant 5), et seulement après confirmation.
/// </param>
public sealed record ContexteAppel(string DemandeUtilisateur);
