using Rotomeca.Core.Optionals;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Local.Installation;

/// <summary>Modèle et moteur retenus pour l'installation.</summary>
/// <param name="Modele">Modèle du palier choisi.</param>
/// <param name="Moteur">Variante de llama.cpp à installer.</param>
public sealed record ChoixInstallation(ModeleCatalogue Modele, MoteurLlm Moteur)
{
    /// <summary>Valeur de <c>--n-gpu-layers</c>.</summary>
    public string CouchesGpu => Modele.SurGpu ? "all" : "0";
}

/// <summary>
/// Choix du palier selon la mémoire vidéo libre.
/// </summary>
public static class ChoixPalier
{
    /// <summary>Choisit le plus gros palier qui tient, ou le palier imposé.</summary>
    /// <param name="memoireVideoLibre">Mémoire vidéo libre mesurée, ou rien si aucune carte graphique n'est détectée.</param>
    /// <param name="palierImpose">Palier demandé explicitement par l'utilisateur.</param>
    /// <param name="modeles">Modèles disponibles, du plus gros au plus léger.</param>
    public static IResult<ChoixInstallation> Choisir(MayBe<long> memoireVideoLibre, MayBe<PalierLlm> palierImpose, IReadOnlyList<ModeleCatalogue> modeles)
    {
        MoteurLlm moteur = memoireVideoLibre.HasValue ? MoteurLlm.Vulkan : MoteurLlm.Cpu;

        if (palierImpose.HasValue)
        {
            ModeleCatalogue? impose = modeles.FirstOrDefault(m => m.Palier == palierImpose.Value);
            return impose switch
            {
                null => Echec($"Palier inconnu : {palierImpose.Value}."),
                { SurGpu: true } when memoireVideoLibre.IsEmpty =>
                    Echec($"Le palier {impose.Palier} demande une carte graphique, et aucune n'a été détectée."),
                _ => Result.Ok(new ChoixInstallation(impose, moteur)),
            };
        }

        long libre = memoireVideoLibre.GetValueOrDefault(0);
        ModeleCatalogue? choisi = modeles.FirstOrDefault(m => !m.SurGpu || (memoireVideoLibre.HasValue && m.MemoireVideoRequise <= libre));
        return choisi is null
            ? Echec("Aucun palier ne convient à cette machine.")
            : Result.Ok(new ChoixInstallation(choisi, moteur));
    }

    private static IResult<ChoixInstallation> Echec(string message) =>
        Result.Fail<ChoixInstallation>(new Error(message, ErrorType.Validation));
}
