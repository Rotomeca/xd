using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Boucle;
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

    /// <summary>Contextes reçus, un par préparation.</summary>
    public List<ContexteAppel> Contextes { get; } = [];

    public OutilFactice(string nom, NiveauOutil niveau, IActionPreparee action)
        : this(nom, niveau, _ => Result.Ok(action))
    {
    }

    public string Nom { get; }
    public string Description => "Outil de test";
    public JsonElement SchemaParametres { get; set; } = JsonDocument.Parse("""{"type":"object"}""").RootElement;
    public NiveauOutil Niveau { get; }

    public IResult<IActionPreparee> Preparer(JsonElement parametres, ContexteAppel contexte)
    {
        Contextes.Add(contexte);
        return _preparer(parametres);
    }
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

internal sealed class ModeleFactice : IModele
{
    private readonly Func<int, CancellationToken, Task<IResult<ReponseModele>>> _repondre;

    public ModeleFactice(Func<int, CancellationToken, Task<IResult<ReponseModele>>> repondre)
    {
        _repondre = repondre;
    }

    /// <summary>Historiques reçus, un par envoi.</summary>
    public List<IReadOnlyList<Message>> Envois { get; } = [];

    /// <summary>Modèle qui renvoie les réponses dans l'ordre, une par envoi.</summary>
    public static ModeleFactice Script(params ReponseModele[] reponses) =>
        new((numero, _) => Task.FromResult(Result.Ok(reponses[numero])));

    public static ReponseModele Texte(string texte) => new([new BlocTexte(texte)], RaisonArret.Fin);

    public static ReponseModele Appels(params BlocAppelOutil[] appels) => new(appels, RaisonArret.AppelOutils);

    public static BlocAppelOutil Appel(string id, string nomOutil) =>
        new(id, nomOutil, JsonDocument.Parse("{}").RootElement);

    public Task<IResult<ReponseModele>> EnvoyerAsync(IReadOnlyList<Message> historique, IReadOnlyList<DefinitionOutil> outils, CancellationToken annulation)
    {
        Envois.Add(historique);
        return _repondre(Envois.Count - 1, annulation);
    }
}
