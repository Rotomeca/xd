using Rotomeca.Rop.Interfaces;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

public sealed class LecturePhraseCheminTests
{
    private static readonly string Documents = Path.Combine(Path.GetTempPath(), "Documents");
    private static readonly string Systeme = Path.Combine(Path.GetTempPath(), "systeme");

    private static LecturePhraseChemin Lecture(params string[] dossiersExistants) =>
        new(Documents, Path.GetTempPath(), chemin => dossiersExistants.Contains(chemin, StringComparer.OrdinalIgnoreCase), [Systeme]);

    private static string? Extraire(LecturePhraseChemin lecture, string phrase, ElementNotes element = ElementNotes.DossierNotes)
    {
        var resultat = lecture.Extraire(phrase, element);
        return resultat.HasValue ? resultat.Value : null;
    }

    [Theory]
    [InlineData(@"Dans D:\Notes.", @"D:\Notes")]
    [InlineData(@"range-les dans D:\Notes, merci", @"D:\Notes")]
    [InlineData(@"D:/Notes/xd", @"D:/Notes/xd")]
    [InlineData(@"mets-les dans « D:\Mes notes » stp", @"D:\Mes notes")]
    [InlineData(@"dans ""E:\Dossier perso\idées""", @"E:\Dossier perso\idées")]
    public void CheminWindows_EstLuDansLaPhrase(string phrase, string attendu)
    {
        Assert.Equal(attendu, Extraire(Lecture(), phrase));
    }

    [Fact]
    public void SansGuillemets_LePlusLongDossierExistantEstRetenu()
    {
        Assert.Equal(@"D:\Mes notes", Extraire(Lecture(@"D:\Mes notes"), @"dans D:\Mes notes s'il te plaît"));
        Assert.Equal(@"D:\Mes", Extraire(Lecture(), @"dans D:\Mes notes s'il te plaît"));
    }

    [Theory]
    [InlineData("par défaut")]
    [InlineData("Range-les dans mes documents.")]
    public void MotCle_DonneLeDossierParDefaut(string phrase)
    {
        Assert.Equal(Path.Combine(Documents, "xd", "notes"), Extraire(Lecture(), phrase));
    }

    [Theory]
    [InlineData("dans mes documents, sous dossier TEST", "TEST")]
    [InlineData("dans mes documents, sous-dossier « Idées »", "Idées")]
    [InlineData("Mets-les dans mes documents dans un dossier nommé Notes.", "Notes")]
    [InlineData("dans mes documents, répertoire perso_2", "perso_2")]
    public void MesDocumentsAvecSousDossier_DonneCeSousDossier(string phrase, string sousDossier)
    {
        Assert.Equal(Path.Combine(Documents, sousDossier), Extraire(Lecture(), phrase));
    }

    [Theory]
    [InlineData("dans mes documents, sous-dossier ..")]
    [InlineData("dans mes documents, sous-dossier .cache")]
    public void SousDossierInvalide_RetombeSurLeDossierParDefaut(string phrase)
    {
        Assert.Equal(Path.Combine(Documents, "xd", "notes"), Extraire(Lecture(), phrase));
    }

    [Theory]
    [InlineData("je ne sais pas trop")]
    [InlineData("dans le dossier notes")]
    [InlineData("et/ou ailleurs")]
    public void SansChemin_NeDonneRien(string phrase)
    {
        Assert.Null(Extraire(Lecture(), phrase));
    }

    [Fact]
    public void MotClePourUnDossierDeProjets_NeDonneRien()
    {
        Assert.Null(Extraire(Lecture(), "par défaut", ElementNotes.DossierProjets));
    }

    [Fact]
    public void Tilde_EstDeveloppeVersLeDossierPersonnel()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath(), "notes"), Extraire(Lecture(), "dans ~/notes"));
    }

    [Fact]
    public void CheminValide_EstNormalise()
    {
        string dossier = Path.Combine(Path.GetTempPath(), "xd-notes") + Path.DirectorySeparatorChar;

        Assert.Equal(Path.TrimEndingDirectorySeparator(dossier), Valeur(Lecture().Valider(dossier, ElementNotes.DossierNotes)));
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("..")]
    public void CheminRelatif_EstRefuse(string chemin)
    {
        Assert.False(Lecture().Valider(chemin, ElementNotes.DossierNotes).IsSuccess);
    }

    [Fact]
    public void CheminAvecRemontee_EstRefuse()
    {
        Assert.False(Lecture().Valider(Path.Combine(Path.GetTempPath(), "a", "..", "b"), ElementNotes.DossierNotes).IsSuccess);
    }

    [Fact]
    public void RacineDUnDisque_EstRefusee()
    {
        string racine = Path.GetPathRoot(Path.GetTempPath()) ?? "/";

        Assert.False(Lecture().Valider(racine, ElementNotes.DossierNotes).IsSuccess);
    }

    [Fact]
    public void DossierSysteme_EstRefuse()
    {
        Assert.False(Lecture().Valider(Path.Combine(Systeme, "notes"), ElementNotes.DossierNotes).IsSuccess);
    }

    [Fact]
    public void DossierDeProjetsAbsent_EstRefuse_MaisPasUnDossierDeNotes()
    {
        string absent = Path.Combine(Path.GetTempPath(), "xd-absent-" + Guid.NewGuid().ToString("N"));

        Assert.False(Lecture().Valider(absent, ElementNotes.DossierProjets).IsSuccess);
        Assert.True(Lecture().Valider(absent, ElementNotes.DossierNotes).IsSuccess);
    }

    private static string Valeur(IResult<string> resultat) => resultat.Match(Ok: c => c, Err: e => "ERREUR " + e.Message);
}
