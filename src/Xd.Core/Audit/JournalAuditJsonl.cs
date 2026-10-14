using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Core.Audit;

/// <summary>
/// Journal d'audit écrit en JSONL : une entrée JSON par ligne, ajoutée en fin de fichier.
/// </summary>
public sealed class JournalAuditJsonl : IJournalAudit, IDisposable
{
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _cheminFichier;
    private readonly SemaphoreSlim _verrou = new(1, 1);

    /// <param name="cheminFichier">Chemin du fichier JSONL, issu de la configuration.</param>
    public JournalAuditJsonl(string cheminFichier)
    {
        _cheminFichier = cheminFichier;
    }

    /// <inheritdoc/>
    public async Task<IResult<bool>> EcrireAsync(EntreeAudit entree, CancellationToken annulation)
    {
        string ligne = JsonSerializer.Serialize(entree, OptionsJson) + Environment.NewLine;

        await _verrou.WaitAsync(annulation);
        try
        {
            string? dossier = Path.GetDirectoryName(_cheminFichier);
            if (!string.IsNullOrEmpty(dossier))
                Directory.CreateDirectory(dossier);

            await File.AppendAllTextAsync(_cheminFichier, ligne, annulation);
            return Result.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<bool>(new Error($"Écriture du journal d'audit impossible : {ex.Message}", ErrorType.Unexpected, exception: ex));
        }
        finally
        {
            _verrou.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _verrou.Dispose();
}
