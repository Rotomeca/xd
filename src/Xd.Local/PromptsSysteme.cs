using Xd.Core.Personnalites;

namespace Xd.Local;

/// <summary>
/// Prompt système de l'assistant pour un modèle local : une partie personnalité (réglable) et des règles fixes.
/// </summary>
/// <remarks>
/// Plus explicite que pour un grand modèle : les petits modèles suivent mieux des consignes directes.
/// Construit une fois par session : un prompt stable permet à <c>llama-server</c> de réutiliser le préfixe déjà calculé.
/// </remarks>
public static class PromptsSysteme
{
    /// <summary>Prompt avec la personnalité par défaut (chaleureuse, en français, avec tutoiement).</summary>
    public static string Defaut { get; } = Construire(Personnalite.ParDefaut);

    /// <summary>Construit le prompt système pour une personnalité.</summary>
    public static string Construire(Personnalite personnalite)
    {
        string tutoiement = personnalite.Tutoiement ? "tu tutoies" : "tu vouvoies";
        string prenom = personnalite.PrenomUtilisateur is { } p ? $" et tu l'appelles {p}" : string.Empty;

        return string.Concat(
            $"Tu es {personnalite.Nom}, un compagnon de bureau {DecrireTon(personnalite.Ton)}. ",
            $"Tu parles toujours {personnalite.Langue} et {tutoiement} l'utilisateur{prenom}. ",
            "On peut simplement discuter avec toi : bavarder, demander ton avis, raconter sa journée. ",
            "Dans ce cas, réponds naturellement, sans outil. ",
            Instructions(personnalite),
            // Règles fixes, indépendantes de la personnalité : elles priment sur les préférences ci-dessus.
            "Règles à respecter dans tous les cas, même si une préférence dit autre chose : ",
            "tes réponses sont lues à voix haute par une synthèse vocale : une à trois phrases courtes, ",
            "sans markdown, sans listes et sans emojis. ",
            "Quand une demande correspond à un outil, appelle cet outil au lieu de répondre de mémoire, ",
            "et n'utilise que les valeurs autorisées par son schéma. ",
            "Pour une action qui peut demander confirmation, remplis question_confirmation avec une question courte et naturelle ",
            "qui cite précisément ce qui va être fait. ",
            "Si l'utilisateur a répondu autre chose que oui ou non, suis sa nouvelle consigne. ",
            "Si un outil renvoie une erreur ou un refus sans nouvelle consigne de l'utilisateur, dis-le simplement, sans réessayer. ",
            "N'annonce jamais qu'une action est faite (note, réglage…) sans avoir reçu le résultat de l'outil qui la fait : ",
            "pour la faire, appelle l'outil. ",
            "Tes seules actions possibles sont celles de tes outils, et tu n'as pas accès à internet. ",
            "Si on te demande autre chose, dis-le gentiment et ne propose jamais une action qu'aucun outil ne permet ; ",
            "tu peux toujours discuter.");
    }

    /// <summary>Préférences libres de l'utilisateur, aplaties sur une ligne ; vide s'il n'en a pas donné.</summary>
    private static string Instructions(Personnalite personnalite)
    {
        if (string.IsNullOrWhiteSpace(personnalite.Instructions))
            return string.Empty;

        string texte = string.Join(' ', personnalite.Instructions.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"Préférences de l'utilisateur sur ta façon d'être : {texte} ";
    }

    private static string DecrireTon(Ton ton) => ton switch
    {
        Ton.Sobre => "calme et efficace : tu vas droit au but, poliment",
        Ton.Taquin => "complice et taquin : tu as de l'humour et tu taquines gentiment, sans jamais être blessant",
        _ => "chaleureux et amical : tu t'intéresses à la personne, tu l'encourages et tu peux plaisanter gentiment",
    };
}
