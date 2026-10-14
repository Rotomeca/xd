using System.Text.Json;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class OutilConsulterNotesTests : IDisposable
{
    private readonly DossierTemporaire _racine = new();
    private readonly ConfigurationEnMemoire _configuration = new();

    public OutilConsulterNotesTests()
    {
        Directory.CreateDirectory(Path.Combine(DossierNotes, "projets"));
        File.WriteAllText(Path.Combine(DossierNotes, "projets", "Xd.md"),
            "# Xd\n\n" + string.Join('\n', Enumerable.Range(1, 7).Select(i => $"- 2026-10-14 10:0{i} — idée {i}")) + "\n");
        _configuration.Valeur = new ConfigurationNotes(DossierNotes);
    }

    private string DossierNotes => Path.Combine(_racine.Chemin, "notes");

    public void Dispose() => _racine.Dispose();

    private async Task<string> Consulter(object parametres)
    {
        IResult<IActionPreparee> preparation = new OutilConsulterNotes(_configuration).Preparer(JsonSerializer.SerializeToElement(parametres), new ContexteAppel("?"));
        IActionPreparee action = preparation.Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));
        Assert.False(action.ConfirmationRequise);
        return (await action.ExecuterAsync(CancellationToken.None)).Match(Ok: t => t, Err: e => "ERREUR " + e.Message);
    }

    [Fact]
    public async Task Emplacement_DonneLeDossierEtLeFichierDesNotesGenerales()
    {
        string reponse = await Consulter(new { quoi = "emplacement" });

        Assert.Contains(DossierNotes, reponse);
        Assert.Contains(Path.Combine(DossierNotes, "general.md"), reponse);
    }

    [Fact]
    public async Task Carnets_ListeLesCarnetsExistants()
    {
        Assert.Equal("Carnets : notes générales, Xd.", await Consulter(new { quoi = "carnets" }));
    }

    [Fact]
    public async Task DernieresNotes_DonneLesCinqPlusRecentes()
    {
        string reponse = await Consulter(new { quoi = "dernieres_notes", carnet = "xd" });

        Assert.StartsWith("Dernières notes du carnet Xd : 2026-10-14 10:03 — idée 3 ;", reponse);
        Assert.DoesNotContain("idée 2", reponse);
        Assert.Contains("idée 7", reponse);
    }

    [Fact]
    public async Task CarnetVide_LeDit()
    {
        Assert.Equal("Aucune note dans les notes générales pour l'instant.", await Consulter(new { quoi = "dernieres_notes", carnet = "general" }));
    }

    [Theory]
    [InlineData("""{"quoi":"dernieres_notes"}""")]
    [InlineData("""{"quoi":"dernieres_notes","carnet":"Inconnu"}""")]
    [InlineData("""{"quoi":"tout"}""")]
    [InlineData("""{"quoi":"carnets","supprimer":true}""")]
    public void DemandeInvalide_EstRefusee(string parametres)
    {
        Assert.False(new OutilConsulterNotes(_configuration).Preparer(JsonDocument.Parse(parametres).RootElement, new ContexteAppel("?")).IsSuccess);
    }

    [Fact]
    public void SansDossierDeNotes_LeDit()
    {
        _configuration.Valeur = ConfigurationNotes.Vide;

        Assert.False(new OutilConsulterNotes(_configuration).Preparer(JsonSerializer.SerializeToElement(new { quoi = "carnets" }), new ContexteAppel("?")).IsSuccess);
    }
}
