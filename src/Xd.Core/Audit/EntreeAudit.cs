using System.Text.Json;

namespace Xd.Core.Audit;

/// <summary>
/// Ligne du journal d'audit.
/// </summary>
/// <param name="Horodatage">Moment de l'événement.</param>
/// <param name="IdAppel">Identifiant de l'appel d'outil fourni par Claude, commun à toutes les étapes d'un même appel.</param>
/// <param name="Outil">Nom de l'outil demandé.</param>
/// <param name="Parametres">Paramètres bruts envoyés par Claude.</param>
/// <param name="DemandeUtilisateur">Phrase de l'utilisateur à l'origine de l'appel.</param>
/// <param name="Etape">Étape atteinte.</param>
/// <param name="Detail">Résultat, motif du rejet ou du refus ; <see langword="null"/> à l'étape de demande
/// (champ omis à la sérialisation, d'où <see langword="null"/> plutôt que <c>MayBe</c>).</param>
public sealed record EntreeAudit(
    DateTimeOffset Horodatage,
    string IdAppel,
    string Outil,
    JsonElement Parametres,
    string DemandeUtilisateur,
    EtapeAudit Etape,
    string? Detail);
