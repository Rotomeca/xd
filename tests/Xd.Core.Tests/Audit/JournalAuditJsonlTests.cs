using System.Text.Json;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;

namespace Xd.Core.Tests.Audit;

public sealed class JournalAuditJsonlTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "xd-tests", Guid.NewGuid().ToString("N"));

    private string Chemin => Path.Combine(_dossier, "audit", "audit.jsonl");

    private static EntreeAudit Entree(EtapeAudit etape, string? detail) => new(
        new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero),
        "appel-1",
        "noter_idee",
        JsonDocument.Parse("""{"projet":"xd","texte":"idée"}""").RootElement,
        "note une idée",
        etape,
        detail);

    [Fact]
    public async Task EcrireAsync_AjouteUneLigneJsonParEntreeSansRecrire()
    {
        using var journal = new JournalAuditJsonl(Chemin);

        IResult<bool> premiere = await journal.EcrireAsync(Entree(EtapeAudit.Demande, null), CancellationToken.None);
        string apresPremiere = await File.ReadAllTextAsync(Chemin);
        IResult<bool> seconde = await journal.EcrireAsync(Entree(EtapeAudit.Execute, "Idée notée."), CancellationToken.None);
        string[] lignes = await File.ReadAllLinesAsync(Chemin);

        Assert.True(premiere.IsSuccess);
        Assert.True(seconde.IsSuccess);
        Assert.Equal(2, lignes.Length);
        Assert.StartsWith(apresPremiere, await File.ReadAllTextAsync(Chemin));

        using JsonDocument derniere = JsonDocument.Parse(lignes[1]);
        Assert.Equal("Execute", derniere.RootElement.GetProperty("etape").GetString());
        Assert.Equal("Idée notée.", derniere.RootElement.GetProperty("detail").GetString());
        Assert.Equal("xd", derniere.RootElement.GetProperty("parametres").GetProperty("projet").GetString());
    }

    [Fact]
    public async Task EcrireAsync_OmetLeDetailAbsent()
    {
        using var journal = new JournalAuditJsonl(Chemin);

        await journal.EcrireAsync(Entree(EtapeAudit.Demande, null), CancellationToken.None);

        using JsonDocument ligne = JsonDocument.Parse(await File.ReadAllTextAsync(Chemin));
        Assert.False(ligne.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task EcrireAsync_CheminInaccessible_RenvoieUneErreur()
    {
        Directory.CreateDirectory(Chemin); // un dossier à la place du fichier
        using var journal = new JournalAuditJsonl(Chemin);

        IResult<bool> resultat = await journal.EcrireAsync(Entree(EtapeAudit.Demande, null), CancellationToken.None);

        Assert.True(resultat.IsError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }
}
