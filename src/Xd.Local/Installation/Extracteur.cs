using System.Formats.Tar;
using System.IO.Compression;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Local.Installation;

/// <summary>
/// Extraction des archives de llama.cpp (<c>.zip</c> sous Windows, <c>.tar.gz</c> sous Linux).
/// Toute entrée qui sortirait du dossier cible fait échouer l'extraction (« zip slip »).
/// </summary>
public static class Extracteur
{
    /// <summary>Extrait l'archive dans <paramref name="dossier"/>, vidé au préalable.</summary>
    public static async Task<IResult<bool>> ExtraireAsync(string archive, string dossier, CancellationToken annulation)
    {
        try
        {
            if (Directory.Exists(dossier))
                Directory.Delete(dossier, recursive: true);
            Directory.CreateDirectory(dossier);
            string racine = Path.GetFullPath(dossier);

            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ExtraireZip(archive, racine);
            else if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                await ExtraireTarGzAsync(archive, racine, annulation);
            else
                return Echec(archive, "format d'archive non pris en charge.");

            return Result.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
        {
            return Echec(archive, ex.Message, ex);
        }
    }

    /// <summary>Cherche un exécutable dans le dossier extrait, quel que soit le sous-dossier.</summary>
    public static IResult<string> TrouverExecutable(string dossier, string nom)
    {
        string nomFichier = OperatingSystem.IsWindows() ? nom + ".exe" : nom;
        string? chemin = Directory.Exists(dossier)
            ? Directory.EnumerateFiles(dossier, nomFichier, SearchOption.AllDirectories).FirstOrDefault()
            : null;
        return chemin is null
            ? Result.Fail<string>(new Error($"{nomFichier} introuvable dans {dossier}.", ErrorType.NotFound))
            : Result.Ok(chemin);
    }

    private static void ExtraireZip(string archive, string racine)
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (ZipArchiveEntry entree in zip.Entries)
        {
            string cible = CheminSur(racine, entree.FullName);
            if (entree.FullName.EndsWith('/') || entree.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(cible);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cible) ?? racine);
            entree.ExtractToFile(cible, overwrite: false);
        }
    }

    private static async Task ExtraireTarGzAsync(string archive, string racine, CancellationToken annulation)
    {
        await using FileStream fichier = File.OpenRead(archive);
        await using var decompression = new GZipStream(fichier, CompressionMode.Decompress);
        await using var tar = new TarReader(decompression);

        while (await tar.GetNextEntryAsync(copyData: false, annulation) is { } entree)
        {
            string cible = CheminSur(racine, entree.Name);
            switch (entree.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(cible);
                    break;

                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(cible) ?? racine);
                    await entree.ExtractToFileAsync(cible, overwrite: false, annulation);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(cible, entree.Mode);
                    break;

                case TarEntryType.SymbolicLink:
                    // Liens des bibliothèques partagées (libllama.so -> libllama.so.0) : la cible doit rester dans le dossier.
                    CheminSur(racine, Path.Combine(Path.GetDirectoryName(entree.Name) ?? string.Empty, entree.LinkName));
                    Directory.CreateDirectory(Path.GetDirectoryName(cible) ?? racine);
                    File.CreateSymbolicLink(cible, entree.LinkName);
                    break;

                default:
                    throw new InvalidDataException($"type d'entrée refusé dans l'archive : {entree.EntryType} ({entree.Name}).");
            }
        }
    }

    /// <summary>Chemin absolu d'une entrée, garanti à l'intérieur de <paramref name="racine"/>.</summary>
    private static string CheminSur(string racine, string entree)
    {
        string cible = Path.GetFullPath(Path.Combine(racine, entree));
        string prefixe = racine.EndsWith(Path.DirectorySeparatorChar) ? racine : racine + Path.DirectorySeparatorChar;
        if (!cible.StartsWith(prefixe, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && cible != racine)
            throw new InvalidDataException($"entrée hors du dossier d'extraction refusée : {entree}.");

        return cible;
    }

    private static IResult<bool> Echec(string archive, string detail, Exception? exception = null) =>
        Result.Fail<bool>(new Error($"Extraction de {Path.GetFileName(archive)} impossible : {detail}", ErrorType.Unexpected, exception: exception));
}
