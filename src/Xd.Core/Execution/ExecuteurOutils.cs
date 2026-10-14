using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Outils;
using Xd.Core.Permissions;

namespace Xd.Core.Execution;

/// <summary>
/// Seul point de passage entre une demande de Claude et l'exécution d'un outil :
/// validation → confirmation si <see cref="NiveauOutil.Confirme"/> → journal d'audit → exécution.
/// Ne lève jamais d'exception : tout échec devient un <see cref="ResultatOutil"/> en erreur.
/// </summary>
/// <remarks>
/// Le journal est écrit avec <see cref="CancellationToken.None"/> pour qu'un refus dû à une annulation
/// (mise en veille) soit tout de même journalisé. Si une entrée ne peut pas être écrite avant l'exécution,
/// l'action n'est pas exécutée.
/// </remarks>
public sealed class ExecuteurOutils
{
    private readonly IReadOnlyDictionary<string, IOutil> _outils;
    private readonly IConfirmateur _confirmateur;
    private readonly IJournalAudit _journal;
    private readonly TimeProvider _horloge;

    public ExecuteurOutils(IEnumerable<IOutil> outils, IConfirmateur confirmateur, IJournalAudit journal, TimeProvider horloge)
    {
        var parNom = new Dictionary<string, IOutil>(StringComparer.Ordinal);
        var definitions = new List<DefinitionOutil>();
        foreach (IOutil outil in outils)
        {
            if (!parNom.TryAdd(outil.Nom, outil))
                throw new ArgumentException($"Deux outils portent le nom « {outil.Nom} ».", nameof(outils));

            definitions.Add(new DefinitionOutil(outil.Nom, outil.Description, outil.SchemaParametres));
        }

        _outils = parNom;
        Definitions = definitions;
        _confirmateur = confirmateur;
        _journal = journal;
        _horloge = horloge;
    }

    /// <summary>Outils exposés à Claude : exactement ceux que cet exécuteur sait traiter.</summary>
    public IReadOnlyList<DefinitionOutil> Definitions { get; }

    /// <summary>Traite une demande d'outil de bout en bout.</summary>
    /// <param name="appel">Demande émise par Claude.</param>
    /// <param name="annulation">Annule la confirmation en attente ou l'action en cours.</param>
    public async Task<ResultatOutil> ExecuterAsync(AppelOutil appel, CancellationToken annulation)
    {
        if (!await JournaliserAsync(appel, EtapeAudit.Demande, null))
            return Erreur(appel, "Journal d'audit indisponible, action non exécutée.");

        if (!_outils.TryGetValue(appel.NomOutil, out IOutil? outil))
            return await TerminerEnErreurAsync(appel, EtapeAudit.Rejete, $"Outil inconnu : {appel.NomOutil}.");

        return await Preparer(outil, appel).Match(
            Ok: action => ConfirmerPuisExecuterAsync(outil, action, appel, annulation),
            Err: erreur => TerminerEnErreurAsync(appel, EtapeAudit.Rejete, $"Paramètres invalides : {erreur.Message}"));
    }

    private static IResult<IActionPreparee> Preparer(IOutil outil, AppelOutil appel)
    {
        try
        {
            return outil.Preparer(appel.Parametres);
        }
        catch (Exception ex)
        {
            return Result.Fail<IActionPreparee>(new Error(ex.Message, ErrorType.Validation, exception: ex));
        }
    }

    private async Task<ResultatOutil> ConfirmerPuisExecuterAsync(IOutil outil, IActionPreparee action, AppelOutil appel, CancellationToken annulation)
    {
        if (outil.Niveau == NiveauOutil.Confirme)
        {
            (bool confirme, string motif) = await DemanderConfirmationAsync(action, annulation);
            if (!confirme)
                return await TerminerEnErreurAsync(appel, EtapeAudit.Refuse, motif);

            if (!await JournaliserAsync(appel, EtapeAudit.Confirme, action.Reformulation))
                return Erreur(appel, "Journal d'audit indisponible, action non exécutée.");
        }

        (EtapeAudit etape, string contenu, bool estErreur) = await ExecuterSansExceptionAsync(action, annulation);
        await JournaliserAsync(appel, etape, contenu);
        return new ResultatOutil(appel.IdAppel, contenu, estErreur);
    }

    private async Task<(bool Confirme, string Motif)> DemanderConfirmationAsync(IActionPreparee action, CancellationToken annulation)
    {
        try
        {
            return await _confirmateur.ConfirmerAsync(action.Reformulation, annulation)
                ? (true, string.Empty)
                : (false, "L'utilisateur a refusé l'action.");
        }
        catch (OperationCanceledException)
        {
            return (false, "Confirmation annulée, action non exécutée.");
        }
        catch (Exception ex)
        {
            return (false, $"Confirmation impossible ({ex.Message}), action non exécutée.");
        }
    }

    private static async Task<(EtapeAudit Etape, string Contenu, bool EstErreur)> ExecuterSansExceptionAsync(IActionPreparee action, CancellationToken annulation)
    {
        try
        {
            IResult<string> resultat = await action.ExecuterAsync(annulation);
            return resultat.Match(
                Ok: contenu => (EtapeAudit.Execute, contenu, false),
                Err: erreur => (EtapeAudit.Echec, erreur.Message, true));
        }
        catch (OperationCanceledException)
        {
            return (EtapeAudit.Echec, "Action annulée.", true);
        }
        catch (Exception ex)
        {
            return (EtapeAudit.Echec, $"Erreur pendant l'exécution : {ex.Message}", true);
        }
    }

    private async Task<ResultatOutil> TerminerEnErreurAsync(AppelOutil appel, EtapeAudit etape, string motif)
    {
        await JournaliserAsync(appel, etape, motif);
        return Erreur(appel, motif);
    }

    private async Task<bool> JournaliserAsync(AppelOutil appel, EtapeAudit etape, string? detail)
    {
        var entree = new EntreeAudit(_horloge.GetUtcNow(), appel.IdAppel, appel.NomOutil, appel.Parametres, appel.DemandeUtilisateur, etape, detail);
        try
        {
            IResult<bool> ecriture = await _journal.EcrireAsync(entree, CancellationToken.None);
            return ecriture.IsSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ResultatOutil Erreur(AppelOutil appel, string message) => new(appel.IdAppel, message, true);
}
