using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Xd.Local.Installation;

namespace Xd.Local.Tests.Installation;

public sealed class ExtracteurTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));

    public ExtracteurTests()
    {
        Directory.CreateDirectory(_dossier);
    }

    private string Cible => Path.Combine(_dossier, "cible");

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private string Zip(params string[] entrees)
    {
        string chemin = Path.Combine(_dossier, "archive.zip");
        using ZipArchive zip = ZipFile.Open(chemin, ZipArchiveMode.Create);
        foreach (string entree in entrees)
        {
            using var ecriture = new StreamWriter(zip.CreateEntry(entree).Open());
            ecriture.Write("contenu");
        }

        return chemin;
    }

    private string TarGz(Action<TarWriter> remplir)
    {
        string chemin = Path.Combine(_dossier, "archive.tar.gz");
        using (FileStream fichier = File.Create(chemin))
        using (var compression = new GZipStream(fichier, CompressionLevel.Fastest))
        using (var tar = new TarWriter(compression))
            remplir(tar);

        return chemin;
    }

    private static PaxTarEntry Fichier(string nom) =>
        new(TarEntryType.RegularFile, nom) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("contenu")) };

    [Fact]
    public async Task Zip_EstExtraitEtLExecutableTrouve()
    {
        string executable = OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";

        Assert.True((await Extracteur.ExtraireAsync(Zip($"build/bin/{executable}", "build/bin/ggml.dll"), Cible, CancellationToken.None)).IsSuccess);

        string trouve = Extracteur.TrouverExecutable(Cible, "llama-server").Match(Ok: c => c, Err: e => e.Message);
        Assert.Equal(Path.Combine(Cible, "build", "bin", executable), trouve);
    }

    [Fact]
    public async Task ZipAvecEntreeHorsDuDossier_EstRefuse()
    {
        Assert.False((await Extracteur.ExtraireAsync(Zip("ok.txt", "../evade.txt"), Cible, CancellationToken.None)).IsSuccess);

        Assert.False(File.Exists(Path.Combine(_dossier, "evade.txt")));
    }

    [Fact]
    public async Task TarGz_EstExtrait()
    {
        string archive = TarGz(tar => tar.WriteEntry(Fichier("llama-b11429/llama-server")));

        Assert.True((await Extracteur.ExtraireAsync(archive, Cible, CancellationToken.None)).IsSuccess);

        Assert.True(File.Exists(Path.Combine(Cible, "llama-b11429", "llama-server")));
    }

    [Fact]
    public async Task TarGzAvecEntreeHorsDuDossier_EstRefuse()
    {
        string archive = TarGz(tar => tar.WriteEntry(Fichier("../evade.txt")));

        Assert.False((await Extracteur.ExtraireAsync(archive, Cible, CancellationToken.None)).IsSuccess);

        Assert.False(File.Exists(Path.Combine(_dossier, "evade.txt")));
    }

    [Fact]
    public async Task TarGzAvecLienVersLExterieur_EstRefuse()
    {
        string archive = TarGz(tar => tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "lib/libllama.so") { LinkName = "../../secret" }));

        Assert.False((await Extracteur.ExtraireAsync(archive, Cible, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task FormatInconnu_EstRefuse()
    {
        string archive = Path.Combine(_dossier, "archive.rar");
        File.WriteAllText(archive, "?");

        Assert.False((await Extracteur.ExtraireAsync(archive, Cible, CancellationToken.None)).IsSuccess);
    }
}
