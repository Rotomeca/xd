namespace Xd.Local.Serveur;

/// <summary>
/// Dossiers de Xd hors du dépôt : configuration (petits fichiers) et données (exécutables, modèles).
/// </summary>
/// <param name="Configuration">Dossier de configuration (Windows : <c>%APPDATA%\xd</c>, Linux : <c>~/.config/xd</c>).</param>
/// <param name="Donnees">Dossier des données volumineuses (Windows : <c>%LOCALAPPDATA%\xd</c>, Linux : <c>~/.local/share/xd</c>).</param>
public sealed record EmplacementsXd(string Configuration, string Donnees)
{
    /// <summary>Emplacements standard de la plateforme.</summary>
    public static EmplacementsXd Defaut() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xd"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "xd"));

    /// <summary>Fichier de configuration du LLM.</summary>
    public string FichierConfigurationLlm => Path.Combine(Configuration, "llm.json");

    /// <summary>Journal d'audit des appels d'outils.</summary>
    public string JournalAudit => Path.Combine(Donnees, "audit.jsonl");

    /// <summary>Dossier des exécutables llama.cpp et des modèles.</summary>
    public string DossierLlm => Path.Combine(Donnees, "llm");
}
