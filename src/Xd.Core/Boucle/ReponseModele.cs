namespace Xd.Core.Boucle;

/// <summary>
/// Raison pour laquelle Claude a terminé sa réponse.
/// </summary>
public enum RaisonArret
{
    /// <summary>Réponse terminée normalement.</summary>
    Fin,

    /// <summary>Claude attend le résultat des outils demandés.</summary>
    AppelOutils,

    /// <summary>Réponse tronquée par la limite de jetons : ses demandes d'outils ne sont pas fiables.</summary>
    LimiteJetons,

    /// <summary>Claude a refusé de répondre.</summary>
    Refus,

    /// <summary>Toute autre raison, non gérée par la boucle.</summary>
    Autre,
}

/// <summary>
/// Réponse de Claude à un envoi de l'historique.
/// </summary>
/// <param name="Blocs">Blocs de la réponse, dans l'ordre.</param>
/// <param name="Arret">Raison de fin de la réponse.</param>
public sealed record ReponseModele(IReadOnlyList<BlocAssistant> Blocs, RaisonArret Arret);
