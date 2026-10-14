using System.Text.Json;

namespace Xd.Core.Execution;

/// <summary>
/// Demande d'outil émise par Claude (bloc <c>tool_use</c>).
/// </summary>
/// <param name="IdAppel">Identifiant du bloc <c>tool_use</c>, à reprendre dans le <c>tool_result</c>.</param>
/// <param name="NomOutil">Nom de l'outil demandé.</param>
/// <param name="Parametres">Paramètres bruts.</param>
/// <param name="DemandeUtilisateur">Phrase de l'utilisateur à l'origine de l'appel, pour le journal d'audit.</param>
public sealed record AppelOutil(string IdAppel, string NomOutil, JsonElement Parametres, string DemandeUtilisateur);
