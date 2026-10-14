using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Tools.Notes;

namespace Xd.Tools.Tests.Notes;

internal sealed class ConfigurationEnMemoire : IStockageConfigurationNotes
{
    public ConfigurationNotes Valeur { get; set; } = ConfigurationNotes.Vide;

    public IResult<ConfigurationNotes> Lire() => Result.Ok(Valeur);

    public IResult<bool> Ecrire(ConfigurationNotes configuration)
    {
        Valeur = configuration;
        return Result.Ok(true);
    }
}

internal sealed class HorlogeFixe(DateTimeOffset maintenant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => maintenant;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Dossier temporaire supprimé à la fin du test.</summary>
internal sealed class DossierTemporaire : IDisposable
{
    public string Chemin { get; } = Path.Combine(Path.GetTempPath(), "xd-tests-" + Guid.NewGuid().ToString("N"));

    public DossierTemporaire() => Directory.CreateDirectory(Chemin);

    public void Dispose() => Directory.Delete(Chemin, recursive: true);
}
