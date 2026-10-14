using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Execution;

namespace Xd.Core.Boucle;

/// <summary>
/// Boucle agentique écrite à la main : envoie l'historique à Claude, fait passer chaque demande d'outil
/// par <see cref="ExecuteurOutils"/>, renvoie les résultats et recommence jusqu'à une réponse texte.
/// Ne lève jamais d'exception : tout échec devient une phrase à dire à l'utilisateur.
/// </summary>
/// <remarks>
/// Chaque tour enregistré dans l'historique se termine par un texte de l'assistant, même en cas d'échec,
/// pour que la conversation reste valide (alternance des rôles, chaque <c>tool_use</c> suivi de son <c>tool_result</c>).
/// Non thread-safe : les demandes sont traitées l'une après l'autre.
/// </remarks>
public sealed class BoucleAgentique
{
    /// <summary>Nombre maximal d'allers-retours d'outils par demande utilisateur.</summary>
    public const int PlafondIterations = 10;

    internal const string MessagePlafond = "J'ai atteint la limite de dix actions pour cette demande, je m'arrête là.";
    internal const string MessageInterruption = "Demande interrompue.";
    internal const string MessageModeleIndisponible = "Je n'arrive pas à joindre le service pour le moment, réessaie dans un instant.";
    internal const string MessageRefus = "Je ne peux pas répondre à cette demande.";
    internal const string MessageTronque = "Ma réponse a été coupée, peux-tu reformuler ta demande ?";
    internal const string MessageVide = "Je n'ai pas de réponse à te donner.";

    private readonly IModele _modele;
    private readonly ExecuteurOutils _executeur;
    private readonly HistoriqueConversation _historique;

    public BoucleAgentique(IModele modele, ExecuteurOutils executeur, HistoriqueConversation historique)
    {
        _modele = modele;
        _executeur = executeur;
        _historique = historique;
    }

    /// <summary>Traite une phrase de l'utilisateur jusqu'à la réponse à lui dire.</summary>
    /// <param name="demande">Phrase de l'utilisateur.</param>
    /// <param name="annulation">Annule la demande, notamment lors d'une mise en veille.</param>
    /// <returns>Le texte à dire à l'utilisateur.</returns>
    public async Task<string> TraiterAsync(string demande, CancellationToken annulation)
    {
        var tour = new List<Message> { new MessageUtilisateur(demande) };
        string reponse = await PoursuivreAsync(demande, tour, 0, annulation);
        _historique.AjouterTour(tour);
        return reponse;
    }

    private async Task<string> PoursuivreAsync(string demande, List<Message> tour, int iteration, CancellationToken annulation)
    {
        if (annulation.IsCancellationRequested)
            return Conclure(tour, MessageInterruption);

        IResult<ReponseModele> resultat = await EnvoyerSansExceptionAsync([.. _historique.Messages, .. tour], annulation);
        return await resultat.Match(
            Ok: reponse => TraiterReponseAsync(demande, tour, iteration, reponse, annulation),
            Err: _ => Task.FromResult(Conclure(tour, annulation.IsCancellationRequested ? MessageInterruption : MessageModeleIndisponible)));
    }

    private async Task<string> TraiterReponseAsync(string demande, List<Message> tour, int iteration, ReponseModele reponse, CancellationToken annulation)
    {
        BlocAppelOutil[] appels = [.. reponse.Blocs.OfType<BlocAppelOutil>()];

        return reponse.Arret switch
        {
            RaisonArret.Refus => Conclure(tour, MessageRefus),
            RaisonArret.LimiteJetons => Conclure(tour, MessageTronque),
            RaisonArret.AppelOutils when appels.Length > 0 && iteration >= PlafondIterations => Conclure(tour, MessagePlafond),
            RaisonArret.AppelOutils when appels.Length > 0 => await ExecuterPuisPoursuivreAsync(demande, tour, iteration, reponse, appels, annulation),
            _ => ConclureAvecTexte(tour, reponse),
        };
    }

    private async Task<string> ExecuterPuisPoursuivreAsync(
        string demande, List<Message> tour, int iteration, ReponseModele reponse, BlocAppelOutil[] appels, CancellationToken annulation)
    {
        tour.Add(new MessageAssistant(reponse.Blocs));

        // Séquentiel : une confirmation attend l'utilisateur, les actions ne doivent pas se chevaucher.
        var resultats = new List<ResultatOutil>(appels.Length);
        foreach (BlocAppelOutil appel in appels)
            resultats.Add(await _executeur.ExecuterAsync(new AppelOutil(appel.IdAppel, appel.NomOutil, appel.Parametres, demande), annulation));

        tour.Add(new MessageResultatsOutils(resultats));
        return await PoursuivreAsync(demande, tour, iteration + 1, annulation);
    }

    private async Task<IResult<ReponseModele>> EnvoyerSansExceptionAsync(IReadOnlyList<Message> messages, CancellationToken annulation)
    {
        try
        {
            return await _modele.EnvoyerAsync(messages, _executeur.Definitions, annulation);
        }
        catch (Exception ex)
        {
            return Result.Fail<ReponseModele>(new Error(ex.Message, ErrorType.Unexpected, exception: ex));
        }
    }

    private static string ConclureAvecTexte(List<Message> tour, ReponseModele reponse)
    {
        string texte = string.Join(" ", reponse.Blocs.OfType<BlocTexte>().Select(b => b.Texte.Trim()).Where(t => t.Length > 0));
        return Conclure(tour, texte.Length > 0 ? texte : MessageVide);
    }

    /// <summary>Termine le tour par un texte de l'assistant, seul message enregistré après un échec.</summary>
    private static string Conclure(List<Message> tour, string texte)
    {
        tour.Add(new MessageAssistant([new BlocTexte(texte)]));
        return texte;
    }
}
