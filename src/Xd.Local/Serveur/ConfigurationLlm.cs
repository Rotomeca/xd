using Xd.Local.Installation;

namespace Xd.Local.Serveur;

/// <summary>
/// Configuration du LLM local, écrite par <c>xd setup llm</c> dans le dossier de configuration utilisateur (jamais dans le dépôt).
/// </summary>
/// <param name="CheminServeur">Chemin de l'exécutable <c>llama-server</c>.</param>
/// <param name="CheminModele">Chemin du fichier GGUF.</param>
/// <param name="Palier">Palier choisi à l'installation.</param>
/// <param name="CouchesGpu">Valeur de <c>--n-gpu-layers</c> : <c>all</c> sur GPU, <c>0</c> sur CPU.</param>
/// <param name="TailleContexte">Taille du contexte en jetons.</param>
/// <param name="Port">Port d'écoute, sur la boucle locale uniquement.</param>
public sealed record ConfigurationLlm(
    string CheminServeur,
    string CheminModele,
    PalierLlm Palier,
    string CouchesGpu,
    int TailleContexte = 8192,
    int Port = 8080)
{
    /// <summary>Adresse de base du serveur.</summary>
    public Uri Adresse => new($"http://127.0.0.1:{Port}/");
}
