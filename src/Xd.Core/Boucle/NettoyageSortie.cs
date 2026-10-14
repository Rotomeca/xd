using System.Globalization;
using System.Text;

namespace Xd.Core.Boucle;

/// <summary>
/// Nettoyage déterministe du texte à dire : la réponse sera lue par une synthèse vocale, on ne compte pas sur le modèle
/// pour respecter « sans emoji, sans markdown ».
/// </summary>
public static class NettoyageSortie
{
    private const int SelecteurVariante = 0xFE0F;
    private const int LiantSansChasse = 0x200D;

    /// <summary>Retire emojis et pictogrammes, marques de markdown (<c>*</c>, <c>`</c>, <c>#</c> en début de ligne) et espaces superflus.</summary>
    public static string PourLaVoix(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (Rune caractere in texte.EnumerateRunes())
        {
            if (EstPictogramme(caractere) || caractere.Value is '*' or '`')
                continue;

            resultat.Append(caractere.ToString());
        }

        IEnumerable<string> lignes = resultat.ToString()
            .Split('\n')
            .Select(ligne => ligne.TrimStart().TrimStart('#').Trim());
        return string.Join(' ', string.Join(' ', lignes).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool EstPictogramme(Rune caractere) =>
        caractere.Value is SelecteurVariante or LiantSansChasse
        || Rune.GetUnicodeCategory(caractere) is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol && caractere.Value > 0x2000;
}
