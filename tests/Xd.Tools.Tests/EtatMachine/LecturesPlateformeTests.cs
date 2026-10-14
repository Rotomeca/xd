using System.Text.Json;
using Rotomeca.Core.Optionals;
using Rotomeca.Rop.Interfaces;
using Xd.Tools.EtatMachine;

namespace Xd.Tools.Tests.EtatMachine;

public sealed class LecturesPlateformeTests
{
    [Fact]
    public void ProcStat_DonneLaChargeEntreDeuxLectures()
    {
        MayBe<LectureProc.TempsProcesseur> avant = LectureProc.LireStat("cpu  100 0 100 700 100 0 0 0 0 0\ncpu0 50 0 50 350 50 0 0 0 0 0\n");
        MayBe<LectureProc.TempsProcesseur> apres = LectureProc.LireStat("cpu  200 0 200 1300 100 0 0 0 0 0\n");

        Assert.True(avant.Value is { } a && apres.Value is { } b && Math.Abs(LectureProc.Charge(a, b) - 25) < 0.001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("intr 12 34\n")]
    [InlineData("cpu  a b c d e\n")]
    public void ProcStatIllisible_NeDonneRien(string contenu)
    {
        Assert.True(LectureProc.LireStat(contenu).IsEmpty);
    }

    [Fact]
    public void ProcMeminfo_DonneTotaleEtDisponible()
    {
        MayBe<MemoireMachine> memoire = LectureProc.LireMeminfo("MemTotal:       32768000 kB\nMemFree:  1000 kB\nMemAvailable:   16384000 kB\n");

        Assert.Equal(new MemoireMachine(32768000L * 1024, 16384000L * 1024), memoire.Value);
    }

    [Fact]
    public void ProcMeminfoSansMemAvailable_NeDonneRien()
    {
        Assert.True(LectureProc.LireMeminfo("MemTotal: 1000 kB\n").IsEmpty);
    }

    [Fact]
    public void ReponseDocker_EstLue()
    {
        JsonElement corps = JsonDocument.Parse("""
            [
              { "Names": ["/web"], "Image": "nginx:1.27", "State": "running", "Status": "Up 2 hours" },
              { "Image": "postgres" }
            ]
            """).RootElement;

        IReadOnlyList<Conteneur> conteneurs = ConteneursDocker.Lire(corps);

        Assert.Equal([new Conteneur("web", "nginx:1.27", "Up 2 hours"), new Conteneur("?", "postgres", "?")], conteneurs);
    }

    [Fact]
    public async Task SurWindows_LesMesuresReellesAboutissent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var mesures = new MesuresWindows();

        IResult<double> processeur = await mesures.MesurerProcesseurAsync(CancellationToken.None);
        Assert.True(processeur.IsSuccess);
        Assert.InRange(processeur.Match(Ok: c => c, Err: _ => -1), 0, 100);

        IResult<MemoireMachine> memoire = mesures.MesurerMemoire();
        Assert.True(memoire.Match(Ok: m => m.Totale > 0 && m.Disponible <= m.Totale, Err: _ => false));

        Assert.True(mesures.MesurerDisques().Match(Ok: d => d.Count > 0, Err: _ => false));
    }
}
