using System.Diagnostics;
using System.Globalization;
using Rotomeca.Core.Optionals;

namespace Xd.Local.Installation;

/// <summary>
/// Mémoire vidéo libre d'une carte NVIDIA, lue avec <c>nvidia-smi</c> (fourni par le pilote, sous Windows comme sous Linux).
/// Sans carte NVIDIA ou sans <c>nvidia-smi</c>, ne renvoie rien.
/// </summary>
public sealed class MesureNvidiaSmi : IMesureMemoireVideo
{
    private static readonly TimeSpan DelaiMax = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    public async Task<MayBe<long>> MesurerLibreAsync(CancellationToken annulation)
    {
        // Aucun shell (invariant 3).
        var demarrage = new ProcessStartInfo("nvidia-smi")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        demarrage.ArgumentList.Add("--query-gpu=memory.free");
        demarrage.ArgumentList.Add("--format=csv,noheader,nounits");

        try
        {
            using Process? processus = Process.Start(demarrage);
            if (processus is null)
                return MayBe<long>.Null;

            using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
            delai.CancelAfter(DelaiMax);
            string sortie = await processus.StandardOutput.ReadToEndAsync(delai.Token);
            await processus.WaitForExitAsync(delai.Token);

            return processus.ExitCode == 0 ? Lire(sortie) : MayBe<long>.Null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return MayBe<long>.Null;
        }
    }

    /// <summary>Lit la première carte : la sortie donne une ligne par carte, en Mio.</summary>
    internal static MayBe<long> Lire(string sortie)
    {
        string premiere = sortie.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return long.TryParse(premiere, NumberStyles.Integer, CultureInfo.InvariantCulture, out long mio) && mio >= 0
            ? mio * 1024 * 1024
            : MayBe<long>.Null;
    }
}
