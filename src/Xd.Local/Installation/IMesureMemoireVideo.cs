using Rotomeca.Core.Optionals;

namespace Xd.Local.Installation;

/// <summary>
/// Mesure de la mémoire vidéo disponible. Une implémentation par type de carte ou par OS.
/// </summary>
public interface IMesureMemoireVideo
{
    /// <summary>Mémoire vidéo libre en octets, ou rien si aucune carte graphique n'est détectée.</summary>
    Task<MayBe<long>> MesurerLibreAsync(CancellationToken annulation);
}
