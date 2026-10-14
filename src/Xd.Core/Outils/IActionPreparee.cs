using Rotomeca.Rop.Interfaces;

namespace Xd.Core.Outils;

/// <summary>
/// Action dont les paramètres ont été validés, en attente d'exécution.
/// </summary>
public interface IActionPreparee
{
    /// <summary>
    /// Reformulation concrète de l'action, construite à partir des paramètres validés
    /// (jamais du texte libre du modèle). Sert de question de confirmation quand elle est demandée.
    /// </summary>
    string Reformulation { get; }

    /// <summary>
    /// Exige une confirmation même pour un outil <see cref="NiveauOutil.Libre"/> (ex. un choix que l'utilisateur n'a pas formulé).
    /// Une action peut seulement ajouter une confirmation, jamais en retirer une : un outil <see cref="NiveauOutil.Confirme"/>
    /// est toujours confirmé, et c'est <see cref="Execution.ExecuteurOutils"/> qui pose la question.
    /// </summary>
    bool ConfirmationRequise => false;

    /// <summary>
    /// Éléments qu'une question de confirmation rédigée par le modèle doit citer pour être affichée (ex. destination, chemin exact).
    /// Vide : seule <see cref="Reformulation"/> peut servir de question.
    /// </summary>
    IReadOnlyList<string> ElementsACiter => [];

    /// <summary>Message renvoyé au modèle si l'utilisateur refuse : peut lui dire quoi demander ensuite.</summary>
    string MessageSiRefus => "L'utilisateur a refusé l'action.";

    /// <summary>Exécute l'action.</summary>
    /// <param name="annulation">Annule l'action, notamment lors d'une mise en veille.</param>
    /// <returns>Le texte renvoyé au modèle, ou une erreur.</returns>
    Task<IResult<string>> ExecuterAsync(CancellationToken annulation);
}
