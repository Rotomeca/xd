using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class OutilNoterIdeeTests : IDisposable
{
    private const string PhraseGenerale = "note dans mes notes générales que je dois revoir le prompt";
    private const string PhraseXd = "note pour le projet Xd que je dois revoir le prompt";

    private readonly string _racine = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ConfigurationEnMemoire _configuration = new();

    public OutilNoterIdeeTests()
    {
        Directory.CreateDirectory(DossierNotes);
        Directory.CreateDirectory(Path.Combine(DossierProjets, "Xd"));
        Directory.CreateDirectory(Path.Combine(DossierProjets, "Rotomeca"));
        Directory.CreateDirectory(Path.Combine(DossierProjets, ".cache"));
        _configuration.Valeur = new ConfigurationNotes(DossierNotes, [DossierProjets]);
    }

    private string DossierNotes => Path.Combine(_racine, "notes");
    private string DossierProjets => Path.Combine(_racine, "projets");
    private string General => Path.Combine(DossierNotes, "general.md");

    public void Dispose() => Directory.Delete(_racine, recursive: true);

    private OutilNoterIdee Outil() => new(_configuration, new HorlogeFixe(new DateTimeOffset(2026, 10, 14, 10, 55, 0, TimeSpan.Zero)));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private IResult<IActionPreparee> Preparer(string parametres, string phrase = PhraseGenerale) =>
        Outil().Preparer(Json(parametres), new ContexteAppel(phrase));

    private static string Erreur<T>(IResult<T> resultat) => resultat.Match(Ok: _ => "(succès)", Err: e => e.Message);

    private async Task<string> Noter(string parametres, string phrase = PhraseGenerale)
    {
        IActionPreparee action = Preparer(parametres, phrase).Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));
        return (await action.ExecuterAsync(CancellationToken.None)).Match(Ok: t => t, Err: e => "ERREUR " + e.Message);
    }

    [Fact]
    public async Task DestinationGenerale_VaDansLesNotesGenerales()
    {
        Assert.Equal("Noté dans les notes générales.", await Noter("""{"texte":"revoir le prompt","destination":"general"}"""));

        Assert.Equal($"# general{Environment.NewLine}{Environment.NewLine}- 2026-10-14 10:55 — revoir le prompt{Environment.NewLine}",
            File.ReadAllText(General));
    }

    [Fact]
    public async Task DestinationProjet_VaDansLeFichierDuProjet()
    {
        Assert.Equal("Noté dans les notes du projet Xd.", await Noter("""{"texte":"ajouter le mot d'éveil","destination":"Xd"}""", PhraseXd));

        Assert.Contains("ajouter le mot d'éveil", File.ReadAllText(Path.Combine(DossierNotes, "projets", "Xd.md")));
    }

    [Fact]
    public async Task EcritureDeNote_EstUnAjoutSeul()
    {
        const string existant = "# general\n\nContenu écrit à la main, à ne jamais perdre.\n";
        File.WriteAllText(General, existant);

        await Noter("""{"texte":"première","destination":"general"}""");
        await Noter("""{"texte":"seconde","destination":"general"}""");

        string contenu = File.ReadAllText(General);
        Assert.StartsWith(existant, contenu);
        Assert.EndsWith($"- 2026-10-14 10:55 — première{Environment.NewLine}- 2026-10-14 10:55 — seconde{Environment.NewLine}", contenu);
    }

    [Fact]
    public async Task TexteSurPlusieursLignes_DevientUneSeuleLigne()
    {
        await Noter("""{"texte":"ligne un\n# pas un titre\r\nligne\ttrois","destination":"general"}""");

        Assert.EndsWith($"— ligne un # pas un titre ligne trois{Environment.NewLine}", File.ReadAllText(General));
    }

    [Theory]
    [InlineData("note que je dois faire un gratin de pâtes", "general")]
    [InlineData("note que je dois faire un gratin de pâtes", "Xd")]
    [InlineData("note pour Rotomeca que je dois relire la doc", "Xd")]
    [InlineData("note que je dois installer xdebug", "Xd")]
    public void DestinationNonCiteeParLUtilisateur_EstRefuseePourQueLeModeleDemande(string phrase, string destination)
    {
        IResult<IActionPreparee> resultat = Preparer(JsonSerializer.Serialize(new { texte = "x", destination }), phrase);

        Assert.False(resultat.IsSuccess);
        Assert.Contains("Demande-lui : dans les notes générales ou dans un projet (Rotomeca, Xd) ?", Erreur(resultat));
    }

    [Theory]
    [InlineData("dans les générales", "general")]
    [InlineData("mets ça dans mes notes generales", "general")]
    [InlineData("sans projet", "general")]
    [InlineData("dans Xd.", "Xd")]
    [InlineData("pour le projet XD", "Xd")]
    [InlineData("rotomeca", "Rotomeca")]
    public void DestinationCiteeParLUtilisateur_EstAcceptee(string phrase, string destination)
    {
        Assert.True(Preparer(JsonSerializer.Serialize(new { texte = "x", destination }), phrase).IsSuccess);
    }

    [Theory]
    [InlineData("""{"texte":"x","destination":"Inconnu"}""")]
    [InlineData("""{"texte":"x","destination":"xd"}""")]
    [InlineData("""{"texte":"x","destination":".cache"}""")]
    [InlineData("""{"texte":"x","destination":"../notes"}""")]
    [InlineData("""{"texte":"x","destination":3}""")]
    [InlineData("""{"texte":"x"}""")]
    public void DestinationHorsListe_EstRefuseeEnDonnantLesChoix(string parametres)
    {
        IResult<IActionPreparee> resultat = Preparer(parametres, "note ça pour xd, .cache, ../notes, inconnu");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("Rotomeca, Xd", Erreur(resultat));
    }

    [Theory]
    [InlineData("""{"destination":"general"}""")]
    [InlineData("""{"texte":"   ","destination":"general"}""")]
    [InlineData("""{"texte":42,"destination":"general"}""")]
    [InlineData("""{"texte":"x","destination":"general","fichier":"C:\\Windows\\win.ini"}""")]
    [InlineData("""["x"]""")]
    public void ParametresMalFormes_SontRefuses(string parametres)
    {
        Assert.False(Preparer(parametres).IsSuccess);
    }

    [Fact]
    public void NoteTropLongue_EstRefusee()
    {
        Assert.False(Preparer(JsonSerializer.Serialize(new { texte = new string('a', OutilNoterIdee.LongueurMax + 1), destination = "general" })).IsSuccess);
    }

    [Fact]
    public void SansDossierDeNotes_DemandeAuModeleDeQuestionnerLUtilisateur()
    {
        _configuration.Valeur = ConfigurationNotes.Vide;

        IResult<IActionPreparee> resultat = Preparer("""{"texte":"x","destination":"general"}""");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("configurer_notes", Erreur(resultat));
    }

    [Fact]
    public void Preparer_NEcritRien()
    {
        Preparer("""{"texte":"x","destination":"general"}""");

        Assert.False(File.Exists(General));
    }

    [Fact]
    public async Task DossierDeNotesDisparu_RenvoieUneErreurSansLeRecreer()
    {
        IActionPreparee action = Preparer("""{"texte":"x","destination":"general"}""").Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));
        Directory.Delete(DossierNotes, recursive: true);

        IResult<string> resultat = await action.ExecuterAsync(CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.Contains("introuvable", Erreur(resultat));
        Assert.False(Directory.Exists(DossierNotes));
    }

    [Fact]
    public void Schema_ProposeGeneralPuisLesProjetsEtSuitLesNouveauxDossiers()
    {
        OutilNoterIdee outil = Outil();
        Assert.Equal(["general", "Rotomeca", "Xd"], Destinations(outil.SchemaParametres));

        Directory.CreateDirectory(Path.Combine(DossierProjets, "Atelier"));

        Assert.Equal(["general", "Atelier", "Rotomeca", "Xd"], Destinations(outil.SchemaParametres));
        Assert.Equal(["texte", "destination"], outil.SchemaParametres.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void SansDossierDeProjets_SeuleLaDestinationGeneraleEstProposee()
    {
        _configuration.Valeur = new ConfigurationNotes(DossierNotes, []);

        Assert.Equal(["general"], Destinations(Outil().SchemaParametres));
    }

    private static List<string?> Destinations(JsonElement schema) =>
        [.. schema.GetProperty("properties").GetProperty("destination").GetProperty("enum").EnumerateArray().Select(e => e.GetString())];

    private sealed class ConfigurationEnMemoire : IStockageConfigurationNotes
    {
        public ConfigurationNotes Valeur { get; set; } = ConfigurationNotes.Vide;

        public IResult<ConfigurationNotes> Lire() => Result.Ok(Valeur);

        public IResult<bool> Ecrire(ConfigurationNotes configuration)
        {
            Valeur = configuration;
            return Result.Ok(true);
        }
    }

    private sealed class HorlogeFixe(DateTimeOffset maintenant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => maintenant;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
