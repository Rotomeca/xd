using System.Net;
using System.Security.Cryptography;
using System.Text;
using Rotomeca.Rop.Interfaces;
using Xd.Local.Installation;

namespace Xd.Local.Tests.Installation;

public sealed class TelechargeurTests : IDisposable
{
    private static readonly byte[] Contenu = Encoding.UTF8.GetBytes("contenu du modèle");

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));

    private string Destination => Path.Combine(_dossier, "modele.gguf");

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private static FichierDistant Fichier(string sha256, long? taille = null, string source = "https://exemple.test/modele.gguf") =>
        new("modele.gguf", new Uri(source), taille ?? Contenu.Length, sha256);

    private static string Empreinte(byte[] octets) => Convert.ToHexStringLower(SHA256.HashData(octets));

    private static Telechargeur Creer(ServeurFichier serveur) => new(new HttpClient(serveur));

    [Fact]
    public async Task EmpreinteCorrecte_PlaceLeFichier()
    {
        var serveur = new ServeurFichier(Contenu);

        IResult<bool> resultat = await Creer(serveur).TelechargerAsync(Fichier(Empreinte(Contenu)), Destination, new Progress<long>(), CancellationToken.None);

        Assert.True(resultat.IsSuccess);
        Assert.Equal(Contenu, File.ReadAllBytes(Destination));
        Assert.False(File.Exists(Destination + ".partiel"));
        Assert.True(await Telechargeur.EstPresentAsync(Fichier(Empreinte(Contenu)), Destination, CancellationToken.None));
    }

    [Fact]
    public async Task EmpreinteFausse_NeLaisseAucunFichier()
    {
        var serveur = new ServeurFichier(Contenu);

        IResult<bool> resultat = await Creer(serveur).TelechargerAsync(Fichier(new string('0', 64)), Destination, new Progress<long>(), CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ".partiel"));
    }

    [Fact]
    public async Task FichierPlusGrosQuAnnonce_EstRefuse()
    {
        var serveur = new ServeurFichier(Contenu);

        IResult<bool> resultat = await Creer(serveur).TelechargerAsync(Fichier(Empreinte(Contenu), taille: 4), Destination, new Progress<long>(), CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task AdresseHttp_EstRefuseeSansRequete()
    {
        var serveur = new ServeurFichier(Contenu);

        IResult<bool> resultat = await Creer(serveur).TelechargerAsync(
            Fichier(Empreinte(Contenu), source: "http://exemple.test/modele.gguf"), Destination, new Progress<long>(), CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.Equal(0, serveur.Requetes);
    }

    [Fact]
    public async Task ErreurHttp_DevientUneErreur()
    {
        var serveur = new ServeurFichier(Contenu, HttpStatusCode.NotFound);

        IResult<bool> resultat = await Creer(serveur).TelechargerAsync(Fichier(Empreinte(Contenu)), Destination, new Progress<long>(), CancellationToken.None);

        Assert.False(resultat.IsSuccess);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task FichierLocalModifie_NEstPlusConsidereCommePresent()
    {
        Directory.CreateDirectory(_dossier);
        byte[] modifie = [.. Contenu];
        modifie[0] ^= 0xFF;
        File.WriteAllBytes(Destination, modifie);

        Assert.False(await Telechargeur.EstPresentAsync(Fichier(Empreinte(Contenu)), Destination, CancellationToken.None));
    }

    private sealed class ServeurFichier(byte[] contenu, HttpStatusCode statut = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requetes { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Requetes++;
            return Task.FromResult(new HttpResponseMessage(statut) { Content = new ByteArrayContent(contenu) });
        }
    }
}
