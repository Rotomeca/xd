using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class CatalogueCarnetsTests
{
    [Theory]
    [InlineData("note ça dans Xd", "Xd")]
    [InlineData("note ça dans X-D", "Xd")]
    [InlineData("note ça dans x d s'il te plaît", "Xd")]
    [InlineData("pour le projet XD.", "Xd")]
    [InlineData("dans mon jardin secret", "Jardin secret")]
    [InlineData("dans Jardin-Secret", "Jardin secret")]
    [InlineData("le carnet Rotoméca", "Rotomeca")]
    public void NomDuCarnet_EstReconnuMemeTranscritOuMalTape(string phrase, string carnet)
    {
        Assert.True(CatalogueCarnets.EstCite([phrase], carnet));
    }

    [Theory]
    [InlineData("note que je dois installer xdebug", "Xd")]
    [InlineData("note que je dois faire un gratin", "Xd")]
    [InlineData("dans mon jardin", "Jardin secret")]
    public void CarnetNonNomme_NEstPasReconnu(string phrase, string carnet)
    {
        Assert.False(CatalogueCarnets.EstCite([phrase], carnet));
    }

    [Theory]
    [InlineData("dans les générales")]
    [InlineData("Mets-la dans le général.")]
    [InlineData("sans projet")]
    [InlineData("DANS MES NOTES GENERALES")]
    public void NotesGenerales_SontReconnues(string phrase)
    {
        Assert.True(CatalogueCarnets.EstCite([phrase], CatalogueCarnets.General));
    }

    [Fact]
    public void UneParoleQuelconqueSuffit()
    {
        Assert.True(CatalogueCarnets.EstCite(["note que je dois faire un gratin", "oui mais dans Xd"], "Xd"));
    }

    [Theory]
    [InlineData("Xd", true)]
    [InlineData("Jardin secret", true)]
    [InlineData("", false)]
    [InlineData(" Xd", false)]
    [InlineData("..", false)]
    [InlineData(".cache", false)]
    [InlineData("a/b", false)]
    [InlineData(@"a\b", false)]
    [InlineData("C:", false)]
    [InlineData("NUL", false)]
    [InlineData("Général", false)]
    [InlineData("fin.", false)]
    public void NomDeNouveauCarnet_EstValide(string nom, bool attendu)
    {
        Assert.Equal(attendu, CatalogueCarnets.EstNomValide(nom));
    }

    [Fact]
    public void Lister_DonneGeneralPuisLesCarnetsExistants()
    {
        using var dossier = new DossierTemporaire();
        Directory.CreateDirectory(Path.Combine(dossier.Chemin, "projets"));
        File.WriteAllText(Path.Combine(dossier.Chemin, "projets", "Xd.md"), "");
        File.WriteAllText(Path.Combine(dossier.Chemin, "projets", "Atelier.md"), "");
        File.WriteAllText(Path.Combine(dossier.Chemin, "projets", "lisez-moi.txt"), "");

        Assert.Equal(["general", "Atelier", "Xd"], CatalogueCarnets.Lister(dossier.Chemin));
    }

    [Fact]
    public void Trouver_IgnoreCasseAccentsEtSeparateurs()
    {
        Assert.Equal("Xd", CatalogueCarnets.Trouver(["general", "Xd"], "x-d").Value);
        Assert.True(CatalogueCarnets.Trouver(["general", "Xd"], "Atelier").IsEmpty);
    }
}
