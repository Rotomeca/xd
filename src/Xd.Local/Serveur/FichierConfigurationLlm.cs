using System.Text.Json;
using System.Text.Json.Serialization;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Local.Serveur;

/// <summary>
/// Lecture et écriture de <see cref="ConfigurationLlm"/> en JSON.
/// </summary>
public static class FichierConfigurationLlm
{
    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Lit la configuration.</summary>
    /// <returns>La configuration, ou une erreur si le fichier est absent ou illisible.</returns>
    public static IResult<ConfigurationLlm> Lire(string chemin)
    {
        if (!File.Exists(chemin))
            return Result.Fail<ConfigurationLlm>(new Error($"LLM non configuré ({chemin} absent). Lance d'abord : xd setup llm", ErrorType.NotFound));

        try
        {
            ConfigurationLlm? configuration = JsonSerializer.Deserialize<ConfigurationLlm>(File.ReadAllText(chemin), OptionsJson);
            return configuration is null
                ? Result.Fail<ConfigurationLlm>(new Error($"Configuration du LLM vide : {chemin}.", ErrorType.Validation))
                : Result.Ok(configuration);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Result.Fail<ConfigurationLlm>(new Error($"Configuration du LLM illisible ({chemin}) : {ex.Message}", ErrorType.Validation, exception: ex));
        }
    }

    /// <summary>Écrit la configuration, en créant le dossier si besoin.</summary>
    public static IResult<bool> Ecrire(string chemin, ConfigurationLlm configuration)
    {
        try
        {
            string? dossier = Path.GetDirectoryName(chemin);
            if (!string.IsNullOrEmpty(dossier))
                Directory.CreateDirectory(dossier);

            File.WriteAllText(chemin, JsonSerializer.Serialize(configuration, OptionsJson));
            return Result.Ok(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Fail<bool>(new Error($"Écriture de la configuration du LLM impossible ({chemin}) : {ex.Message}", ErrorType.Unexpected, exception: ex));
        }
    }
}
