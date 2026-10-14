using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class FichierConfigurationNotesTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));

    private string Chemin => Path.Combine(_dossier, "notes.json");

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void FichierAbsent_DonneUneConfigurationVide()
    {
        ConfigurationNotes configuration = new FichierConfigurationNotes(Chemin).Lire().Match(Ok: c => c, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

        Assert.Null(configuration.DossierNotes);
        Assert.Empty(configuration.DossiersProjets);
    }

    [Fact]
    public void Ecrire_PuisLire_RedonneLesMemesValeurs()
    {
        var fichier = new FichierConfigurationNotes(Chemin);

        Assert.True(fichier.Ecrire(new ConfigurationNotes(@"D:\Notes", [@"E:\Projets"])).IsSuccess);
        ConfigurationNotes relue = fichier.Lire().Match(Ok: c => c, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

        Assert.Equal(@"D:\Notes", relue.DossierNotes);
        Assert.Equal([@"E:\Projets"], relue.DossiersProjets);
    }

    [Fact]
    public void FichierIllisible_RenvoieUneErreur()
    {
        Directory.CreateDirectory(_dossier);
        File.WriteAllText(Chemin, "pas du json");

        Assert.False(new FichierConfigurationNotes(Chemin).Lire().IsSuccess);
    }
}
