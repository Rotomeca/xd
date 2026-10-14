using System.Text.Json;
using Xd.Core.Execution;

namespace Xd.Core.Boucle;

/// <summary>
/// Message de l'historique de conversation, indépendant du SDK utilisé pour parler à Claude.
/// </summary>
public abstract record Message;

/// <summary>Phrase prononcée par l'utilisateur.</summary>
/// <param name="Texte">Texte de la phrase.</param>
public sealed record MessageUtilisateur(string Texte) : Message;

/// <summary>Réponse de Claude : du texte et/ou des demandes d'outils.</summary>
/// <param name="Blocs">Blocs de la réponse, dans l'ordre.</param>
public sealed record MessageAssistant(IReadOnlyList<BlocAssistant> Blocs) : Message;

/// <summary>Résultats des outils demandés dans le message assistant précédent (blocs <c>tool_result</c>).</summary>
/// <param name="Resultats">Un résultat par demande d'outil, dans l'ordre des demandes.</param>
public sealed record MessageResultatsOutils(IReadOnlyList<ResultatOutil> Resultats) : Message;

/// <summary>Bloc d'une réponse de Claude.</summary>
public abstract record BlocAssistant;

/// <summary>Texte à dire à l'utilisateur.</summary>
/// <param name="Texte">Texte du bloc.</param>
public sealed record BlocTexte(string Texte) : BlocAssistant;

/// <summary>Demande d'outil (bloc <c>tool_use</c>).</summary>
/// <param name="IdAppel">Identifiant du bloc, à reprendre dans le <c>tool_result</c>.</param>
/// <param name="NomOutil">Nom de l'outil demandé.</param>
/// <param name="Parametres">Paramètres bruts.</param>
public sealed record BlocAppelOutil(string IdAppel, string NomOutil, JsonElement Parametres) : BlocAssistant;
