using System.Text.Json;
using Rotomeca.Core.Optionals;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Execution;
using Xd.Core.Outils;
using Xd.Core.Permissions;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class OutilConfigurerNotesTests : IDisposable
{
    private readonly DossierTemporaire _racine = new();
    private readonly ConfigurationEnMemoire _configuration = new();

    private string DossierNotes => Path.Combine(_racine.Chemin, "notes");

    public void Dispose() => _racine.Dispose();

    private OutilConfigurerNotes Outil() => new(_configuration, new LecturePhraseChemin(
        Path.Combine(_racine.Chemin, "Documents"), _racine.Chemin, Directory.Exists, []));

    private IResult<IActionPreparee> Preparer(string parametres, params string[] paroles) =>
        Outil().Preparer(JsonDocument.Parse(parametres).RootElement, new ContexteAppel(paroles[0]) { ReponsesUtilisateur = paroles[1..] });

    private static IActionPreparee Action(IResult<IActionPreparee> resultat) =>
        resultat.Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

    [Fact]
    public void Definition_EstConfirmeeEtSansParametre()
    {
        OutilConfigurerNotes outil = Outil();

        Assert.Equal(NiveauOutil.Confirme, outil.Niveau);
        Assert.Empty(outil.SchemaParametres.GetProperty("properties").EnumerateObject());
    }

    [Fact]
    public async Task Dossier_EstLuDansLaPhraseConfirmePuisEnregistre()
    {
        IActionPreparee action = Action(Preparer("{}", $"Range-les dans {DossierNotes}."));

        Assert.Equal($"Je range tes notes dans « {DossierNotes} », un nouveau dossier que je vais créer. Je confirme ?", action.Reformulation);
        Assert.Equal([DossierNotes], action.ElementsACiter);
        Assert.False(Directory.Exists(DossierNotes));

        Assert.True((await action.ExecuterAsync(CancellationToken.None)).IsSuccess);
        Assert.True(Directory.Exists(DossierNotes));
        Assert.Equal(DossierNotes, _configuration.Valeur.DossierNotes);
    }

    [Theory]
    [InlineData("par défaut, ça ira", "xd", "notes")]
    [InlineData("dans mes documents, dossier TEST", "TEST", "")]
    [InlineData("mes documents/TEST", "TEST", "")]
    public void RaccourcisDictables_DonnentUnDossierDesDocuments(string phrase, string sousDossier, string sousSousDossier)
    {
        IActionPreparee action = Action(Preparer("{}", phrase));

        Assert.Contains(Path.TrimEndingDirectorySeparator(Path.Combine(_racine.Chemin, "Documents", sousDossier, sousSousDossier)), action.Reformulation);
    }

    [Fact]
    public void DossierDonneDansUneReponseUlterieure_EstPrefere()
    {
        IActionPreparee action = Action(Preparer("{}", $"range mes notes dans {Path.Combine(_racine.Chemin, "ancien")}", $"non, plutôt dans {DossierNotes}"));

        Assert.Contains(DossierNotes, action.Reformulation);
    }

    [Theory]
    [InlineData("""{"chemin":"C:\\Windows"}""")]
    [InlineData("""{"element":"dossier_notes"}""")]
    [InlineData("""[]""")]
    public void ParametresHorsContrat_SontRefuses(string parametres)
    {
        Assert.False(Preparer(parametres, $"dans {DossierNotes}").IsSuccess);
    }

    [Fact]
    public void SansDossierDansLesParoles_LeModeleDoitRedemander()
    {
        IResult<IActionPreparee> resultat = Preparer("{}", "je ne sais pas, choisis pour moi");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("Redemande", resultat.Match(Ok: _ => string.Empty, Err: e => e.Message));
    }

    [Fact]
    public async Task ConfirmationRefusee_NeModifieRien()
    {
        var executeur = new ExecuteurOutils([Outil()], new ConfirmateurQuiRefuse(), new JournalMuet(), TimeProvider.System);

        ResultatOutil resultat = await executeur.ExecuterAsync(
            new AppelOutil("a1", "configurer_notes", JsonDocument.Parse("{}").RootElement, $"dans {DossierNotes}"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Null(_configuration.Valeur.DossierNotes);
        Assert.False(Directory.Exists(DossierNotes));
    }

    private sealed class ConfirmateurQuiRefuse : IConfirmateur
    {
        public Task<MayBe<string>> PoserQuestionAsync(string question, CancellationToken annulation) => Task.FromResult<MayBe<string>>("non");
    }

    private sealed class JournalMuet : IJournalAudit
    {
        public Task<IResult<bool>> EcrireAsync(EntreeAudit entree, CancellationToken annulation) => Task.FromResult(Result.Ok(true));
    }
}
