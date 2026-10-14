using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class OutilNoterIdeeTests : IDisposable
{
    private static readonly ContexteAppel Contexte = new("note que je dois revoir le prompt");

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

    private IResult<IActionPreparee> Preparer(string parametres) => Outil().Preparer(Json(parametres), Contexte);

    private static string Erreur<T>(IResult<T> resultat) => resultat.Match(Ok: _ => "(succès)", Err: e => e.Message);

    private async Task<string> Noter(string parametres)
    {
        IActionPreparee action = Preparer(parametres).Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));
        return (await action.ExecuterAsync(CancellationToken.None)).Match(Ok: t => t, Err: e => "ERREUR " + e.Message);
    }

    [Fact]
    public async Task SansProjet_VaDansLesNotesGenerales()
    {
        Assert.Equal("Noté dans les notes générales.", await Noter("""{"texte":"revoir le prompt"}"""));

        Assert.Equal($"# general{Environment.NewLine}{Environment.NewLine}- 2026-10-14 10:55 — revoir le prompt{Environment.NewLine}",
            File.ReadAllText(General));
    }

    [Fact]
    public async Task AvecProjet_VaDansLeFichierDuProjet()
    {
        Assert.Equal("Noté dans les notes du projet Xd.", await Noter("""{"texte":"ajouter le mot d'éveil","projet":"Xd"}"""));

        Assert.Contains("ajouter le mot d'éveil", File.ReadAllText(Path.Combine(DossierNotes, "projets", "Xd.md")));
    }

    [Fact]
    public async Task EcritureDeNote_EstUnAjoutSeul()
    {
        const string existant = "# general\n\nContenu écrit à la main, à ne jamais perdre.\n";
        File.WriteAllText(General, existant);

        await Noter("""{"texte":"première"}""");
        await Noter("""{"texte":"seconde"}""");

        string contenu = File.ReadAllText(General);
        Assert.StartsWith(existant, contenu);
        Assert.EndsWith($"- 2026-10-14 10:55 — première{Environment.NewLine}- 2026-10-14 10:55 — seconde{Environment.NewLine}", contenu);
    }

    [Fact]
    public async Task TexteSurPlusieursLignes_DevientUneSeuleLigne()
    {
        await Noter("""{"texte":"ligne un\n# pas un titre\r\nligne\ttrois"}""");

        Assert.EndsWith($"— ligne un # pas un titre ligne trois{Environment.NewLine}", File.ReadAllText(General));
    }

    [Theory]
    [InlineData("""{"texte":"x","projet":"Inconnu"}""")]
    [InlineData("""{"texte":"x","projet":"xd"}""")]
    [InlineData("""{"texte":"x","projet":".cache"}""")]
    [InlineData("""{"texte":"x","projet":"../notes"}""")]
    [InlineData("""{"texte":"x","projet":3}""")]
    public void ProjetHorsListe_EstRefuseEnDonnantLesProjetsConnus(string parametres)
    {
        IResult<IActionPreparee> resultat = Preparer(parametres);

        Assert.False(resultat.IsSuccess);
        Assert.Contains("Projets connus : Rotomeca, Xd", Erreur(resultat));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"texte":"   "}""")]
    [InlineData("""{"texte":42}""")]
    [InlineData("""{"texte":"x","fichier":"C:\\Windows\\win.ini"}""")]
    [InlineData("""["x"]""")]
    public void ParametresMalFormes_SontRefuses(string parametres)
    {
        Assert.False(Preparer(parametres).IsSuccess);
    }

    [Fact]
    public void NoteTropLongue_EstRefusee()
    {
        Assert.False(Preparer(JsonSerializer.Serialize(new { texte = new string('a', OutilNoterIdee.LongueurMax + 1) })).IsSuccess);
    }

    [Fact]
    public void SansDossierDeNotes_DemandeAuModeleDeQuestionnerLUtilisateur()
    {
        _configuration.Valeur = ConfigurationNotes.Vide;

        IResult<IActionPreparee> resultat = Preparer("""{"texte":"x"}""");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("configurer_notes", Erreur(resultat));
    }

    [Fact]
    public void Preparer_NEcritRien()
    {
        Preparer("""{"texte":"x"}""");

        Assert.False(File.Exists(General));
    }

    [Fact]
    public async Task DossierDeNotesDisparu_RenvoieUneErreurSansLeRecreer()
    {
        IActionPreparee action = Preparer("""{"texte":"x"}""").Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));
        Directory.Delete(DossierNotes, recursive: true);

        IResult<string> resultat = await action.ExecuterAsync(CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.Contains("introuvable", Erreur(resultat));
        Assert.False(Directory.Exists(DossierNotes));
    }

    [Fact]
    public void Schema_ProposeLesProjetsConnusEtSuitLesNouveauxDossiers()
    {
        OutilNoterIdee outil = Outil();
        Assert.Equal(["Rotomeca", "Xd"], Projets(outil.SchemaParametres));

        Directory.CreateDirectory(Path.Combine(DossierProjets, "Atelier"));

        Assert.Equal(["Atelier", "Rotomeca", "Xd"], Projets(outil.SchemaParametres));
        Assert.Equal(["texte"], outil.SchemaParametres.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void SansDossierDeProjets_LeSchemaNeProposePasDeProjet()
    {
        _configuration.Valeur = new ConfigurationNotes(DossierNotes, []);

        Assert.False(Outil().SchemaParametres.GetProperty("properties").TryGetProperty("projet", out _));
    }

    private static List<string?> Projets(JsonElement schema) =>
        [.. schema.GetProperty("properties").GetProperty("projet").GetProperty("enum").EnumerateArray().Select(e => e.GetString())];

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
