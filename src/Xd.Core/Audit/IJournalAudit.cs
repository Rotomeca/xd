using Rotomeca.Rop.Interfaces;

namespace Xd.Core.Audit;

/// <summary>
/// Journal d'audit des appels d'outils, en ajout seul.
/// </summary>
public interface IJournalAudit
{
    /// <summary>Ajoute une entrée au journal.</summary>
    /// <returns><see langword="true"/> si l'entrée est écrite, sinon une erreur.</returns>
    Task<IResult<bool>> EcrireAsync(EntreeAudit entree, CancellationToken annulation);
}
