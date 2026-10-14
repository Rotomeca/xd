namespace Xd.Core.Permissions;

/// <summary>
/// Demande à l'utilisateur de confirmer une action.
/// </summary>
public interface IConfirmateur
{
    /// <summary>Pose la question et attend la réponse de l'utilisateur.</summary>
    /// <param name="reformulation">Action concrète à confirmer.</param>
    /// <param name="annulation">Annule l'attente ; une annulation est traitée comme un refus.</param>
    /// <returns><see langword="true"/> uniquement sur un oui explicite.</returns>
    Task<bool> ConfirmerAsync(string reformulation, CancellationToken annulation);
}
