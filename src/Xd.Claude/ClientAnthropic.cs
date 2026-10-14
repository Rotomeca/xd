using Anthropic;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Claude;

/// <summary>
/// Construction du client de l'API Claude.
/// </summary>
public static class ClientAnthropic
{
    /// <summary>Variable d'environnement contenant la clé API, seule source de secret acceptée.</summary>
    public const string VariableCle = "ANTHROPIC_API_KEY";

    /// <summary>Crée un client authentifié uniquement par la clé de <see cref="VariableCle"/>.</summary>
    /// <returns>Le client, ou une erreur si la variable est absente.</returns>
    public static IResult<IAnthropicClient> Creer()
    {
        string? cle = Environment.GetEnvironmentVariable(VariableCle);
        if (string.IsNullOrWhiteSpace(cle))
            return Result.Fail<IAnthropicClient>(new Error($"La variable d'environnement {VariableCle} n'est pas définie.", ErrorType.Unauthorized));

        return Result.Ok<IAnthropicClient>(new AnthropicClient { ApiKey = cle });
    }
}
