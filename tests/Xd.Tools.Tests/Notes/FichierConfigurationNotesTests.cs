using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class FichierConfigurationNotesTests : IDisposable
{
    private readonly DossierTemporaire _dossier = new();

    private string Chemin => Path.Combine(_dossier.Chemin, "config", "notes.json");

    public void Dispose() => _dossier.Dispose();

    private static ConfigurationNotes Valeur(FichierConfigurationNotes fichier) =>
        fichier.Lire().Match(Ok: c => c, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

    [Fact]
    public void FichierAbsent_DonneUneConfigurationVide()
    {
        Assert.Null(Valeur(new FichierConfigurationNotes(Chemin)).DossierNotes);
    }

    [Fact]
    public void Ecrire_PuisLire_RedonneLeMemeDossier()
    {
        var fichier = new FichierConfigurationNotes(Chemin);

        Assert.True(fichier.Ecrire(new ConfigurationNotes(@"D:\Notes")).IsSuccess);

        Assert.Equal(@"D:\Notes", Valeur(fichier).DossierNotes);
    }

    [Fact]
    public void AncienFichierAvecDossiersDeProjets_ResteLisible()
    {
        Directory.CreateDirectory(Path.Combine(_dossier.Chemin, "config"));
        File.WriteAllText(Chemin, """{ "dossierNotes": "D:\\Notes", "dossiersProjets": [ "E:\\Projets" ] }""");

        Assert.Equal(@"D:\Notes", Valeur(new FichierConfigurationNotes(Chemin)).DossierNotes);
    }

    [Fact]
    public void FichierIllisible_RenvoieUneErreur()
    {
        Directory.CreateDirectory(Path.Combine(_dossier.Chemin, "config"));
        File.WriteAllText(Chemin, "pas du json");

        Assert.False(new FichierConfigurationNotes(Chemin).Lire().IsSuccess);
    }
}
