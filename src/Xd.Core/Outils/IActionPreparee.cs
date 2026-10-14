using Rotomeca.Rop.Interfaces;

namespace Xd.Core.Outils;

/// <summary>
/// Action dont les paramètres ont été validés, en attente d'exécution.
/// </summary>
public interface IActionPreparee
{
    /// <summary>
    /// Reformulation concrète de l'action, construite à partir des paramètres validés
    /// (jamais du texte libre de Claude). Sert de question de confirmation pour un outil <see cref="NiveauOutil.Confirme"/>.
    /// </summary>
    string Reformulation { get; }

    /// <summary>Exécute l'action.</summary>
    /// <param name="annulation">Annule l'action, notamment lors d'une mise en veille.</param>
    /// <returns>Le texte renvoyé à Claude, ou une erreur.</returns>
    Task<IResult<string>> ExecuterAsync(CancellationToken annulation);
}
