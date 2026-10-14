using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Execution;
using Xd.Core.Outils;

namespace Xd.Core.Tests.Execution;

public sealed class ExecuteurOutilsTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 14, 9, 30, 0, TimeSpan.Zero);

    private readonly JournalMemoire _journal = new();

    private ExecuteurOutils Executeur(ConfirmateurFactice confirmateur, params IOutil[] outils) =>
        new(outils, confirmateur, _journal, new HorlogeFixe(Maintenant));

    private static AppelOutil Appel(string nomOutil, string parametres = "{}") =>
        new("appel-1", nomOutil, JsonDocument.Parse(parametres).RootElement, "lance le mode jeu");

    [Fact]
    public async Task OutilLibre_SExecuteSansConfirmation()
    {
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");
        var confirmateur = new ConfirmateurFactice(false);
        var executeur = Executeur(confirmateur, new OutilFactice("etat_machine", NiveauOutil.Libre, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("etat_machine"), CancellationToken.None);

        Assert.Equal(new ResultatOutil("appel-1", "RAM : 40 %", false), resultat);
        Assert.Equal(1, action.NombreExecutions);
        Assert.Empty(confirmateur.Questions);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Execute], _journal.Etapes);
    }

    [Fact]
    public async Task OutilConfirme_ConfirmationAcceptee_PoseLaReformulationPuisExecute()
    {
        var action = new ActionFactice("Je lance le mode jeu : Unity et Discord.", "Mode jeu lancé.");
        var confirmateur = new ConfirmateurFactice(true);
        var executeur = Executeur(confirmateur, new OutilFactice("lancer_mode", NiveauOutil.Confirme, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("lancer_mode"), CancellationToken.None);

        Assert.False(resultat.EstErreur);
        Assert.Equal(1, action.NombreExecutions);
        Assert.Equal(["Je lance le mode jeu : Unity et Discord."], confirmateur.Questions);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Confirme, EtapeAudit.Execute], _journal.Etapes);
    }

    [Fact]
    public async Task OutilConfirme_ConfirmationRefusee_NExecutePasEtRenvoieUneErreur()
    {
        var action = new ActionFactice("Je lance le mode jeu.", "Mode jeu lancé.");
        var executeur = Executeur(new ConfirmateurFactice(false), new OutilFactice("lancer_mode", NiveauOutil.Confirme, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("lancer_mode"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Equal(0, action.NombreExecutions);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Refuse], _journal.Etapes);
    }

    [Fact]
    public async Task MiseEnVeillePendantLaConfirmation_TraiteeCommeUnRefusJournalise()
    {
        var action = new ActionFactice("Je lance le mode jeu.", "Mode jeu lancé.");
        var executeur = Executeur(ConfirmateurFactice.EnAttente(), new OutilFactice("lancer_mode", NiveauOutil.Confirme, action));
        using var veille = new CancellationTokenSource();

        Task<ResultatOutil> enCours = executeur.ExecuterAsync(Appel("lancer_mode"), veille.Token);
        await veille.CancelAsync();
        ResultatOutil resultat = await enCours;

        Assert.True(resultat.EstErreur);
        Assert.Equal(0, action.NombreExecutions);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Refuse], _journal.Etapes);
    }

    [Fact]
    public async Task ConfirmateurEnPanne_TraiteCommeUnRefus()
    {
        var action = new ActionFactice("Je lance le mode jeu.", "Mode jeu lancé.");
        var confirmateur = new ConfirmateurFactice(_ => throw new InvalidOperationException("micro débranché"));
        var executeur = Executeur(confirmateur, new OutilFactice("lancer_mode", NiveauOutil.Confirme, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("lancer_mode"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Equal(0, action.NombreExecutions);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Refuse], _journal.Etapes);
    }

    [Fact]
    public async Task OutilInconnu_RenvoieUneErreurEtJournaliseLeRejet()
    {
        var executeur = Executeur(new ConfirmateurFactice(true));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("formater_disque"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Contains("formater_disque", resultat.Contenu);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Rejete], _journal.Etapes);
    }

    [Fact]
    public async Task ParametresInvalides_RejetesAvantTouteConfirmation()
    {
        var confirmateur = new ConfirmateurFactice(true);
        var outil = new OutilFactice("lancer_mode", NiveauOutil.Confirme,
            _ => Result.Fail<IActionPreparee>(new Error("mode inconnu : « casino »", ErrorType.Validation)));
        var executeur = Executeur(confirmateur, outil);

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("lancer_mode", """{"mode":"casino"}"""), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Contains("casino", resultat.Contenu);
        Assert.Empty(confirmateur.Questions);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Rejete], _journal.Etapes);
    }

    [Fact]
    public async Task ExceptionDansLaPreparation_DevientUneErreur()
    {
        var outil = new OutilFactice("lancer_mode", NiveauOutil.Libre, _ => throw new KeyNotFoundException("mode"));
        var executeur = Executeur(new ConfirmateurFactice(true), outil);

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("lancer_mode"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Rejete], _journal.Etapes);
    }

    [Fact]
    public async Task EchecRopPendantLExecution_DevientUneErreur()
    {
        var action = new ActionFactice("Noter l'idée", _ => Task.FromResult(Result.Fail<string>(new Error("dossier introuvable", ErrorType.NotFound))));
        var executeur = Executeur(new ConfirmateurFactice(true), new OutilFactice("noter_idee", NiveauOutil.Libre, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("noter_idee"), CancellationToken.None);

        Assert.Equal(new ResultatOutil("appel-1", "dossier introuvable", true), resultat);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Echec], _journal.Etapes);
    }

    [Fact]
    public async Task ExceptionPendantLExecution_DevientUneErreur()
    {
        var action = new ActionFactice("Noter l'idée", _ => throw new IOException("disque plein"));
        var executeur = Executeur(new ConfirmateurFactice(true), new OutilFactice("noter_idee", NiveauOutil.Libre, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("noter_idee"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Contains("disque plein", resultat.Contenu);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Echec], _journal.Etapes);
    }

    [Fact]
    public async Task AnnulationPendantLExecution_DevientUneErreur()
    {
        using var veille = new CancellationTokenSource();
        var action = new ActionFactice("Lire la RAM", async annulation =>
        {
            await veille.CancelAsync();
            annulation.ThrowIfCancellationRequested();
            return Result.Ok("jamais");
        });
        var executeur = Executeur(new ConfirmateurFactice(true), new OutilFactice("etat_machine", NiveauOutil.Libre, action));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("etat_machine"), veille.Token);

        Assert.True(resultat.EstErreur);
        Assert.Equal([EtapeAudit.Demande, EtapeAudit.Echec], _journal.Etapes);
    }

    [Fact]
    public async Task JournalIndisponible_NExecuteRien()
    {
        var journal = new JournalMemoire { Indisponible = true };
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");
        var executeur = new ExecuteurOutils([new OutilFactice("etat_machine", NiveauOutil.Libre, action)],
            new ConfirmateurFactice(true), journal, new HorlogeFixe(Maintenant));

        ResultatOutil resultat = await executeur.ExecuterAsync(Appel("etat_machine"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Equal(0, action.NombreExecutions);
    }

    [Fact]
    public async Task ChaqueEntreeDuJournal_PorteLeContexteDeLAppel()
    {
        var action = new ActionFactice("Lire la RAM", "RAM : 40 %");
        var executeur = Executeur(new ConfirmateurFactice(true), new OutilFactice("etat_machine", NiveauOutil.Libre, action));

        await executeur.ExecuterAsync(Appel("etat_machine", """{"aspect":"ram"}"""), CancellationToken.None);

        Assert.All(_journal.Entrees, entree =>
        {
            Assert.Equal(Maintenant, entree.Horodatage);
            Assert.Equal("appel-1", entree.IdAppel);
            Assert.Equal("etat_machine", entree.Outil);
            Assert.Equal("ram", entree.Parametres.GetProperty("aspect").GetString());
            Assert.Equal("lance le mode jeu", entree.DemandeUtilisateur);
        });
        Assert.Equal("RAM : 40 %", _journal.Entrees[^1].Detail);
    }

    [Fact]
    public async Task Preparer_RecoitLaPhraseDeLUtilisateur()
    {
        var outil = new OutilFactice("etat_machine", NiveauOutil.Libre, new ActionFactice("Lire la RAM", "RAM : 40 %"));
        var executeur = Executeur(new ConfirmateurFactice(true), outil);

        await executeur.ExecuterAsync(Appel("etat_machine"), CancellationToken.None);

        Assert.Equal([new ContexteAppel("lance le mode jeu")], outil.Contextes);
    }

    [Fact]
    public void Definitions_SontReluesAChaqueAcces()
    {
        var outil = new OutilFactice("noter_idee", NiveauOutil.Libre, new ActionFactice("x", "x"));
        var executeur = Executeur(new ConfirmateurFactice(true), outil);
        _ = executeur.Definitions;

        outil.SchemaParametres = JsonDocument.Parse("""{"type":"object","properties":{"projet":{"enum":["Xd"]}}}""").RootElement;

        Assert.True(executeur.Definitions[0].SchemaParametres.TryGetProperty("properties", out _));
    }

    [Fact]
    public void Definitions_GardentLOrdreDEnregistrement()
    {
        var action = new ActionFactice("x", "x");
        var executeur = Executeur(new ConfirmateurFactice(true),
            new OutilFactice("noter_idee", NiveauOutil.Libre, action),
            new OutilFactice("etat_machine", NiveauOutil.Libre, action),
            new OutilFactice("lancer_mode", NiveauOutil.Confirme, action));

        Assert.Equal(["noter_idee", "etat_machine", "lancer_mode"], executeur.Definitions.Select(d => d.Nom));
    }

    [Fact]
    public void DeuxOutilsDeMemeNom_SontRefusesALaConstruction()
    {
        var action = new ActionFactice("x", "x");

        Assert.Throws<ArgumentException>(() => Executeur(new ConfirmateurFactice(true),
            new OutilFactice("noter_idee", NiveauOutil.Libre, action),
            new OutilFactice("noter_idee", NiveauOutil.Libre, action)));
    }
}
