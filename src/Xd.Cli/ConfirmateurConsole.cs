using Rotomeca.Core.Optionals;
using Xd.Core.Permissions;

namespace Xd.Cli;

/// <summary>
/// Pose la question de confirmation en console et rapporte la réponse telle quelle.
/// Son interprétation (oui, non, réponse libre) est faite par l'orchestrateur.
/// </summary>
public sealed class ConfirmateurConsole : IConfirmateur
{
    /// <inheritdoc/>
    public async Task<MayBe<string>> PoserQuestionAsync(string question, CancellationToken annulation)
    {
        Console.Write($"{question} ");
        string? reponse = await Console.In.ReadLineAsync(annulation);
        return reponse is null ? MayBe<string>.Null : reponse;
    }
}
