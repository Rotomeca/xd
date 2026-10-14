using System.Globalization;
using System.Text;
using Rotomeca.Core.Optionals;

namespace Xd.Tools.Notes;

/// <summary>
/// Carnets de notes : <c>general</c> (notes générales) et un fichier par carnet de projet, créé à la demande.
/// </summary>
/// <remarks>
/// Fichiers : <c>{dossier}/general.md</c> et <c>{dossier}/projets/{carnet}.md</c>. Les paroles peuvent venir d'une saisie
/// ou d'une transcription vocale : les comparaisons ignorent casse, accents, ponctuation et espaces (« x d » vaut « Xd »).
/// </remarks>
public static class CatalogueCarnets
{
    /// <summary>Carnet des notes générales.</summary>
    public const string General = "general";

    private const int LongueurNomMax = 60;

    private static readonly string[] FormulationsGenerales = ["general", "generale", "generales", "sans projet", "aucun projet", "divers"];

    private static readonly HashSet<string> NomsReservesWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Fichier d'un carnet.</summary>
    public static string Fichier(string dossierNotes, string carnet) => carnet == General
        ? Path.Combine(dossierNotes, "general.md")
        : Path.Combine(dossierNotes, "projets", carnet + ".md");

    /// <summary>Carnets existants : <c>general</c> puis les carnets de projet, triés.</summary>
    public static IReadOnlyList<string> Lister(string dossierNotes)
    {
        var carnets = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string dossier = Path.Combine(dossierNotes, "projets");
            if (Directory.Exists(dossier))
            {
                foreach (string fichier in Directory.EnumerateFiles(dossier, "*.md"))
                {
                    string nom = Path.GetFileNameWithoutExtension(fichier);
                    if (EstNomValide(nom))
                        carnets.Add(nom);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier inaccessible : seuls les carnets déjà connus manquent, les notes générales restent possibles.
        }

        return [General, .. carnets];
    }

    /// <summary>Carnet existant correspondant au nom demandé (casse, accents et espaces ignorés).</summary>
    public static MayBe<string> Trouver(IReadOnlyList<string> carnets, string nom)
    {
        string cle = Compacter(nom);
        string? trouve = carnets.FirstOrDefault(c => Compacter(c) == cle);
        return trouve ?? MayBe<string>.Null;
    }

    /// <summary>
    /// Nom acceptable pour un nouveau carnet : il devient un nom de fichier (invariant 5).
    /// Pas de séparateur, pas de caractère interdit, pas de nom réservé, pas de « general ».
    /// </summary>
    public static bool EstNomValide(string nom) =>
        nom.Length is > 0 and <= LongueurNomMax
        && nom.Trim() == nom
        && !nom.StartsWith('.')
        && !nom.EndsWith('.')
        && nom.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && nom.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && !NomsReservesWindows.Contains(nom)
        && Compacter(nom) != General
        && Compacter(nom).Length > 0;

    /// <summary>Les paroles de l'utilisateur nomment-elles ce carnet ?</summary>
    public static bool EstCite(IEnumerable<string> paroles, string carnet)
    {
        foreach (string parole in paroles)
        {
            string mots = Mots(parole);
            if (carnet == General
                ? FormulationsGenerales.Any(f => $" {mots} ".Contains($" {f} ", StringComparison.Ordinal))
                : MotsCompactes(mots).Contains(Compacter(carnet)))
                return true;
        }

        return false;
    }

    /// <summary>Minuscules sans accents, mots séparés par une espace (la ponctuation sépare).</summary>
    private static string Mots(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char caractere in texte.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) == UnicodeCategory.NonSpacingMark)
                continue;

            resultat.Append(char.IsLetterOrDigit(caractere) ? caractere : ' ');
        }

        return string.Join(' ', resultat.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Minuscules sans accents ni séparateurs : « X-D », « x d » et « xd » donnent « xd ».</summary>
    private static string Compacter(string texte) => Mots(texte).Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>Suites de mots consécutifs recollées (« x d » → « xd »), pour reconnaître un nom dicté lettre par lettre.</summary>
    private static HashSet<string> MotsCompactes(string mots)
    {
        string[] liste = mots.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var suites = new HashSet<string>(StringComparer.Ordinal);
        for (int debut = 0; debut < liste.Length; debut++)
        {
            var suite = new StringBuilder();
            for (int fin = debut; fin < liste.Length && fin < debut + 6; fin++)
            {
                suite.Append(liste[fin]);
                suites.Add(suite.ToString());
            }
        }

        return suites;
    }
}
