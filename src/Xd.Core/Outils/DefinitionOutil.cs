using System.Text.Json;

namespace Xd.Core.Outils;

/// <summary>
/// Ce que Claude voit d'un outil : son nom, sa description et le schéma de ses paramètres.
/// Ne donne aucun accès à l'exécution.
/// </summary>
/// <param name="Nom">Nom exposé à Claude.</param>
/// <param name="Description">Description exposée à Claude.</param>
/// <param name="SchemaParametres">Schéma JSON des paramètres.</param>
public sealed record DefinitionOutil(string Nom, string Description, JsonElement SchemaParametres);
