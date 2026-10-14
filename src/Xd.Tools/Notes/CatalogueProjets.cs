namespace Xd.Tools.Notes;

/// <summary>
/// Projets connus : les sous-dossiers des dossiers de projets configurés. Le modèle ne voit que leurs noms.
/// </summary>
public static class CatalogueProjets
{
    /// <summary>Noms des projets, triés, sans doublon ni dossier caché.</summary>
    /// <param name="dossiersProjets">Dossiers de projets de la configuration ; un dossier absent est ignoré.</param>
    public static IReadOnlyList<string> Lister(IEnumerable<string> dossiersProjets)
    {
        var noms = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string racine in dossiersProjets)
        {
            try
            {
                if (!Directory.Exists(racine))
                    continue;

                foreach (string dossier in Directory.EnumerateDirectories(racine))
                {
                    string nom = Path.GetFileName(dossier);
                    if (EstNomValide(nom))
                        noms.Add(nom);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier devenu inaccessible : ses projets ne sont simplement pas proposés.
            }
        }

        return [.. noms];
    }

    /// <summary>
    /// Un nom de projet sert de nom de fichier : pas de dossier caché, pas de séparateur, pas de caractère interdit.
    /// </summary>
    public static bool EstNomValide(string nom) =>
        nom.Length > 0
        && !nom.StartsWith('.')
        && nom.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && nom.IndexOfAny(['/', '\\']) < 0
        && nom is not ".." and not ".";
}
