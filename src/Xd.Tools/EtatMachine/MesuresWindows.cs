using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.EtatMachine;

/// <summary>
/// Métriques Windows via <c>kernel32</c> (<c>GetSystemTimes</c>, <c>GlobalMemoryStatusEx</c>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MesuresWindows : IMesuresMachine
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc/>
    public async Task<IResult<double>> MesurerProcesseurAsync(CancellationToken annulation)
    {
        if (!LireTemps(out ulong repos1, out ulong noyau1, out ulong utilisateur1))
            return Echec<double>("lecture des temps processeur impossible");

        try
        {
            await Task.Delay(Intervalle, annulation);
        }
        catch (OperationCanceledException)
        {
            return Echec<double>("mesure annulée");
        }

        if (!LireTemps(out ulong repos2, out ulong noyau2, out ulong utilisateur2))
            return Echec<double>("lecture des temps processeur impossible");

        // Le temps noyau inclut le temps de repos.
        double total = (noyau2 - noyau1) + (utilisateur2 - utilisateur1);
        double repos = repos2 - repos1;
        return Result.Ok(total > 0 ? (1 - (repos / total)) * 100 : 0);
    }

    /// <inheritdoc/>
    public IResult<MemoireMachine> MesurerMemoire()
    {
        var etat = new EtatMemoire { Longueur = (uint)Marshal.SizeOf<EtatMemoire>() };
        return GlobalMemoryStatusEx(ref etat)
            ? Result.Ok(new MemoireMachine((long)etat.PhysiqueTotale, (long)etat.PhysiqueDisponible))
            : Echec<MemoireMachine>("lecture de la mémoire impossible");
    }

    /// <inheritdoc/>
    public IResult<IReadOnlyList<EspaceDisque>> MesurerDisques() => LectureDisques.Lire();

    private static bool LireTemps(out ulong repos, out ulong noyau, out ulong utilisateur)
    {
        bool lu = GetSystemTimes(out TempsFichier r, out TempsFichier n, out TempsFichier u);
        repos = r.EnEntier;
        noyau = n.EnEntier;
        utilisateur = u.EnEntier;
        return lu;
    }

    private static IResult<T> Echec<T>(string detail) =>
        Result.Fail<T>(new Error($"Mesure impossible : {detail} (code {Marshal.GetLastPInvokeError()}).", ErrorType.Unexpected));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out TempsFichier repos, out TempsFichier noyau, out TempsFichier utilisateur);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref EtatMemoire etat);

    [StructLayout(LayoutKind.Sequential)]
    private struct TempsFichier
    {
        public uint Bas;
        public uint Haut;

        public readonly ulong EnEntier => ((ulong)Haut << 32) | Bas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EtatMemoire
    {
        public uint Longueur;
        public uint Charge;
        public ulong PhysiqueTotale;
        public ulong PhysiqueDisponible;
        public ulong FichierEchangeTotal;
        public ulong FichierEchangeDisponible;
        public ulong VirtuelleTotale;
        public ulong VirtuelleDisponible;
        public ulong VirtuelleEtendueDisponible;
    }
}
