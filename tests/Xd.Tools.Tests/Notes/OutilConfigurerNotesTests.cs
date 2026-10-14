using System.Text.Json;
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
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ConfigurationEnMemoire _configuration = new();

    public OutilConfigurerNotesTests()
    {
        Directory.CreateDirectory(Path.Combine(_racine, "projets", "Xd"));
        Directory.CreateDirectory(Path.Combine(_racine, "projets", "Rotomeca"));
    }

    private string DossierNotes => Path.Combine(_racine, "notes");
    private string DossierProjets => Path.Combine(_racine, "projets");

    public void Dispose() => Directory.Delete(_racine, recursive: true);

    private OutilConfigurerNotes Outil() => new(_configuration, new LecturePhraseChemin(
        Path.Combine(_racine, "Documents"), _racine, Directory.Exists, []));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private IResult<IActionPreparee> Preparer(string parametres, string phrase) => Outil().Preparer(Json(parametres), new ContexteAppel(phrase));

    private static IActionPreparee Action(IResult<IActionPreparee> resultat) =>
        resultat.Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

    [Fact]
    public void Definition_EstConfirmeeEtSansParametreDeChemin()
    {
        OutilConfigurerNotes outil = Outil();

        Assert.Equal(NiveauOutil.Confirme, outil.Niveau);
        Assert.Equal(["element"], outil.SchemaParametres.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task DossierDeNotes_EstLuDansLaPhraseConfirmePuisEnregistre()
    {
        IActionPreparee action = Action(Preparer("""{"element":"dossier_notes"}""", $"Range-les dans {DossierNotes}."));

        Assert.Equal($"Je range tes notes dans « {DossierNotes} », un nouveau dossier que je vais créer. Je confirme ?", action.Reformulation);
        Assert.False(Directory.Exists(DossierNotes));

        Assert.True((await action.ExecuterAsync(CancellationToken.None)).IsSuccess);
        Assert.True(Directory.Exists(DossierNotes));
        Assert.Equal(DossierNotes, _configuration.Valeur.DossierNotes);
    }

    [Fact]
    public void ParDefaut_ProposeLeDossierDesDocuments()
    {
        IActionPreparee action = Action(Preparer("""{"element":"dossier_notes"}""", "par défaut, ça ira"));

        Assert.Contains(Path.Combine(_racine, "Documents", "xd", "notes"), action.Reformulation);
    }

    [Fact]
    public async Task DossierDeProjets_ListeSesProjetsEtNEstAjouteQuUneFois()
    {
        IActionPreparee action = Action(Preparer("""{"element":"dossier_projets"}""", $"mes projets sont dans {DossierProjets}"));
        Assert.Equal($"J'ajoute « {DossierProjets} » comme dossier de projets (2 projets : Rotomeca, Xd). Je confirme ?", action.Reformulation);

        await action.ExecuterAsync(CancellationToken.None);
        await action.ExecuterAsync(CancellationToken.None);

        Assert.Equal([DossierProjets], _configuration.Valeur.DossiersProjets);
    }

    [Theory]
    [InlineData("""{"element":"dossier_notes","chemin":"C:\\Windows"}""")]
    [InlineData("""{"element":"fichier_systeme"}""")]
    [InlineData("""{"element":"DOSSIER_NOTES"}""")]
    [InlineData("""{}""")]
    public void ParametresHorsContrat_SontRefuses(string parametres)
    {
        Assert.False(Preparer(parametres, $"dans {DossierNotes}").IsSuccess);
    }

    [Fact]
    public void CheminDansLeTexteDuModele_NEstJamaisLu()
    {
        // Seule la phrase de l'utilisateur compte : sans chemin dedans, l'outil refuse.
        IResult<IActionPreparee> resultat = Preparer("""{"element":"dossier_notes"}""", "je ne sais pas, choisis pour moi");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("Redemande", resultat.Match(Ok: _ => string.Empty, Err: e => e.Message));
    }

    [Fact]
    public void DossierDeProjetsInexistant_EstRefuse()
    {
        Assert.False(Preparer("""{"element":"dossier_projets"}""", $"dans {Path.Combine(_racine, "absent")}").IsSuccess);
    }

    [Fact]
    public async Task ConfirmationRefusee_NeModifieRien()
    {
        var executeur = new ExecuteurOutils([Outil()], new ConfirmateurQuiRefuse(), new JournalMuet(), TimeProvider.System);

        ResultatOutil resultat = await executeur.ExecuterAsync(
            new AppelOutil("a1", "configurer_notes", Json("""{"element":"dossier_notes"}"""), $"dans {DossierNotes}"), CancellationToken.None);

        Assert.True(resultat.EstErreur);
        Assert.Null(_configuration.Valeur.DossierNotes);
        Assert.False(Directory.Exists(DossierNotes));
    }

    private sealed class ConfigurationEnMemoire : IStockageConfigurationNotes
    {
        public ConfigurationNotes Valeur { get; private set; } = ConfigurationNotes.Vide;

        public IResult<ConfigurationNotes> Lire() => Result.Ok(Valeur);

        public IResult<bool> Ecrire(ConfigurationNotes configuration)
        {
            Valeur = configuration;
            return Result.Ok(true);
        }
    }

    private sealed class ConfirmateurQuiRefuse : IConfirmateur
    {
        public Task<bool> ConfirmerAsync(string reformulation, CancellationToken annulation) => Task.FromResult(false);
    }

    private sealed class JournalMuet : IJournalAudit
    {
        public Task<IResult<bool>> EcrireAsync(EntreeAudit entree, CancellationToken annulation) => Task.FromResult(Result.Ok(true));
    }
}
