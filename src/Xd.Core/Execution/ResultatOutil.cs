using Rotomeca.Core.Optionals;

namespace Xd.Core.Execution;

/// <summary>
/// Résultat renvoyé au modèle (bloc <c>tool_result</c>). Un refus ou une erreur est un résultat comme un autre.
/// </summary>
/// <param name="IdAppel">Identifiant du bloc <c>tool_use</c> d'origine.</param>
/// <param name="Contenu">Texte renvoyé au modèle.</param>
/// <param name="EstErreur">Valeur de <c>is_error</c>.</param>
public sealed record ResultatOutil(string IdAppel, string Contenu, bool EstErreur)
{
    /// <summary>
    /// Réponse libre de l'utilisateur à la question de confirmation (ni oui ni non francs). Elle compte ensuite
    /// parmi ses paroles pour les appels suivants de la même demande.
    /// </summary>
    public MayBe<string> ReponseUtilisateur { get; init; }
}
