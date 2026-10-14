using System.Security.Cryptography;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Local.Installation;

/// <summary>
/// Téléchargement HTTPS vérifié par SHA-256. Un fichier n'apparaît à sa place définitive qu'une fois son empreinte vérifiée.
/// </summary>
public sealed class Telechargeur
{
    private const int TailleTampon = 1024 * 1024;

    private readonly HttpClient _http;

    /// <param name="http">Client sans délai global : les modèles pèsent plusieurs gigaoctets.</param>
    public Telechargeur(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Le fichier est-il déjà présent avec la bonne empreinte ?</summary>
    public static async Task<bool> EstPresentAsync(FichierDistant fichier, string destination, CancellationToken annulation)
    {
        if (!File.Exists(destination) || new FileInfo(destination).Length != fichier.Taille)
            return false;

        await using FileStream flux = File.OpenRead(destination);
        byte[] empreinte = await SHA256.HashDataAsync(flux, annulation);
        return Convert.ToHexStringLower(empreinte) == fichier.Sha256;
    }

    /// <summary>Télécharge le fichier vers <paramref name="destination"/> et vérifie taille et empreinte.</summary>
    /// <param name="fichier">Fichier épinglé.</param>
    /// <param name="destination">Chemin final.</param>
    /// <param name="progression">Reçoit le nombre d'octets reçus.</param>
    /// <param name="annulation">Annule le téléchargement ; le fichier partiel est supprimé.</param>
    public async Task<IResult<bool>> TelechargerAsync(FichierDistant fichier, string destination, IProgress<long> progression, CancellationToken annulation)
    {
        if (fichier.Source.Scheme != Uri.UriSchemeHttps)
            return Echec(fichier, "seules les adresses HTTPS sont acceptées.");

        string partiel = destination + ".partiel";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
            string empreinte = await RecevoirAsync(fichier, partiel, progression, annulation);

            if (new FileInfo(partiel).Length != fichier.Taille)
                return Supprimer(partiel, Echec(fichier, $"taille inattendue ({new FileInfo(partiel).Length} octets au lieu de {fichier.Taille})."));
            if (empreinte != fichier.Sha256)
                return Supprimer(partiel, Echec(fichier, $"empreinte SHA-256 inattendue ({empreinte}). Fichier supprimé."));

            File.Move(partiel, destination, overwrite: true);
            return Result.Ok(true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return Supprimer(partiel, Echec(fichier, ex.Message, ex));
        }
    }

    private async Task<string> RecevoirAsync(FichierDistant fichier, string partiel, IProgress<long> progression, CancellationToken annulation)
    {
        using HttpResponseMessage reponse = await _http.GetAsync(fichier.Source, HttpCompletionOption.ResponseHeadersRead, annulation);
        reponse.EnsureSuccessStatusCode();

        await using Stream source = await reponse.Content.ReadAsStreamAsync(annulation);
        await using var cible = new FileStream(partiel, FileMode.Create, FileAccess.Write, FileShare.None, TailleTampon, useAsync: true);
        using var hachage = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] tampon = new byte[TailleTampon];
        long recus = 0;
        int lus;
        while ((lus = await source.ReadAsync(tampon, annulation)) > 0)
        {
            // Au-delà de la taille annoncée, inutile de continuer.
            if (recus + lus > fichier.Taille)
                throw new IOException($"le fichier dépasse la taille attendue ({fichier.Taille} octets).");

            hachage.AppendData(tampon, 0, lus);
            await cible.WriteAsync(tampon.AsMemory(0, lus), annulation);
            recus += lus;
            progression.Report(recus);
        }

        return Convert.ToHexStringLower(hachage.GetHashAndReset());
    }

    private static IResult<bool> Supprimer(string partiel, IResult<bool> resultat)
    {
        try
        {
            File.Delete(partiel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Le fichier partiel ne sera jamais utilisé : il porte une autre extension.
        }

        return resultat;
    }

    private static IResult<bool> Echec(FichierDistant fichier, string detail, Exception? exception = null) =>
        Result.Fail<bool>(new Error($"Téléchargement de {fichier.Nom} impossible : {detail}", ErrorType.Unexpected, exception: exception));
}
