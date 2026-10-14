using System.Globalization;
using System.Text;

namespace Xd.Core.Permissions;

/// <summary>Sens d'une réponse à une question de confirmation.</summary>
public enum ReponseConfirmation
{
    /// <summary>Oui explicite.</summary>
    Oui,

    /// <summary>Non explicite.</summary>
    Non,

    /// <summary>Ni oui ni non francs : une réponse libre (« oui mais dans Xd »), rendue au modèle comme une nouvelle consigne.</summary>
    Autre,
}

/// <summary>
/// Interprétation déterministe d'une réponse de confirmation, sans le modèle : la confirmation est un garde-fou,
/// le modèle ne doit jamais pouvoir se valider lui-même.
/// </summary>
/// <remarks>
/// Oui (ou non) seulement si la réponse ne contient <b>que</b> des formules d'accord (ou de refus) et de politesse.
/// Tout le reste (« oui mais dans Xd », « non, mets-le dans Xd », « euh ») est une réponse libre.
/// </remarks>
public static class InterpretationConfirmation
{
    private static readonly string[] FormulesOui =
    [
        "oui", "o", "ouais", "ouaip", "yes", "y", "yep", "ok", "okay", "okey", "go", "vas y", "allez", "allez y",
        "d accord", "dac", "bien sur", "carrement", "exactement", "c est ca", "c est bon", "parfait", "confirme", "je confirme",
        "valide", "je valide", "absolument", "tout a fait", "evidemment",
    ];

    private static readonly string[] FormulesNon =
    [
        "non", "n", "nan", "nope", "no", "annule", "annuler", "stop", "laisse tomber", "surtout pas", "pas du tout",
        "pas maintenant", "pas question", "jamais", "refuse", "je refuse",
    ];

    private static readonly string[] Politesse = ["merci", "stp", "svp", "s il te plait", "s il vous plait", "please", "alors", "bah", "ben"];

    /// <summary>Interprète la réponse de l'utilisateur.</summary>
    public static ReponseConfirmation Interpreter(string? reponse)
    {
        string normalisee = Normaliser(reponse ?? string.Empty);
        if (normalisee.Length == 0)
            return ReponseConfirmation.Autre;

        bool oui = Contient(normalisee, FormulesOui);
        bool non = Contient(normalisee, FormulesNon);

        return (oui, non) switch
        {
            (true, false) when Retirer(normalisee, [.. FormulesOui, .. Politesse]).Length == 0 => ReponseConfirmation.Oui,
            (false, true) when Retirer(normalisee, [.. FormulesNon, .. Politesse]).Length == 0 => ReponseConfirmation.Non,
            _ => ReponseConfirmation.Autre,
        };
    }

    private static bool Contient(string texte, string[] formules) =>
        formules.Any(f => $" {texte} ".Contains($" {f} ", StringComparison.Ordinal));

    /// <summary>Retire les formules connues, des plus longues aux plus courtes ; il ne doit rien rester pour un oui franc.</summary>
    private static string Retirer(string texte, string[] formules)
    {
        string reste = $" {texte} ";
        foreach (string formule in formules.OrderByDescending(f => f.Length))
        {
            while (reste.Contains($" {formule} ", StringComparison.Ordinal))
                reste = reste.Replace($" {formule} ", " ", StringComparison.Ordinal);
        }

        return reste.Trim();
    }

    /// <summary>Minuscules, sans accents, ponctuation et apostrophes remplacées par des espaces, espaces simplifiés.</summary>
    private static string Normaliser(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char caractere in texte.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory categorie = CharUnicodeInfo.GetUnicodeCategory(caractere);
            if (categorie == UnicodeCategory.NonSpacingMark)
                continue;

            resultat.Append(char.IsLetterOrDigit(caractere) ? caractere : ' ');
        }

        return string.Join(' ', resultat.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
