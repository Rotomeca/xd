using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Boucle;
using Xd.Core.Execution;
using Xd.Core.Outils;
using static Xd.Core.Tests.ModeleFactice;

namespace Xd.Core.Tests.Boucle;

public sealed class BoucleAgentiqueTests
{
    private const string Demande = "lance le mode jeu";

    private readonly JournalMemoire _journal = new();
    private readonly HistoriqueConversation _historique = new(toursMax: 5);

    private BoucleAgentique Boucle(IModele modele, ConfirmateurFactice confirmateur, params IOutil[] outils) =>
        new(modele, new ExecuteurOutils(outils, confirmateur, _journal, TimeProvider.System), _historique);

    private static OutilFactice OutilLibre(ActionFactice action) => new("etat_machine", NiveauOutil.Libre, action);

    private static OutilFactice OutilConfirme(ActionFactice action) => new("lancer_mode", NiveauOutil.Confirme, action);

    [Fact]
    public async Task ReponseTexte_EstRenvoyeeSansExecuterDOutil()
    {
        var modele = Script(Texte("Bonjour !"));
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");

        string reponse = await Boucle(modele, new ConfirmateurFactice(true), OutilLibre(action)).TraiterAsync("salut", CancellationToken.None);

        Assert.Equal("Bonjour !", reponse);
        Assert.Equal(0, action.NombreExecutions);
        Assert.Equal([new MessageUtilisateur("salut")], modele.Envois[0]);
    }

    [Fact]
    public async Task AppelOutil_PasseParLExecuteurPuisRenvoieLeResultatAClaude()
    {
        var modele = Script(Appels(Appel("a1", "etat_machine")), Texte("La RAM est à 40 %."));
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");

        string reponse = await Boucle(modele, new ConfirmateurFactice(true), OutilLibre(action)).TraiterAsync("et la RAM ?", CancellationToken.None);

        Assert.Equal("La RAM est à 40 %.", reponse);
        Assert.Equal(1, action.NombreExecutions);
        MessageResultatsOutils resultats = Assert.IsType<MessageResultatsOutils>(modele.Envois[1][^1]);
        Assert.Equal([new ResultatOutil("a1", "RAM : 40 %", false)], resultats.Resultats);
        Assert.All(_journal.Entrees, e => Assert.Equal("et la RAM ?", e.DemandeUtilisateur));
    }

    [Fact]
    public async Task PlusieursAppelsDansUneReponse_TousLesResultatsDansUnSeulMessage()
    {
        var modele = Script(Appels(Appel("a1", "etat_machine"), Appel("a2", "etat_machine")), Texte("Fait."));
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");

        await Boucle(modele, new ConfirmateurFactice(true), OutilLibre(action)).TraiterAsync("état ?", CancellationToken.None);

        Assert.Equal(2, action.NombreExecutions);
        MessageResultatsOutils resultats = Assert.IsType<MessageResultatsOutils>(modele.Envois[1][^1]);
        Assert.Equal(["a1", "a2"], resultats.Resultats.Select(r => r.IdAppel));
    }

    [Fact]
    public async Task ConfirmationRefusee_RenvoieUneErreurAClaudeEtLaBouclePoursuit()
    {
        var modele = Script(Appels(Appel("a1", "lancer_mode")), Texte("D'accord, je ne lance rien."));
        var action = new ActionFactice("Je lance le mode jeu.", "Mode jeu lancé.");

        string reponse = await Boucle(modele, new ConfirmateurFactice(false), OutilConfirme(action)).TraiterAsync(Demande, CancellationToken.None);

        Assert.Equal("D'accord, je ne lance rien.", reponse);
        Assert.Equal(0, action.NombreExecutions);
        MessageResultatsOutils resultats = Assert.IsType<MessageResultatsOutils>(modele.Envois[1][^1]);
        Assert.True(Assert.Single(resultats.Resultats).EstErreur);
    }

    [Fact]
    public async Task ParametresInvalides_RenvoieUneErreurAClaudeSansExecuter()
    {
        var modele = Script(Appels(Appel("a1", "lancer_mode")), Texte("Ce mode n'existe pas."));
        var outil = new OutilFactice("lancer_mode", NiveauOutil.Confirme,
            _ => Result.Fail<IActionPreparee>(new Error("mode inconnu : « boulot2 »", ErrorType.Validation)));
        var confirmateur = new ConfirmateurFactice(true);

        await Boucle(modele, confirmateur, outil).TraiterAsync(Demande, CancellationToken.None);

        Assert.Empty(confirmateur.Questions);
        MessageResultatsOutils resultats = Assert.IsType<MessageResultatsOutils>(modele.Envois[1][^1]);
        Assert.True(Assert.Single(resultats.Resultats).EstErreur);
    }

    [Fact]
    public async Task PlafondDIterationsAtteint_ArreteAvecUnMessageSansExecuterLaDemandeSuivante()
    {
        var modele = new ModeleFactice((numero, _) => Task.FromResult(Result.Ok(Appels(Appel($"a{numero}", "etat_machine")))));
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");

        string reponse = await Boucle(modele, new ConfirmateurFactice(true), OutilLibre(action)).TraiterAsync("boucle", CancellationToken.None);

        Assert.Equal(BoucleAgentique.MessagePlafond, reponse);
        Assert.Equal(BoucleAgentique.PlafondIterations, action.NombreExecutions);
        Assert.Equal(BoucleAgentique.PlafondIterations + 1, modele.Envois.Count);
        AssertTourValide(_historique.Messages);
    }

    [Fact]
    public async Task MiseEnVeillePendantUneConfirmation_RefusJournaliseEtDemandeInterrompue()
    {
        var modele = Script(Appels(Appel("a1", "lancer_mode")), Texte("ne doit pas être demandé"));
        var action = new ActionFactice("Je lance le mode jeu.", "Mode jeu lancé.");
        using var veille = new CancellationTokenSource();

        Task<string> enCours = Boucle(modele, ConfirmateurFactice.EnAttente(), OutilConfirme(action)).TraiterAsync(Demande, veille.Token);
        await veille.CancelAsync();
        string reponse = await enCours;

        Assert.Equal(BoucleAgentique.MessageInterruption, reponse);
        Assert.Equal(0, action.NombreExecutions);
        Assert.Single(modele.Envois);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Refuse], _journal.Etapes);
        AssertTourValide(_historique.Messages);
    }

    [Fact]
    public async Task ErreurDuModele_RenvoieUnMessageSansFaireTomberLaBoucle()
    {
        var modele = new ModeleFactice((_, _) => Task.FromResult(Result.Fail<ReponseModele>(new Error("HTTP 529", ErrorType.Unexpected))));

        string reponse = await Boucle(modele, new ConfirmateurFactice(true)).TraiterAsync("salut", CancellationToken.None);

        Assert.Equal(BoucleAgentique.MessageModeleIndisponible, reponse);
        AssertTourValide(_historique.Messages);
    }

    [Fact]
    public async Task ExceptionDuModele_EstConvertieEnMessage()
    {
        var modele = new ModeleFactice((_, _) => throw new HttpRequestException("réseau coupé"));

        string reponse = await Boucle(modele, new ConfirmateurFactice(true)).TraiterAsync("salut", CancellationToken.None);

        Assert.Equal(BoucleAgentique.MessageModeleIndisponible, reponse);
    }

    [Fact]
    public async Task RefusDuModele_RenvoieUnMessageDeRefus()
    {
        var modele = Script(new ReponseModele([], RaisonArret.Refus));

        string reponse = await Boucle(modele, new ConfirmateurFactice(true)).TraiterAsync("salut", CancellationToken.None);

        Assert.Equal(BoucleAgentique.MessageRefus, reponse);
        AssertTourValide(_historique.Messages);
    }

    [Fact]
    public async Task ReponseTronquee_NExecutePasSesDemandesDOutils()
    {
        var modele = Script(new ReponseModele([Appel("a1", "etat_machine")], RaisonArret.LimiteJetons));
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");

        string reponse = await Boucle(modele, new ConfirmateurFactice(true), OutilLibre(action)).TraiterAsync("état ?", CancellationToken.None);

        Assert.Equal(BoucleAgentique.MessageTronque, reponse);
        Assert.Equal(0, action.NombreExecutions);
        Assert.Empty(_journal.Entrees);
        AssertTourValide(_historique.Messages);
    }

    [Fact]
    public async Task DemandeSuivante_EnvoieLHistoriqueDesToursPrecedents()
    {
        var modele = Script(Appels(Appel("a1", "etat_machine")), Texte("RAM à 40 %."), Texte("De rien."));
        var boucle = Boucle(modele, new ConfirmateurFactice(true), OutilLibre(new ActionFactice("Lire la RAM", "RAM : 40 %")));

        await boucle.TraiterAsync("et la RAM ?", CancellationToken.None);
        await boucle.TraiterAsync("merci", CancellationToken.None);

        IReadOnlyList<Message> envoi = modele.Envois[2];
        Assert.Equal(5, envoi.Count);
        Assert.Equal(new MessageUtilisateur("et la RAM ?"), envoi[0]);
        Assert.Equal(new MessageUtilisateur("merci"), envoi[^1]);
    }

    /// <summary>Un historique valide alterne les rôles, termine sur un texte et apparie chaque tool_use.</summary>
    private static void AssertTourValide(IReadOnlyList<Message> messages)
    {
        Assert.IsType<MessageUtilisateur>(messages[0]);
        MessageAssistant dernier = Assert.IsType<MessageAssistant>(messages[^1]);
        Assert.All(dernier.Blocs, b => Assert.IsType<BlocTexte>(b));

        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i] is not MessageAssistant { Blocs: var blocs })
                continue;

            string[] appels = [.. blocs.OfType<BlocAppelOutil>().Select(a => a.IdAppel)];
            if (appels.Length == 0)
                continue;

            MessageResultatsOutils resultats = Assert.IsType<MessageResultatsOutils>(messages[i + 1]);
            Assert.Equal(appels, resultats.Resultats.Select(r => r.IdAppel));
        }
    }
}
