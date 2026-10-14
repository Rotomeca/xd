using Rotomeca.Rop.Interfaces;
using Xd.Local.Installation;
using Xd.Local.Serveur;

namespace Xd.Local.Tests;

public sealed class FichierConfigurationLlmTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));

    private string Chemin => Path.Combine(_dossier, "config", "llm.json");

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void Ecrire_PuisLire_RedonneLaMemeConfiguration()
    {
        var configuration = new ConfigurationLlm(@"C:\xd\llama-server.exe", @"C:\xd\Qwen3-8B-Q4_K_M.gguf", PalierLlm.Standard, "all");

        Assert.True(FichierConfigurationLlm.Ecrire(Chemin, configuration).IsSuccess);
        IResult<ConfigurationLlm> relue = FichierConfigurationLlm.Lire(Chemin);

        Assert.Equal(configuration, relue.Match(Ok: c => c, Err: e => throw new Xunit.Sdk.XunitException(e.Message)));
        Assert.Contains("\"palier\": \"standard\"", File.ReadAllText(Chemin));
    }

    [Fact]
    public void FichierAbsent_RenvoieUneErreurQuiIndiqueLeSetup()
    {
        IResult<ConfigurationLlm> resultat = FichierConfigurationLlm.Lire(Chemin);

        Assert.False(resultat.IsSuccess);
        Assert.Contains("xd setup llm", resultat.Match(Ok: _ => string.Empty, Err: e => e.Message));
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("""{ "cheminServeur": "a", "cheminModele": "b", "palier": "gigantesque", "couchesGpu": "all" }""")]
    public void FichierInvalide_RenvoieUneErreur(string contenu)
    {
        Directory.CreateDirectory(Path.Combine(_dossier, "config"));
        File.WriteAllText(Chemin, contenu);

        Assert.False(FichierConfigurationLlm.Lire(Chemin).IsSuccess);
    }

    [Fact]
    public void Adresse_EstSurLaBoucleLocale()
    {
        var configuration = new ConfigurationLlm("s", "m", PalierLlm.Leger, "0", Port: 8123);

        Assert.Equal("http://127.0.0.1:8123/", configuration.Adresse.ToString());
    }
}
