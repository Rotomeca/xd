using System.Text.RegularExpressions;
using Rotomeca.Core.Optionals;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.Notes;

/// <summary>
/// Lecture d'un chemin dans la phrase de l'utilisateur (jamais dans le texte du modèle), puis validation.
/// </summary>
public sealed class LecturePhraseChemin
{
    private static readonly Regex Guillemets = new("[\"«“]\\s*([^\"»”]+?)\\s*[\"»”]");
    private static readonly Regex CheminWindows = new(@"(?<![\w])[A-Za-z]:[\\/][^\r\n""«»“”]*");
    private static readonly Regex CheminUnix = new(@"(?<![\w:~/])(?:~[\\/]|/)[^\r\n""«»“”]*");
    private static readonly Regex DebutDeChemin = new(@"^(?:[A-Za-z]:[\\/]|~[\\/]|/)");
    private static readonly Regex SousDossier = new(
        @"(?:sous[- ]?dossier|sous[- ]?r[ée]pertoire|dossier|r[ée]pertoire)\s+(?:nomm[ée]\s+|appel[ée]\s+)?[«""“]?\s*([^\s,.;!?«»""”\\/]+)",
        RegexOptions.IgnoreCase);
    private static readonly Regex DocumentsBarre = new(@"mes\s+documents\s*[\\/]\s*([^\s,.;!?«»""”\\/]+)", RegexOptions.IgnoreCase);
    private static readonly char[] PonctuationFinale = ['.', ',', ';', '!', '?', ')', ' ', '\t'];

    private readonly string _documents;
    private readonly string _personnel;
    private readonly Func<string, bool> _dossierExiste;
    private readonly IReadOnlyList<string> _dossiersSysteme;

    /// <param name="documents">Dossier « Documents » de l'utilisateur.</param>
    /// <param name="personnel">Dossier personnel (cible de <c>~</c>).</param>
    /// <param name="dossierExiste">Test d'existence d'un dossier (injecté pour les tests).</param>
    /// <param name="dossiersSysteme">Dossiers où Xd refuse d'écrire ou de lister.</param>
    public LecturePhraseChemin(string documents, string personnel, Func<string, bool> dossierExiste, IReadOnlyList<string> dossiersSysteme)
    {
        _documents = documents;
        _personnel = personnel;
        _dossierExiste = dossierExiste;
        _dossiersSysteme = dossiersSysteme;
    }

    /// <summary>Lecture avec les dossiers réels de la plateforme.</summary>
    public static LecturePhraseChemin Plateforme() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Directory.Exists,
        DossiersSystemePlateforme());

    /// <summary>Dossier proposé par « par défaut » ou « dans mes documents ».</summary>
    public string DossierNotesParDefaut => Path.Combine(_documents, "xd", "notes");

    /// <summary>Trouve un chemin dans la phrase : entre guillemets, absolu (<c>D:\Notes</c>, <c>/home/…</c>, <c>~/…</c>) ou par mot-clé.</summary>
    public MayBe<string> Extraire(string phrase)
    {
        Match entreGuillemets = Guillemets.Match(phrase);
        if (entreGuillemets.Success && DebutDeChemin.IsMatch(entreGuillemets.Groups[1].Value))
            return Developper(entreGuillemets.Groups[1].Value);

        Match absolu = CheminWindows.Match(phrase);
        if (!absolu.Success)
            absolu = CheminUnix.Match(phrase);
        if (absolu.Success)
            return Delimiter(Developper(absolu.Value));

        string minuscules = phrase.ToLowerInvariant();

        // « mes documents/TEST », « dans mes documents, sous-dossier TEST » : sous-dossier nommé des Documents.
        if (minuscules.Contains("mes documents"))
        {
            Match sousDossier = DocumentsBarre.Match(phrase);
            if (!sousDossier.Success)
                sousDossier = SousDossier.Match(phrase);
            if (sousDossier.Success && CatalogueCarnets.EstNomValide(sousDossier.Groups[1].Value))
                return Path.Combine(_documents, sousDossier.Groups[1].Value);

            return DossierNotesParDefaut;
        }

        return minuscules.Contains("par défaut") || minuscules.Contains("par defaut") ? DossierNotesParDefaut : MayBe<string>.Null;
    }

    /// <summary>Vérifie qu'un chemin est acceptable pour le dossier de notes et le normalise.</summary>
    public IResult<string> Valider(string chemin)
    {
        if (!EstAbsolu(chemin))
            return Refus($"« {chemin} » n'est pas un chemin complet.");
        if (chemin.Split('/', '\\').Any(segment => segment == ".."))
            return Refus($"« {chemin} » contient « .. ».");

        string complet = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chemin));
        string? racine = Path.GetPathRoot(complet);
        if (racine is not null && string.Equals(Path.TrimEndingDirectorySeparator(racine), complet, Comparaison))
            return Refus($"la racine d'un disque (« {complet} ») n'est pas acceptée.");
        if (_dossiersSysteme.Any(systeme => EstDans(complet, systeme)))
            return Refus($"« {complet} » est un dossier système.");
        if (File.Exists(complet))
            return Refus($"« {complet} » est un fichier, pas un dossier.");

        return Result.Ok(complet);
    }

    /// <summary>Le dossier existe-t-il déjà ?</summary>
    public bool Existe(string chemin) => _dossierExiste(chemin);

    private static StringComparison Comparaison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool EstAbsolu(string chemin) => Path.IsPathFullyQualified(chemin);

    private static bool EstDans(string chemin, string dossier)
    {
        if (string.IsNullOrEmpty(dossier))
            return false;

        string parent = Path.TrimEndingDirectorySeparator(dossier);
        return string.Equals(chemin, parent, Comparaison) || chemin.StartsWith(parent + Path.DirectorySeparatorChar, Comparaison);
    }

    private string Developper(string chemin) =>
        chemin.StartsWith('~') ? Path.Combine(_personnel, chemin[1..].TrimStart('/', '\\')) : chemin.Trim();

    /// <summary>
    /// Sans guillemets, la fin du chemin est ambiguë (« D:\Mes notes s'il te plaît ») : on garde le plus long préfixe,
    /// coupé entre deux mots, qui désigne un dossier existant ; sinon le premier mot.
    /// </summary>
    private string Delimiter(string capture)
    {
        string[] mots = capture.TrimEnd(PonctuationFinale).Split(' ');
        for (int nombre = mots.Length; nombre > 0; nombre--)
        {
            string candidat = string.Join(' ', mots[..nombre]).TrimEnd(PonctuationFinale);
            if (_dossierExiste(candidat))
                return candidat;
        }

        return mots[0].TrimEnd(PonctuationFinale);
    }

    private static IReadOnlyList<string> DossiersSystemePlateforme() => OperatingSystem.IsWindows()
        ?
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ]
        : ["/bin", "/boot", "/dev", "/etc", "/lib", "/lib64", "/proc", "/root", "/run", "/sbin", "/sys", "/usr", "/var"];

    private static IResult<string> Refus(string message) => Result.Fail<string>(new Error(message, ErrorType.Validation));
}
