using System.Runtime.Versioning;
using Rotomeca.Core.Optionals;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.EtatMachine;

/// <summary>
/// Métriques Linux lues dans <c>/proc/stat</c> et <c>/proc/meminfo</c>.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class MesuresLinux : IMesuresMachine
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public async Task<IResult<double>> MesurerProcesseurAsync(CancellationToken annulation)
    {
        try
        {
            MayBe<LectureProc.TempsProcesseur> premier = LectureProc.LireStat(await File.ReadAllTextAsync("/proc/stat", annulation));
            await Task.Delay(Intervalle, annulation);
            MayBe<LectureProc.TempsProcesseur> second = LectureProc.LireStat(await File.ReadAllTextAsync("/proc/stat", annulation));

            return premier.Value is { } avant && second.Value is { } apres
                ? Result.Ok(LectureProc.Charge(avant, apres))
                : Echec<double>("format de /proc/stat inattendu");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Echec<double>(ex.Message);
        }
    }

    /// <inheritdoc/>
    public IResult<MemoireMachine> MesurerMemoire()
    {
        try
        {
            MayBe<MemoireMachine> memoire = LectureProc.LireMeminfo(File.ReadAllText("/proc/meminfo"));
            return memoire.HasValue && memoire.Value is { } valeur
                ? Result.Ok(valeur)
                : Echec<MemoireMachine>("format de /proc/meminfo inattendu");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Echec<MemoireMachine>(ex.Message);
        }
    }

    /// <inheritdoc/>
    public IResult<IReadOnlyList<EspaceDisque>> MesurerDisques() => LectureDisques.Lire();

    private static IResult<T> Echec<T>(string detail) =>
        Result.Fail<T>(new Error($"Mesure impossible : {detail}.", ErrorType.Unexpected));
}
