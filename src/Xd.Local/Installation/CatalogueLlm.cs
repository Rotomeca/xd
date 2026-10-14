using System.Runtime.InteropServices;
using Rotomeca.Core.Optionals;

namespace Xd.Local.Installation;

/// <summary>Fichier à télécharger, épinglé par son empreinte.</summary>
/// <param name="Nom">Nom du fichier sur le disque.</param>
/// <param name="Source">Adresse HTTPS figée (version ou commit précis, jamais « latest »).</param>
/// <param name="Taille">Taille attendue en octets.</param>
/// <param name="Sha256">Empreinte SHA-256 attendue, en hexadécimal minuscule.</param>
public sealed record FichierDistant(string Nom, Uri Source, long Taille, string Sha256);

/// <summary>Modèle proposé pour un palier.</summary>
/// <param name="Palier">Palier servi par ce modèle.</param>
/// <param name="Fichier">Fichier GGUF.</param>
/// <param name="MemoireVideoRequise">Mémoire vidéo libre nécessaire, marge pour la voix comprise ; 0 pour un modèle sur processeur.</param>
public sealed record ModeleCatalogue(PalierLlm Palier, FichierDistant Fichier, long MemoireVideoRequise)
{
    /// <summary>Le modèle tourne-t-il sur carte graphique ?</summary>
    public bool SurGpu => MemoireVideoRequise > 0;
}

/// <summary>Variante de llama.cpp.</summary>
public enum MoteurLlm
{
    /// <summary>Processeur uniquement.</summary>
    Cpu,

    /// <summary>Carte graphique via Vulkan (NVIDIA, AMD, Intel ; Windows et Linux).</summary>
    Vulkan,
}

/// <summary>
/// Sources épinglées de <c>xd setup llm</c>. Toute mise à jour passe par une revue de ce fichier :
/// nouvelle version, nouvelles adresses, nouvelles empreintes.
/// </summary>
public static class CatalogueLlm
{
    private const long Go = 1024L * 1024 * 1024;

    /// <summary>Build de llama.cpp (version stable v0.6.0).</summary>
    public const string VersionLlamaCpp = "b11429";

    /// <summary>Modèles, du plus gros au plus léger. Qwen3, licence Apache 2.0, quantification Q4_K_M.</summary>
    public static IReadOnlyList<ModeleCatalogue> Modeles { get; } =
    [
        new(PalierLlm.Confort, Gguf("Qwen/Qwen3-14B-GGUF", "530227a7d994db8eca5ab5ced2fb692b614357fd", "Qwen3-14B-Q4_K_M.gguf",
            9001752960, "500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0"), 13 * Go),
        new(PalierLlm.Standard, Gguf("Qwen/Qwen3-8B-GGUF", "7c41481f57cb95916b40956ab2f0b139b296d974", "Qwen3-8B-Q4_K_M.gguf",
            5027783488, "d98cdcbd03e17ce47681435b5150e34c1417f50b5c0019dd560e4882c5745785"), 9 * Go),
        new(PalierLlm.Leger, Gguf("Qwen/Qwen3-4B-GGUF", "bc640142c66e1fdd12af0bd68f40445458f3869b", "Qwen3-4B-Q4_K_M.gguf",
            2497280256, "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5"), 0),
    ];

    /// <summary>Archive de llama.cpp pour la plateforme et le moteur demandés.</summary>
    /// <returns>L'archive, ou rien si la plateforme n'est pas prise en charge.</returns>
    public static MayBe<FichierDistant> ArchiveServeur(OSPlatform systeme, Architecture architecture, MoteurLlm moteur)
    {
        if (architecture != Architecture.X64)
            return MayBe<FichierDistant>.Null;

        return (systeme == OSPlatform.Windows, systeme == OSPlatform.Linux, moteur) switch
        {
            (true, _, MoteurLlm.Vulkan) => LlamaCpp("win-vulkan-x64.zip", 33337769, "1bfe78ad9168b79fa02bf67f6af9f5e17a966d824d77238517f7bef12ac73b36"),
            (true, _, MoteurLlm.Cpu) => LlamaCpp("win-cpu-x64.zip", 19398918, "1283323272b04cd07905816a597a0da810918102de958f4ff6f7bbaa70ed2efe"),
            (_, true, MoteurLlm.Vulkan) => LlamaCpp("ubuntu-vulkan-x64.tar.gz", 31636673, "632c4e98feba2b94407a2130e3133e0c3aefb0ea1ab41337e926d8bfafdd0b74"),
            (_, true, MoteurLlm.Cpu) => LlamaCpp("ubuntu-x64.tar.gz", 17693462, "f6d25dde8f51133143d1453da4fd5f73b145127177612a283bf7995957af3392"),
            _ => MayBe<FichierDistant>.Null,
        };
    }

    private static FichierDistant LlamaCpp(string suffixe, long taille, string sha256)
    {
        string nom = $"llama-{VersionLlamaCpp}-bin-{suffixe}";
        return new FichierDistant(nom, new Uri($"https://github.com/ggml-org/llama.cpp/releases/download/{VersionLlamaCpp}/{nom}"), taille, sha256);
    }

    // Adresse figée sur un commit du dépôt Hugging Face, jamais sur « main ».
    private static FichierDistant Gguf(string depot, string commit, string nom, long taille, string sha256) =>
        new(nom, new Uri($"https://huggingface.co/{depot}/resolve/{commit}/{nom}"), taille, sha256);
}
