using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;
using Xd.Tools.EtatMachine;

namespace Xd.Tools.Tests.EtatMachine;

public sealed class OutilEtatMachineTests
{
    private const long Go = 1024L * 1024 * 1024;

    private readonly MesuresFactices _mesures = new();
    private readonly ConteneursFactices _conteneurs = new();

    private OutilEtatMachine Outil() => new(_mesures, _conteneurs);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<string> Executer(string parametres)
    {
        IActionPreparee action = Outil().Preparer(Json(parametres)).Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));
        IResult<string> resultat = await action.ExecuterAsync(CancellationToken.None);
        return resultat.Match(Ok: texte => texte, Err: e => "ERREUR " + e.Message);
    }

    [Fact]
    public void Definition_EstLibreAvecUneEnumerationFermee()
    {
        OutilEtatMachine outil = Outil();

        Assert.Equal("etat_machine", outil.Nom);
        Assert.Equal(NiveauOutil.Libre, outil.Niveau);
        JsonElement aspect = outil.SchemaParametres.GetProperty("properties").GetProperty("aspect");
        Assert.Equal(["cpu", "ram", "disque", "conteneurs"], aspect.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.False(outil.SchemaParametres.GetProperty("additionalProperties").GetBoolean());
    }

    [Theory]
    [InlineData("""{"aspect":"gpu"}""")]
    [InlineData("""{"aspect":"CPU"}""")]
    [InlineData("""{"aspect":" cpu"}""")]
    [InlineData("""{"aspect":""}""")]
    public void ValeurHorsEnumeration_EstRefusee(string parametres)
    {
        IResult<IActionPreparee> resultat = Outil().Preparer(Json(parametres));

        Assert.False(resultat.IsSuccess);
        Assert.Contains("cpu, ram, disque, conteneurs", resultat.Match(Ok: _ => string.Empty, Err: e => e.Message));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"aspect":3}""")]
    [InlineData("""{"aspect":null}""")]
    [InlineData("""{"aspect":"cpu","detail":true}""")]
    [InlineData("""["cpu"]""")]
    [InlineData("\"cpu\"")]
    public void ParametresMalFormes_SontRefuses(string parametres)
    {
        Assert.False(Outil().Preparer(Json(parametres)).IsSuccess);
    }

    [Fact]
    public void Preparer_NeMesureRien()
    {
        Outil().Preparer(Json("""{"aspect":"cpu"}"""));

        Assert.Equal(0, _mesures.Appels);
    }

    [Theory]
    [InlineData("cpu", "Je regarde la charge du processeur.")]
    [InlineData("conteneurs", "Je regarde les conteneurs en marche.")]
    public void Reformulation_DecritLAspectValide(string aspect, string attendue)
    {
        IActionPreparee action = Outil().Preparer(Json($$"""{"aspect":"{{aspect}}"}""")).Match(Ok: a => a, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

        Assert.Equal(attendue, action.Reformulation);
    }

    [Fact]
    public async Task Cpu_DonneLaChargeArrondie()
    {
        _mesures.Processeur = Result.Ok(23.4);

        Assert.Equal("Processeur utilisé à 23 %.", await Executer("""{"aspect":"cpu"}"""));
    }

    [Fact]
    public async Task Ram_DonneUtiliseeTotaleEtPourcentage()
    {
        _mesures.Memoire = Result.Ok(new MemoireMachine(32 * Go, 20 * Go));

        Assert.Equal("Mémoire vive : 12 Go utilisés sur 32 Go (38 %).", await Executer("""{"aspect":"ram"}"""));
    }

    [Fact]
    public async Task Disque_ResumeChaqueDisque()
    {
        _mesures.Disques = Result.Ok<IReadOnlyList<EspaceDisque>>([new EspaceDisque(@"C:\", 930 * Go, 120 * Go), new EspaceDisque(@"D:\", 2000 * Go, 1500 * Go)]);

        Assert.Equal(@"Disque C:\ : 120 Go libres sur 930 Go. Disque D:\ : 1500 Go libres sur 2000 Go.", await Executer("""{"aspect":"disque"}"""));
    }

    [Theory]
    [InlineData(0, "Aucun conteneur en marche.")]
    [InlineData(1, "1 conteneur en marche : web (nginx).")]
    [InlineData(2, "2 conteneurs en marche : web (nginx), base (postgres).")]
    public async Task Conteneurs_SontResumes(int nombre, string attendu)
    {
        Conteneur[] tous = [new("web", "nginx", "Up 2 hours"), new("base", "postgres", "Up 1 hour")];
        _conteneurs.Resultat = Result.Ok<IReadOnlyList<Conteneur>>(tous[..nombre]);

        Assert.Equal(attendu, await Executer("""{"aspect":"conteneurs"}"""));
    }

    [Fact]
    public async Task MesureEnEchec_RenvoieUneErreur()
    {
        _conteneurs.Resultat = Result.Fail<IReadOnlyList<Conteneur>>(new Error("Docker n'est pas accessible.", ErrorType.Unexpected));

        Assert.StartsWith("ERREUR Docker", await Executer("""{"aspect":"conteneurs"}"""));
    }

    private sealed class MesuresFactices : IMesuresMachine
    {
        public int Appels { get; private set; }
        public IResult<double> Processeur { get; set; } = Result.Ok(0d);
        public IResult<MemoireMachine> Memoire { get; set; } = Result.Ok(new MemoireMachine(1, 1));
        public IResult<IReadOnlyList<EspaceDisque>> Disques { get; set; } = Result.Ok<IReadOnlyList<EspaceDisque>>([]);

        public Task<IResult<double>> MesurerProcesseurAsync(CancellationToken annulation)
        {
            Appels++;
            return Task.FromResult(Processeur);
        }

        public IResult<MemoireMachine> MesurerMemoire()
        {
            Appels++;
            return Memoire;
        }

        public IResult<IReadOnlyList<EspaceDisque>> MesurerDisques()
        {
            Appels++;
            return Disques;
        }
    }

    private sealed class ConteneursFactices : ISourceConteneurs
    {
        public IResult<IReadOnlyList<Conteneur>> Resultat { get; set; } = Result.Ok<IReadOnlyList<Conteneur>>([]);

        public Task<IResult<IReadOnlyList<Conteneur>>> ListerAsync(CancellationToken annulation) => Task.FromResult(Resultat);
    }
}
