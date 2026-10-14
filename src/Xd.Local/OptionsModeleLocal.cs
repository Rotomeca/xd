namespace Xd.Local;

/// <summary>
/// Réglages de l'appel au serveur LLM local (<c>llama-server</c>).
/// </summary>
public sealed record OptionsModeleLocal
{
    /// <summary>Adresse de base de <c>llama-server</c>, sur la boucle locale.</summary>
    public Uri Adresse { get; init; } = new("http://127.0.0.1:8080/");

    /// <summary>Nombre maximal de jetons d'une réponse ; les réponses attendues tiennent en une ou deux phrases.</summary>
    public int MaxJetons { get; init; } = 512;

    /// <summary>Prompt système envoyé à chaque appel.</summary>
    public string PromptSysteme { get; init; } = PromptsSysteme.Defaut;
}
