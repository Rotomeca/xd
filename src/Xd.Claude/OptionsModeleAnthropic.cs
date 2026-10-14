namespace Xd.Claude;

/// <summary>
/// Réglages de l'appel à l'API Claude.
/// </summary>
public sealed record OptionsModeleAnthropic
{
    /// <summary>Identifiant du modèle. Haiku par défaut : la latence prime pour un usage vocal.</summary>
    public string Modele { get; init; } = "claude-haiku-5-5";

    /// <summary>Nombre maximal de jetons d'une réponse ; les réponses attendues tiennent en une ou deux phrases.</summary>
    public int MaxJetons { get; init; } = 1024;

    /// <summary>Prompt système envoyé à chaque appel.</summary>
    public string PromptSysteme { get; init; } = PromptsSysteme.Defaut;
}
