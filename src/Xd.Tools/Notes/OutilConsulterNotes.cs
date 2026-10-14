using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.Notes;

/// <summary>
/// <c>consulter_notes</c> (niveau <see cref="NiveauOutil.Libre"/>, lecture seule) : emplacement des notes, liste des carnets,
/// ou dernières notes d'un carnet.
/// </summary>
public sealed class OutilConsulterNotes : IOutil
{
    internal const int NotesMax = 5;
    private const int LongueurNoteMax = 200;
    private const string ParametreQuoi = "quoi";
    private const string ParametreCarnet = "carnet";

    private static readonly IReadOnlyDictionary<string, Consultation> Consultations = new Dictionary<string, Consultation>(StringComparer.Ordinal)
    {
        ["emplacement"] = Consultation.Emplacement,
        ["carnets"] = Consultation.Carnets,
        ["dernieres_notes"] = Consultation.DernieresNotes,
    };

    private readonly IStockageConfigurationNotes _configuration;

    public OutilConsulterNotes(IStockageConfigurationNotes configuration)
    {
        _configuration = configuration;
    }

    private enum Consultation
    {
        Emplacement,
        Carnets,
        DernieresNotes,
    }

    /// <inheritdoc/>
    public string Nom => "consulter_notes";

    /// <inheritdoc/>
    public string Description =>
        "Lit les notes sans rien modifier : où elles sont rangées (emplacement), quels carnets existent (carnets), "
        + "ou les dernières notes d'un carnet (dernieres_notes, avec le carnet demandé).";

    /// <inheritdoc/>
    public JsonElement SchemaParametres => JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            [ParametreQuoi] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = Consultations.Keys.ToArray(),
            },
            [ParametreCarnet] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Pour dernieres_notes : « general » ou le nom d'un carnet existant.",
            },
        },
        ["required"] = new[] { ParametreQuoi },
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
            if (propriete.Name is not ParametreQuoi and not ParametreCarnet)
                return Invalide($"paramètre inconnu : « {propriete.Name} ».");
        }

        if (!parametres.TryGetProperty(ParametreQuoi, out JsonElement valeurQuoi) || valeurQuoi.ValueKind != JsonValueKind.String
            || !Consultations.TryGetValue(valeurQuoi.GetString() ?? string.Empty, out Consultation consultation))
            return Invalide($"le paramètre « {ParametreQuoi} » doit valoir {string.Join(", ", Consultations.Keys)}.");

        return _configuration.Lire().Match(
            Ok: configuration => PreparerAvec(configuration, consultation, parametres),
            Err: erreur => Result.Fail<IActionPreparee>(erreur));
    }

    private static IResult<IActionPreparee> PreparerAvec(ConfigurationNotes configuration, Consultation consultation, JsonElement parametres)
    {
        if (string.IsNullOrWhiteSpace(configuration.DossierNotes) || configuration.DossierNotes is not { } dossier)
            return Invalide("aucun dossier de notes n'est configuré : il n'y a encore aucune note.");

        IReadOnlyList<string> carnets = CatalogueCarnets.Lister(dossier);
        if (consultation != Consultation.DernieresNotes)
            return Result.Ok<IActionPreparee>(new ActionConsulter(consultation, dossier, carnets, CatalogueCarnets.General));

        // Pas de carnet par défaut (invariant 4) : sans carnet nommé, le modèle demande lequel.
        string demande = parametres.TryGetProperty(ParametreCarnet, out JsonElement valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? string.Empty
            : string.Empty;
        var carnet = CatalogueCarnets.Trouver(carnets, demande);
        return carnet.Value is { } trouve
            ? Result.Ok<IActionPreparee>(new ActionConsulter(consultation, dossier, carnets, trouve))
            : Invalide($"carnet inconnu : « {demande} ». Carnets existants : {string.Join(", ", carnets)}.");
    }

    private static IResult<IActionPreparee> Invalide(string message) =>
        Result.Fail<IActionPreparee>(new Error(message, ErrorType.Validation));

    private sealed class ActionConsulter(Consultation consultation, string dossier, IReadOnlyList<string> carnets, string carnet) : IActionPreparee
    {
        public string Reformulation => consultation switch
        {
            Consultation.Emplacement => "Je regarde où sont rangées les notes.",
            Consultation.Carnets => "Je regarde quels carnets existent.",
            _ => $"Je lis les dernières notes de {carnet}.",
        };

        public async Task<IResult<string>> ExecuterAsync(CancellationToken annulation) => consultation switch
        {
            Consultation.Emplacement => Result.Ok(
                $"Les notes sont dans {dossier}. Notes générales : {CatalogueCarnets.Fichier(dossier, CatalogueCarnets.General)}. "
                + $"Carnets de projet : {Path.Combine(dossier, "projets")}."),
            Consultation.Carnets => Result.Ok(carnets.Count > 1
                ? $"Carnets : notes générales, {string.Join(", ", carnets.Skip(1))}."
                : "Il n'y a que les notes générales pour l'instant."),
            _ => await LireDernieresAsync(annulation),
        };

        private async Task<IResult<string>> LireDernieresAsync(CancellationToken annulation)
        {
            string fichier = CatalogueCarnets.Fichier(dossier, carnet);
            if (!File.Exists(fichier))
                return Result.Ok($"Aucune note dans {Nom()} pour l'instant.");

            try
            {
                string[] lignes = await File.ReadAllLinesAsync(fichier, annulation);
                string[] notes = [.. lignes.Where(l => l.StartsWith("- ", StringComparison.Ordinal))
                    .TakeLast(NotesMax)
                    .Select(l => l.Length > LongueurNoteMax + 2 ? l[2..(LongueurNoteMax + 2)] + "…" : l[2..])];

                return Result.Ok(notes.Length == 0
                    ? $"Aucune note dans {Nom()} pour l'instant."
                    : $"Dernières notes {Du()} : {string.Join(" ; ", notes)}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Fail<string>(new Error($"lecture des notes impossible : {ex.Message}", ErrorType.Unexpected, exception: ex));
            }
        }

        private string Nom() => carnet == CatalogueCarnets.General ? "les notes générales" : $"le carnet {carnet}";

        private string Du() => carnet == CatalogueCarnets.General ? "des notes générales" : $"du carnet {carnet}";
    }
}
