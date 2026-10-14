using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Outils;
using Xd.Core.Permissions;

namespace Xd.Core.Tests;

internal sealed class OutilFactice : IOutil
{
    private readonly Func<JsonElement, IResult<IActionPreparee>> _preparer;

    public OutilFactice(string nom, NiveauOutil niveau, Func<JsonElement, IResult<IActionPreparee>> preparer)
    {
        Nom = nom;
        Niveau = niveau;
        _preparer = preparer;
    }

    public OutilFactice(string nom, NiveauOutil niveau, IActionPreparee action)
        : this(nom, niveau, _ => Result.Ok(action))
    {
    }

    public string Nom { get; }
    public string Description => "Outil de test";
    public JsonElement SchemaParametres { get; } = JsonDocument.Parse("""{"type":"object"}""").RootElement;
    public NiveauOutil Niveau { get; }

    public IResult<IActionPreparee> Preparer(JsonElement parametres) => _preparer(parametres);
}

internal sealed class ActionFactice : IActionPreparee
{
    private readonly Func<CancellationToken, Task<IResult<string>>> _executer;

    public ActionFactice(string reformulation, Func<CancellationToken, Task<IResult<string>>> executer)
    {
        Reformulation = reformulation;
        _executer = executer;
    }

    public ActionFactice(string reformulation, string resultat)
        : this(reformulation, _ => Task.FromResult(Result.Ok(resultat)))
    {
    }

    public string Reformulation { get; }
    public int NombreExecutions { get; private set; }

    public Task<IResult<string>> ExecuterAsync(CancellationToken annulation)
    {
        NombreExecutions++;
        return _executer(annulation);
    }
}

internal sealed class ConfirmateurFactice : IConfirmateur
{
    private readonly Func<CancellationToken, Task<bool>> _repondre;

    public ConfirmateurFactice(bool reponse) : this(_ => Task.FromResult(reponse))
    {
    }

    public ConfirmateurFactice(Func<CancellationToken, Task<bool>> repondre)
    {
        _repondre = repondre;
    }

    public List<string> Questions { get; } = [];

    /// <summary>Confirmateur qui ne répond jamais, jusqu'à l'annulation.</summary>
    public static ConfirmateurFactice EnAttente() =>
        new(async annulation =>
        {
            await Task.Delay(Timeout.Infinite, annulation);
            return true;
        });

    public Task<bool> ConfirmerAsync(string reformulation, CancellationToken annulation)
    {
        Questions.Add(reformulation);
        return _repondre(annulation);
    }
}

internal sealed class JournalMemoire : IJournalAudit
{
    public List<EntreeAudit> Entrees { get; } = [];
    public bool Indisponible { get; init; }

    public IEnumerable<EtapeAudit> Etapes => Entrees.Select(e => e.Etape);

    public Task<IResult<bool>> EcrireAsync(EntreeAudit entree, CancellationToken annulation)
    {
        if (Indisponible)
            return Task.FromResult(Result.Fail<bool>(new Error("disque plein", ErrorType.Unexpected)));

        Entrees.Add(entree);
        return Task.FromResult(Result.Ok(true));
    }
}

internal sealed class HorlogeFixe(DateTimeOffset maintenant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => maintenant;
}
