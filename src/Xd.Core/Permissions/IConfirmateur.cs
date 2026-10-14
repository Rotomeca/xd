using Rotomeca.Core.Optionals;

namespace Xd.Core.Permissions;

/// <summary>
/// Canal de confirmation avec l'utilisateur (console, puis voix). Ne fait que poser la question et rapporter la réponse :
/// c'est <see cref="Execution.ExecuteurOutils"/> qui l'interprète (<see cref="InterpretationConfirmation"/>), jamais le modèle.
/// </summary>
public interface IConfirmateur
{
    /// <summary>Pose la question et attend la réponse de l'utilisateur.</summary>
    /// <param name="question">Question de confirmation à poser telle quelle.</param>
    /// <param name="annulation">Annule l'attente ; une annulation est traitée comme un refus.</param>
    /// <returns>La réponse brute de l'utilisateur, ou rien s'il n'y en a pas (fin de l'entrée).</returns>
    Task<MayBe<string>> PoserQuestionAsync(string question, CancellationToken annulation);
}
