using Xd.Core.Permissions;

namespace Xd.Cli;

/// <summary>
/// Pose la question de confirmation en console. Seul un « o » ou un « oui » explicite confirme.
/// </summary>
public sealed class ConfirmateurConsole : IConfirmateur
{
    /// <inheritdoc/>
    public async Task<bool> ConfirmerAsync(string reformulation, CancellationToken annulation)
    {
        Console.Write($"{reformulation} (o/N) ");
        string? reponse = await Console.In.ReadLineAsync(annulation);
        return reponse?.Trim().ToLowerInvariant() is "o" or "oui";
    }
}
