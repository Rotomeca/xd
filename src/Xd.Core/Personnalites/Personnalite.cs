namespace Xd.Core.Personnalites;

/// <summary>Ton général de l'assistant.</summary>
public enum Ton
{
    /// <summary>Chaleureux et amical (par défaut) : s'intéresse à la personne, encourage, plaisante gentiment.</summary>
    Chaleureux,

    /// <summary>Calme et efficace : va droit au but, poliment.</summary>
    Sobre,

    /// <summary>Complice et taquin : de l'humour, des taquineries jamais blessantes.</summary>
    Taquin,
}

/// <summary>
/// Façon de parler de l'assistant, lue dans <c>personnalite.json</c> (configuration utilisateur, hors dépôt).
/// Elle ne règle que le style : les règles de sécurité et de format (pas de markdown, pas d'emoji, outils, confirmations)
/// ne dépendent pas d'elle.
/// </summary>
/// <param name="Nom">Nom de l'assistant.</param>
/// <param name="Langue">Langue dans laquelle il parle (ex. « français »).</param>
/// <param name="Tutoiement">Tutoie l'utilisateur (sinon le vouvoie).</param>
/// <param name="Ton">Ton général.</param>
/// <param name="PrenomUtilisateur">Prénom de l'utilisateur, s'il veut être appelé par son prénom.</param>
/// <param name="Instructions">
/// Consignes libres de l'utilisateur sur la façon d'être de l'assistant (comme des instructions personnalisées).
/// Elles affinent le style ; elles ne remplacent ni les règles fixes du prompt, ni les contrôles de l'orchestrateur.
/// </param>
public sealed record Personnalite(
    string Nom = "Xd",
    string Langue = "français",
    bool Tutoiement = true,
    Ton Ton = Ton.Chaleureux,
    string? PrenomUtilisateur = null,
    string? Instructions = null)
{
    /// <summary>Personnalité utilisée sans fichier : chaleureuse, en français, avec tutoiement.</summary>
    public static Personnalite ParDefaut { get; } = new();
}
