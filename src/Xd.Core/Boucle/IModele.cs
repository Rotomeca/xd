using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Core.Boucle;

/// <summary>
/// Accès au modèle de langage. N'exécute jamais d'outil : il ne fait que transmettre les demandes de Claude.
/// </summary>
public interface IModele
{
    /// <summary>Envoie l'historique et la liste des outils, et renvoie la réponse de Claude.</summary>
    /// <param name="historique">Messages de la conversation, du plus ancien au plus récent.</param>
    /// <param name="outils">Outils que Claude peut demander.</param>
    /// <param name="annulation">Annule l'appel, notamment lors d'une mise en veille.</param>
    /// <returns>La réponse, ou une erreur (réseau, API, annulation…).</returns>
    Task<IResult<ReponseModele>> EnvoyerAsync(IReadOnlyList<Message> historique, IReadOnlyList<DefinitionOutil> outils, CancellationToken annulation);
}
