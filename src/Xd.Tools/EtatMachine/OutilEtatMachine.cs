using System.Globalization;
using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.EtatMachine;

/// <summary>
/// <c>etat_machine</c> (niveau <see cref="NiveauOutil.Libre"/>, lecture seule) : charge du processeur, mémoire vive,
/// espace disque ou conteneurs en marche, résumés en une phrase.
/// </summary>
public sealed class OutilEtatMachine : IOutil
{
    private const string ParametreAspect = "aspect";

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly IReadOnlyDictionary<string, AspectMachine> Aspects = new Dictionary<string, AspectMachine>(StringComparer.Ordinal)
    {
        ["cpu"] = AspectMachine.Cpu,
        ["ram"] = AspectMachine.Ram,
        ["disque"] = AspectMachine.Disque,
        ["conteneurs"] = AspectMachine.Conteneurs,
    };

    private readonly IMesuresMachine _mesures;
    private readonly ISourceConteneurs _conteneurs;

    public OutilEtatMachine(IMesuresMachine mesures, ISourceConteneurs conteneurs)
    {
        _mesures = mesures;
        _conteneurs = conteneurs;
    }

    /// <inheritdoc/>
    public string Nom => "etat_machine";

    /// <inheritdoc/>
    public string Description =>
        "Donne l'état actuel de l'ordinateur : charge du processeur (cpu), mémoire vive (ram), "
        + "espace disque (disque) ou conteneurs Docker en marche (conteneurs).";

    /// <inheritdoc/>
    public JsonElement SchemaParametres { get; } = JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            [ParametreAspect] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = Aspects.Keys.ToArray(),
                ["description"] = "Aspect de la machine à consulter.",
            },
        },
        ["required"] = new[] { ParametreAspect },
        ["additionalProperties"] = false,
    });

    /// <inheritdoc/>
    public NiveauOutil Niveau => NiveauOutil.Libre;

    /// <inheritdoc/>
    public IResult<IActionPreparee> Preparer(JsonElement parametres, ContexteAppel contexte)
    {
        if (parametres.ValueKind != JsonValueKind.Object)
            return Invalide("les paramètres doivent être un objet JSON.");

        foreach (JsonProperty propriete in parametres.EnumerateObject())
        {
            if (propriete.Name != ParametreAspect)
                return Invalide($"paramètre inconnu : « {propriete.Name} ».");
        }

        if (!parametres.TryGetProperty(ParametreAspect, out JsonElement valeur) || valeur.ValueKind != JsonValueKind.String)
            return Invalide($"le paramètre « {ParametreAspect} » est obligatoire ({ValeursPossibles()}).");

        // Valeur exacte uniquement : hors énumération, c'est une erreur, jamais un aspect par défaut (invariant 4).
        string texte = valeur.GetString() ?? string.Empty;
        return Aspects.TryGetValue(texte, out AspectMachine aspect)
            ? Result.Ok<IActionPreparee>(new ActionEtatMachine(aspect, _mesures, _conteneurs))
            : Invalide($"aspect inconnu : « {texte} ». Valeurs possibles : {ValeursPossibles()}.");
    }

    private static string ValeursPossibles() => string.Join(", ", Aspects.Keys);

    private static IResult<IActionPreparee> Invalide(string message) =>
        Result.Fail<IActionPreparee>(new Error(message, ErrorType.Validation));

    private sealed class ActionEtatMachine(AspectMachine aspect, IMesuresMachine mesures, ISourceConteneurs conteneurs) : IActionPreparee
    {
        public string Reformulation => aspect switch
        {
            AspectMachine.Cpu => "Je regarde la charge du processeur.",
            AspectMachine.Ram => "Je regarde la mémoire vive.",
            AspectMachine.Disque => "Je regarde l'espace disque.",
            AspectMachine.Conteneurs => "Je regarde les conteneurs en marche.",
            _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, null),
        };

        public async Task<IResult<string>> ExecuterAsync(CancellationToken annulation) => aspect switch
        {
            AspectMachine.Cpu => Resumer(await mesures.MesurerProcesseurAsync(annulation), ResumerProcesseur),
            AspectMachine.Ram => Resumer(mesures.MesurerMemoire(), ResumerMemoire),
            AspectMachine.Disque => Resumer(mesures.MesurerDisques(), ResumerDisques),
            AspectMachine.Conteneurs => Resumer(await conteneurs.ListerAsync(annulation), ResumerConteneurs),
            _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, null),
        };
    }

    private static IResult<string> Resumer<T>(IResult<T> mesure, Func<T, string> resumer) =>
        mesure.Match(Ok: valeur => Result.Ok(resumer(valeur)), Err: erreur => Result.Fail<string>(erreur));

    internal static string ResumerProcesseur(double charge) =>
        string.Format(Francais, "Processeur utilisé à {0:0} %.", Math.Clamp(charge, 0, 100));

    internal static string ResumerMemoire(MemoireMachine memoire)
    {
        long utilisee = memoire.Totale - memoire.Disponible;
        double pourcentage = memoire.Totale > 0 ? utilisee * 100d / memoire.Totale : 0;
        return string.Format(Francais, "Mémoire vive : {0} utilisés sur {1} ({2:0} %).", Go(utilisee), Go(memoire.Totale), pourcentage);
    }

    internal static string ResumerDisques(IReadOnlyList<EspaceDisque> disques) =>
        disques.Count == 0
            ? "Aucun disque fixe trouvé."
            : string.Join(" ", disques.Select(d => string.Format(Francais, "Disque {0} : {1} libres sur {2}.", d.Nom, Go(d.Libre), Go(d.Total))));

    internal static string ResumerConteneurs(IReadOnlyList<Conteneur> conteneurs) => conteneurs.Count switch
    {
        0 => "Aucun conteneur en marche.",
        1 => $"1 conteneur en marche : {Decrire(conteneurs[0])}.",
        _ => $"{conteneurs.Count} conteneurs en marche : {string.Join(", ", conteneurs.Select(Decrire))}.",
    };

    private static string Decrire(Conteneur conteneur) => $"{conteneur.Nom} ({conteneur.Image})";

    private static string Go(long octets) => string.Format(Francais, "{0:0.#} Go", octets / (1024d * 1024 * 1024));
}
