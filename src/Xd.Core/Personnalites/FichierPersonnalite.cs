using System.Text.Json;
using System.Text.Json.Serialization;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Core.Personnalites;

/// <summary>
/// Lecture de <c>personnalite.json</c>. Fichier absent : <see cref="Personnalite.ParDefaut"/>.
/// </summary>
public static class FichierPersonnalite
{
    private const int LongueurTexteMax = 30;

    /// <summary>Longueur maximale des instructions libres (elles entrent dans chaque prompt).</summary>
    public const int LongueurInstructionsMax = 1500;

    private static readonly JsonSerializerOptions OptionsJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Lit et valide la personnalité.</summary>
    /// <returns>La personnalité, ou une erreur si le fichier est illisible ou contient une valeur invalide.</returns>
    public static IResult<Personnalite> Lire(string chemin)
    {
        if (!File.Exists(chemin))
            return Result.Ok(Personnalite.ParDefaut);

        try
        {
            Personnalite? lue = JsonSerializer.Deserialize<Personnalite>(File.ReadAllText(chemin), OptionsJson);
            return lue is null ? Result.Ok(Personnalite.ParDefaut) : Valider(lue, chemin);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Echec($"{chemin} est illisible : {ex.Message}", ex);
        }
    }

    // Ces textes entrent dans le prompt système : courts, sur une ligne, sans caractère de contrôle.
    private static IResult<Personnalite> Valider(Personnalite personnalite, string chemin) => personnalite switch
    {
        { Nom: var nom } when !TexteValide(nom) => Echec($"{chemin} : « nom » doit faire 1 à {LongueurTexteMax} caractères."),
        { Langue: var langue } when !TexteValide(langue) => Echec($"{chemin} : « langue » doit faire 1 à {LongueurTexteMax} caractères."),
        { PrenomUtilisateur: { } prenom } when !TexteValide(prenom) =>
            Echec($"{chemin} : « prenomUtilisateur » doit faire 1 à {LongueurTexteMax} caractères."),
        { Ton: var ton } when !Enum.IsDefined(ton) => Echec($"{chemin} : ton inconnu."),
        { Instructions: { } instructions } when !InstructionsValides(instructions) =>
            Echec($"{chemin} : « instructions » doit faire au plus {LongueurInstructionsMax} caractères, sans caractère de contrôle autre qu'un retour à la ligne."),
        _ => Result.Ok(personnalite),
    };

    private static bool TexteValide(string? texte) =>
        texte is { Length: > 0 and <= LongueurTexteMax } && texte.Trim() == texte && !texte.Any(char.IsControl);

    private static bool InstructionsValides(string instructions) =>
        instructions.Length <= LongueurInstructionsMax && !instructions.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t');

    private static IResult<Personnalite> Echec(string message, Exception? exception = null) =>
        Result.Fail<Personnalite>(new Error(message, ErrorType.Validation, exception: exception));
}
