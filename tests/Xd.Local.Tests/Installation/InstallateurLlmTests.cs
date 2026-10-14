using Rotomeca.Core.Optionals;
using Rotomeca.Rop.Interfaces;
using Xd.Local.Installation;
using Xd.Local.Serveur;

namespace Xd.Local.Tests.Installation;

public sealed class InstallateurLlmTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ServeurCompteur _serveur = new();

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private InstallateurLlm Installateur(InteractionFactice interaction) => new(
        new MesureFactice(),
        new Telechargeur(new HttpClient(_serveur)),
        interaction,
        new EmplacementsXd(Path.Combine(_dossier, "config"), Path.Combine(_dossier, "donnees")));

    [Fact]
    public async Task TelechargementRefuse_NeTelechargeRienEtNEcritPasDeConfiguration()
    {
        var interaction = new InteractionFactice(reponse: false);

        IResult<ConfigurationLlm> resultat = await Installateur(interaction).InstallerAsync(MayBe<PalierLlm>.Null, CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.Single(interaction.Questions);
        Assert.Equal(0, _serveur.Requetes);
        Assert.False(File.Exists(Path.Combine(_dossier, "config", "llm.json")));
    }

    [Fact]
    public async Task AvantTouteQuestion_AnnonceLesFichiersEtLeurSource()
    {
        var interaction = new InteractionFactice(reponse: false);

        await Installateur(interaction).InstallerAsync(MayBe<PalierLlm>.Null, CancellationToken.None);

        Assert.Contains(interaction.Messages, m => m.Contains("Qwen3-4B-Q4_K_M.gguf") && m.Contains("huggingface.co"));
        Assert.Contains(interaction.Messages, m => m.Contains("llama-b11429") && m.Contains("github.com"));
    }

    private sealed class MesureFactice : IMesureMemoireVideo
    {
        public Task<MayBe<long>> MesurerLibreAsync(CancellationToken annulation) => Task.FromResult(MayBe<long>.Null);
    }

    private sealed class InteractionFactice(bool reponse) : IInteractionInstallation
    {
        public List<string> Messages { get; } = [];
        public List<string> Questions { get; } = [];

        public void Informer(string message) => Messages.Add(message);

        public Task<bool> ConfirmerAsync(string question, CancellationToken annulation)
        {
            Questions.Add(question);
            return Task.FromResult(reponse);
        }

        public IProgress<long> SuivreTelechargement(FichierDistant fichier) => new Progress<long>();
    }

    private sealed class ServeurCompteur : HttpMessageHandler
    {
        public int Requetes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Requetes++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
