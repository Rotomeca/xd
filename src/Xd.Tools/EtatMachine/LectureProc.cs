using System.Globalization;
using Rotomeca.Core.Optionals;

namespace Xd.Tools.EtatMachine;

/// <summary>
/// Lecture du texte de <c>/proc/stat</c> et <c>/proc/meminfo</c> (Linux). Fonctions pures, testables sur toute plateforme.
/// </summary>
internal static class LectureProc
{
    /// <summary>Temps cumulés de la ligne <c>cpu</c> de <c>/proc/stat</c>, en ticks.</summary>
    internal sealed record TempsProcesseur(ulong Total, ulong Repos);

    /// <summary>Lit la première ligne : <c>cpu user nice system idle iowait irq softirq steal …</c>.</summary>
    internal static MayBe<TempsProcesseur> LireStat(string contenu)
    {
        string? ligne = contenu.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (ligne is null)
            return MayBe<TempsProcesseur>.Null;

        ulong[] valeurs = [.. ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(v => ulong.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out ulong n) ? n : ulong.MaxValue)];
        if (valeurs.Length < 5 || valeurs.Contains(ulong.MaxValue))
            return MayBe<TempsProcesseur>.Null;

        ulong total = valeurs.Aggregate(0UL, (somme, v) => somme + v);
        return new TempsProcesseur(total, valeurs[3] + valeurs[4]);
    }

    internal static double Charge(TempsProcesseur avant, TempsProcesseur apres)
    {
        double total = apres.Total - avant.Total;
        double repos = apres.Repos - avant.Repos;
        return total > 0 ? (1 - (repos / total)) * 100 : 0;
    }

    /// <summary>Lit <c>MemTotal</c> et <c>MemAvailable</c> (en kio).</summary>
    internal static MayBe<MemoireMachine> LireMeminfo(string contenu)
    {
        MayBe<long> totale = Champ(contenu, "MemTotal:");
        MayBe<long> disponible = Champ(contenu, "MemAvailable:");
        return totale.HasValue && disponible.HasValue
            ? new MemoireMachine(totale.Value * 1024, disponible.Value * 1024)
            : MayBe<MemoireMachine>.Null;
    }

    private static MayBe<long> Champ(string contenu, string nom)
    {
        string? ligne = contenu.Split('\n').FirstOrDefault(l => l.StartsWith(nom, StringComparison.Ordinal));
        string? valeur = ligne?[nom.Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return long.TryParse(valeur, NumberStyles.None, CultureInfo.InvariantCulture, out long kio) ? kio : MayBe<long>.Null;
    }
}
