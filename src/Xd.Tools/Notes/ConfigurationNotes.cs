using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.Notes;

/// <summary>
/// Configuration des notes, dans le dossier de configuration utilisateur (jamais dans le dépôt).
/// </summary>
/// <param name="DossierNotes">Dossier central des notes ; <see langword="null"/> tant que l'utilisateur ne l'a pas indiqué.</param>
public sealed record ConfigurationNotes(string? DossierNotes)
{
    /// <summary>Configuration vide : rien n'a encore été indiqué.</summary>
    public static ConfigurationNotes Vide { get; } = new((string?)null);
}

/// <summary>
/// Lecture et écriture de <see cref="ConfigurationNotes"/>.
/// </summary>
public interface IStockageConfigurationNotes
{
    /// <summary>Configuration actuelle ; vide si rien n'a encore été indiqué.</summary>
    IResult<ConfigurationNotes> Lire();

    /// <summary>Enregistre la configuration.</summary>
    IResult<bool> Ecrire(ConfigurationNotes configuration);
}

/// <summary>
/// <see cref="ConfigurationNotes"/> en JSON (<c>notes.json</c>).
/// </summary>
public sealed class FichierConfigurationNotes : IStockageConfigurationNotes
{
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _chemin;

    /// <param name="chemin">Chemin de <c>notes.json</c>, issu des emplacements de la plateforme.</param>
    public FichierConfigurationNotes(string chemin)
    {
        _chemin = chemin;
    }

    /// <inheritdoc/>
    public IResult<ConfigurationNotes> Lire()
    {
        if (!File.Exists(_chemin))
            return Result.Ok(ConfigurationNotes.Vide);

        try
        {
            ConfigurationNotes? configuration = JsonSerializer.Deserialize<ConfigurationNotes>(File.ReadAllText(_chemin), OptionsJson);
            return Result.Ok(configuration ?? ConfigurationNotes.Vide);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Result.Fail<ConfigurationNotes>(new Error($"Configuration des notes illisible ({_chemin}) : {ex.Message}", ErrorType.Validation, exception: ex));
        }
    }

    /// <inheritdoc/>
    public IResult<bool> Ecrire(ConfigurationNotes configuration)
    {
        try
        {
            string? dossier = Path.GetDirectoryName(_chemin);
            if (!string.IsNullOrEmpty(dossier))
                Directory.CreateDirectory(dossier);

            File.WriteAllText(_chemin, JsonSerializer.Serialize(configuration, OptionsJson));
            return Result.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<bool>(new Error($"Écriture de la configuration des notes impossible ({_chemin}) : {ex.Message}", ErrorType.Unexpected, exception: ex));
        }
    }
}
