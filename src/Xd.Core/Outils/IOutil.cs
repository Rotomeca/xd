using System.Text.Json;
using Rotomeca.Rop.Interfaces;

namespace Xd.Core.Outils;

/// <summary>
/// Outil que le modèle peut demander. L'outil ne vérifie aucune permission :
/// c'est le rôle de <see cref="Execution.ExecuteurOutils"/>.
/// </summary>
public interface IOutil
{
    /// <summary>Nom exposé au modèle, unique parmi les outils.</summary>
    string Nom { get; }

    /// <summary>Description exposée au modèle.</summary>
    string Description { get; }

    /// <summary>Schéma JSON des paramètres, exposé au modèle. Peut changer avec la configuration (ex. liste de projets) : relu à chaque envoi.</summary>
    JsonElement SchemaParametres { get; }

    /// <summary>Niveau de permission de l'outil.</summary>
    NiveauOutil Niveau { get; }

    /// <summary>
    /// Valide les paramètres envoyés par le modèle et prépare l'action, sans rien exécuter.
    /// Toute valeur hors énumération ou invalide doit produire une erreur, jamais un comportement par défaut.
    /// </summary>
    /// <param name="parametres">Paramètres bruts envoyés par le modèle.</param>
    /// <param name="contexte">Contexte fourni par l'orchestrateur, dont la phrase d'origine de l'utilisateur.</param>
    /// <returns>L'action prête à être confirmée puis exécutée, ou une erreur de validation.</returns>
    IResult<IActionPreparee> Preparer(JsonElement parametres, ContexteAppel contexte);
}
