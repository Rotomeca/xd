using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.EtatMachine;

/// <summary>
/// Disques fixes via <see cref="DriveInfo"/>, commun à Windows et Linux.
/// </summary>
internal static class LectureDisques
{
    // Systèmes de fichiers virtuels ou en lecture seule (Linux) : pas de l'espace disque au sens de l'utilisateur.
    private static readonly HashSet<string> FormatsIgnores = new(StringComparer.OrdinalIgnoreCase)
    {
        "squashfs", "overlay", "tmpfs", "devtmpfs", "ramfs", "iso9660",
    };

    public static IResult<IReadOnlyList<EspaceDisque>> Lire()
    {
        try
        {
            var disques = new List<EspaceDisque>();
            foreach (DriveInfo lecteur in DriveInfo.GetDrives())
            {
                if (Retenir(lecteur) is { } disque)
                    disques.Add(disque);
            }

            return Result.Ok<IReadOnlyList<EspaceDisque>>(disques);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<IReadOnlyList<EspaceDisque>>(new Error($"Lecture des disques impossible : {ex.Message}", ErrorType.Unexpected, exception: ex));
        }
    }

    private static EspaceDisque? Retenir(DriveInfo lecteur)
    {
        try
        {
            if (lecteur.DriveType != DriveType.Fixed || !lecteur.IsReady || FormatsIgnores.Contains(lecteur.DriveFormat) || lecteur.TotalSize <= 0)
                return null;

            return new EspaceDisque(lecteur.Name, lecteur.TotalSize, lecteur.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Lecteur devenu indisponible entre l'énumération et la lecture : ignoré.
            return null;
        }
    }
}
