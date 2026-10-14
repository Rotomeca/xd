namespace Xd.Core.Execution;

/// <summary>
/// Résultat renvoyé à Claude (bloc <c>tool_result</c>). Un refus ou une erreur est un résultat comme un autre.
/// </summary>
/// <param name="IdAppel">Identifiant du bloc <c>tool_use</c> d'origine.</param>
/// <param name="Contenu">Texte renvoyé à Claude.</param>
/// <param name="EstErreur">Valeur de <c>is_error</c>.</param>
public sealed record ResultatOutil(string IdAppel, string Contenu, bool EstErreur);
