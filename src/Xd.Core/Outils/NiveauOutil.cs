namespace Xd.Core.Outils;

/// <summary>
/// Niveau de permission d'un outil, vérifié par l'orchestrateur avant toute exécution.
/// </summary>
public enum NiveauOutil
{
    /// <summary>L'outil s'exécute sans confirmation de l'utilisateur.</summary>
    Libre,

    /// <summary>L'outil exige un oui explicite de l'utilisateur avant de s'exécuter.</summary>
    Confirme,
}
