namespace Xd.Local.Installation;

/// <summary>
/// Palier de modèle, du plus léger au plus gros.
/// </summary>
public enum PalierLlm
{
    /// <summary>Petit modèle sur processeur : toujours disponible, plus lent.</summary>
    Leger,

    /// <summary>Modèle moyen sur carte graphique.</summary>
    Standard,

    /// <summary>Gros modèle sur carte graphique.</summary>
    Confort,
}
