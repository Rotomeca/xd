using System.Text.Json;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class OutilNoterIdeeTests : IDisposable
{
    private readonly DossierTemporaire _racine = new();
    private readonly ConfigurationEnMemoire _configuration = new();

    public OutilNoterIdeeTests()
    {
        Directory.CreateDirectory(Path.Combine(DossierNotes, "projets"));
        File.WriteAllText(Path.Combine(DossierNotes, "projets", "Xd.md"), "# Xd\n\n");
        _configuration.Valeur = new ConfigurationNotes(DossierNotes);
    }

    private string DossierNotes => Path.Combine(_racine.Chemin, "notes");
    private string General => Path.Combine(DossierNotes, "general.md");

    public void Dispose() => _racine.Dispose();

    private OutilNoterIdee Outil() => new(_configuration, new HorlogeFixe(new DateTimeOffset(2026, 10, 14, 10, 55, 0, TimeSpan.Zero)));

    private IResult<IActionPreparee> Preparer(object parametres, params string[] paroles) =>
        Outil().Preparer(JsonSerializer.SerializeToElement(parametres), new ContexteAppel(paroles[0]) { ReponsesUtilisateur = paroles[1..] });

    private static IActionPreparee Action(IResult<IActionPreparee> resultat) =>
        resultat.Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

    private static string Erreur<T>(IResult<T> resultat) => resultat.Match(Ok: _ => "(succès)", Err: e => e.Message);

    private static async Task<string> Executer(IActionPreparee action) =>
        (await action.ExecuterAsync(CancellationToken.None)).Match(Ok: t => t, Err: e => "ERREUR " + e.Message);

    [Fact]
    public async Task NotesGeneralesCitees_SontEcritesSansConfirmation()
    {
        IActionPreparee action = Action(Preparer(new { texte = "revoir le prompt", destination = "general" }, "note dans les générales de revoir le prompt"));

        Assert.False(action.ConfirmationRequise);
        Assert.StartsWith($"Noté dans les notes générales, fichier : {General}.", await Executer(action));
        Assert.Equal($"# general{Environment.NewLine}{Environment.NewLine}- 2026-10-14 10:55 — revoir le prompt{Environment.NewLine}", File.ReadAllText(General));
    }

    [Fact]
    public async Task CarnetExistantCite_EstEcritSansConfirmation()
    {
        IActionPreparee action = Action(Preparer(new { texte = "mot d'éveil", destination = "xd" }, "note dans X D : mot d'éveil"));

        Assert.False(action.ConfirmationRequise);
        await Executer(action);
        Assert.EndsWith($"- 2026-10-14 10:55 — mot d'éveil{Environment.NewLine}", File.ReadAllText(Path.Combine(DossierNotes, "projets", "Xd.md")));
    }

    [Fact]
    public void DestinationNonCitee_ExigeUneConfirmationEtDitQuoiDemanderSiRefus()
    {
        IActionPreparee action = Action(Preparer(new { texte = "faire un gratin", destination = "general" }, "note que je dois faire un gratin"));

        Assert.True(action.ConfirmationRequise);
        Assert.Equal("Je note « faire un gratin » dans les notes générales. Je confirme ?", action.Reformulation);
        Assert.Equal(["générales"], action.ElementsACiter);
        Assert.Contains("carnet existant (Xd)", action.MessageSiRefus);
    }

    [Fact]
    public void CarnetCiteDansUneReponseALaConfirmation_SePasseDeConfirmation()
    {
        IActionPreparee action = Action(Preparer(new { texte = "faire un gratin", destination = "Xd" }, "note que je dois faire un gratin", "oui mais dans Xd"));

        Assert.False(action.ConfirmationRequise);
    }

    [Fact]
    public async Task NouveauCarnetNomme_EstCreeApresConfirmation()
    {
        IActionPreparee action = Action(Preparer(new { texte = "acheter du terreau", destination = "Jardin" }, "note dans le carnet jardin d'acheter du terreau"));

        Assert.True(action.ConfirmationRequise);
        Assert.Equal("Je crée le carnet « Jardin » et j'y note « acheter du terreau ». Je confirme ?", action.Reformulation);
        Assert.Equal(["Jardin"], action.ElementsACiter);

        await Executer(action);
        Assert.Contains("acheter du terreau", File.ReadAllText(Path.Combine(DossierNotes, "projets", "Jardin.md")));
    }

    [Theory]
    [InlineData("Courses")]
    [InlineData("Jardin")]
    public void NouveauCarnetQueLUtilisateurNAPasNomme_EstRefuse(string destination)
    {
        IResult<IActionPreparee> resultat = Preparer(new { texte = "x", destination }, "note que je dois acheter du pain");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("Ne choisis pas le nom à sa place", Erreur(resultat));
    }

    [Theory]
    [InlineData("../evade")]
    [InlineData("C:")]
    [InlineData("NUL")]
    [InlineData(".cache")]
    public void NomDeCarnetDangereux_EstRefuseMemeSiLUtilisateurLeDit(string destination)
    {
        Assert.False(Preparer(new { texte = "x", destination }, $"note dans {destination}").IsSuccess);
    }

    [Fact]
    public async Task EcritureDeNote_EstUnAjoutSeul()
    {
        const string existant = "# general\n\nContenu écrit à la main, à ne jamais perdre.\n";
        File.WriteAllText(General, existant);

        await Executer(Action(Preparer(new { texte = "première", destination = "general" }, "dans les générales")));
        await Executer(Action(Preparer(new { texte = "seconde", destination = "general" }, "dans les générales")));

        string contenu = File.ReadAllText(General);
        Assert.StartsWith(existant, contenu);
        Assert.EndsWith($"- 2026-10-14 10:55 — première{Environment.NewLine}- 2026-10-14 10:55 — seconde{Environment.NewLine}", contenu);
    }

    [Fact]
    public async Task TexteSurPlusieursLignes_DevientUneSeuleLigne()
    {
        await Executer(Action(Preparer(new { texte = "ligne un\n# pas un titre\r\nligne\ttrois", destination = "general" }, "dans les générales")));

        Assert.EndsWith($"— ligne un # pas un titre ligne trois{Environment.NewLine}", File.ReadAllText(General));
    }

    [Theory]
    [InlineData("""{"destination":"general"}""")]
    [InlineData("""{"texte":"x"}""")]
    [InlineData("""{"texte":"x","destination":"  "}""")]
    [InlineData("""{"texte":"   ","destination":"general"}""")]
    [InlineData("""{"texte":42,"destination":"general"}""")]
    [InlineData("""{"texte":"x","destination":"general","fichier":"C:\\Windows\\win.ini"}""")]
    [InlineData("""["x"]""")]
    public void ParametresMalFormes_SontRefuses(string parametres)
    {
        Assert.False(Outil().Preparer(JsonDocument.Parse(parametres).RootElement, new ContexteAppel("dans les générales")).IsSuccess);
    }

    [Fact]
    public void NoteTropLongue_EstRefusee()
    {
        Assert.False(Preparer(new { texte = new string('a', OutilNoterIdee.LongueurMax + 1), destination = "general" }, "dans les générales").IsSuccess);
    }

    [Fact]
    public void SansDossierDeNotes_DemandeAuModeleDeQuestionnerLUtilisateur()
    {
        _configuration.Valeur = ConfigurationNotes.Vide;

        IResult<IActionPreparee> resultat = Preparer(new { texte = "x", destination = "general" }, "dans les générales");

        Assert.False(resultat.IsSuccess);
        Assert.Contains("configurer_notes", Erreur(resultat));
    }

    [Fact]
    public void Preparer_NEcritRien()
    {
        Preparer(new { texte = "x", destination = "general" }, "dans les générales");

        Assert.False(File.Exists(General));
    }

    [Fact]
    public async Task DossierDeNotesDisparu_RenvoieUneErreurSansLeRecreer()
    {
        IActionPreparee action = Action(Preparer(new { texte = "x", destination = "general" }, "dans les générales"));
        Directory.Delete(DossierNotes, recursive: true);

        Assert.Contains("introuvable", await Executer(action));
        Assert.False(Directory.Exists(DossierNotes));
    }

    [Fact]
    public void Schema_ListeLesCarnetsExistantsEtSuitLesNouveaux()
    {
        OutilNoterIdee outil = Outil();
        Assert.Contains("Carnets existants : general, Xd.", Description(outil));

        File.WriteAllText(Path.Combine(DossierNotes, "projets", "Atelier.md"), "");

        Assert.Contains("Carnets existants : general, Atelier, Xd.", Description(outil));
        Assert.Equal(["texte", "destination"], outil.SchemaParametres.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }

    private static string Description(OutilNoterIdee outil) =>
        outil.SchemaParametres.GetProperty("properties").GetProperty("destination").GetProperty("description").GetString() ?? string.Empty;
}
