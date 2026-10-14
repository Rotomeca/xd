using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.Notes;

/// <summary>
/// <c>configurer_notes</c> (niveau <see cref="NiveauOutil.Confirme"/>) : enregistre le dossier de notes ou un dossier de projets
/// que l'utilisateur vient d'indiquer.
/// </summary>
/// <remarks>
/// L'outil n'a <b>aucun paramètre de chemin</b> : le chemin est lu dans la phrase de l'utilisateur
/// (<see cref="ContexteAppel.DemandeUtilisateur"/>), validé, puis montré tel quel dans la question de confirmation (invariant 5).
/// Le modèle choisit seulement quel dossier l'utilisateur vient d'indiquer.
/// </remarks>
public sealed class OutilConfigurerNotes : IOutil
{
    private const string ParametreElement = "element";
    private const int ProjetsCitesMax = 5;

    // Une note demandée avant la configuration attend encore : le modèle doit la reprendre (nouvelle décision de sa part,
    // pas un nouvel essai automatique du code).
    private const string SuiteNote = "Si l'utilisateur avait demandé une note qui n'a pas encore été écrite, appelle maintenant noter_idee avec ce texte.";

    private static readonly IReadOnlyDictionary<string, ElementNotes> Elements = new Dictionary<string, ElementNotes>(StringComparer.Ordinal)
    {
        ["dossier_notes"] = ElementNotes.DossierNotes,
        ["dossier_projets"] = ElementNotes.DossierProjets,
    };

    private readonly IStockageConfigurationNotes _configuration;
    private readonly LecturePhraseChemin _lecture;

    public OutilConfigurerNotes(IStockageConfigurationNotes configuration, LecturePhraseChemin lecture)
    {
        _configuration = configuration;
        _lecture = lecture;
    }

    /// <inheritdoc/>
    public string Nom => "configurer_notes";

    /// <inheritdoc/>
    public string Description =>
        "Enregistre un dossier que l'utilisateur vient d'indiquer dans sa phrase : le dossier où ranger ses notes (dossier_notes) "
        + "ou un dossier qui contient ses projets (dossier_projets). Cet outil n'a pas de paramètre de chemin : "
        + "Xd lit le chemin directement dans la phrase de l'utilisateur. Appelle-le juste après que l'utilisateur a indiqué un dossier.";

    /// <inheritdoc/>
    public JsonElement SchemaParametres { get; } = JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            [ParametreElement] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = Elements.Keys.ToArray(),
                ["description"] = "Quel dossier l'utilisateur vient d'indiquer.",
            },
        },
        ["required"] = new[] { ParametreElement },
        ["additionalProperties"] = false,
    });

    /// <inheritdoc/>
    public NiveauOutil Niveau => NiveauOutil.Confirme;

    /// <inheritdoc/>
    public IResult<IActionPreparee> Preparer(JsonElement parametres, ContexteAppel contexte)
    {
        if (parametres.ValueKind != JsonValueKind.Object)
            return Invalide("les paramètres doivent être un objet JSON.");

        foreach (JsonProperty propriete in parametres.EnumerateObject())
        {
            if (propriete.Name != ParametreElement)
                return Invalide($"paramètre inconnu : « {propriete.Name} ». Cet outil ne prend pas de chemin : il le lit dans la phrase de l'utilisateur.");
        }

        if (!parametres.TryGetProperty(ParametreElement, out JsonElement valeur) || valeur.ValueKind != JsonValueKind.String
            || !Elements.TryGetValue(valeur.GetString() ?? string.Empty, out ElementNotes element))
            return Invalide($"le paramètre « {ParametreElement} » doit valoir {string.Join(" ou ", Elements.Keys)}.");

        var chemin = _lecture.Extraire(contexte.DemandeUtilisateur, element);
        if (chemin.IsEmpty || chemin.Value is not { } trouve)
        {
            return Invalide(element == ElementNotes.DossierNotes
                ? "je n'ai pas trouvé de chemin dans la phrase de l'utilisateur. Redemande-lui où ranger ses notes : "
                  + "un chemin complet comme D:\\Notes, « dans mes documents » ou « par défaut »."
                : "je n'ai pas trouvé de chemin dans la phrase de l'utilisateur. Redemande-lui le chemin complet du dossier de ses projets.");
        }

        return _lecture.Valider(trouve, element).Match(
            Ok: valide => Result.Ok<IActionPreparee>(element == ElementNotes.DossierNotes
                ? new ActionDossierNotes(valide, _lecture.Existe(valide), _configuration)
                : new ActionDossierProjets(valide, CatalogueProjets.Lister([valide]), _configuration)),
            Err: erreur => Invalide($"{erreur.Message} Redemande le dossier à l'utilisateur."));
    }

    private static IResult<IActionPreparee> Invalide(string message) =>
        Result.Fail<IActionPreparee>(new Error(message, ErrorType.Validation));

    private static IResult<string> Enregistrer(IStockageConfigurationNotes stockage, Func<ConfigurationNotes, ConfigurationNotes> modifier, string succes) =>
        stockage.Lire().Match(
            Ok: configuration => stockage.Ecrire(modifier(configuration)).Match(
                Ok: _ => Result.Ok(succes),
                Err: erreur => Result.Fail<string>(erreur)),
            Err: erreur => Result.Fail<string>(erreur));

    private sealed class ActionDossierNotes(string dossier, bool existe, IStockageConfigurationNotes stockage) : IActionPreparee
    {
        public string Reformulation => existe
            ? $"Je range tes notes dans « {dossier} ». Je confirme ?"
            : $"Je range tes notes dans « {dossier} », un nouveau dossier que je vais créer. Je confirme ?";

        public Task<IResult<string>> ExecuterAsync(CancellationToken annulation)
        {
            try
            {
                Directory.CreateDirectory(dossier);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(Result.Fail<string>(new Error($"création du dossier « {dossier} » impossible : {ex.Message}", ErrorType.Unexpected, exception: ex)));
            }

            return Task.FromResult(Enregistrer(stockage, c => c with { DossierNotes = dossier }, $"Dossier de notes enregistré : {dossier}. {SuiteNote}"));
        }
    }

    private sealed class ActionDossierProjets(string dossier, IReadOnlyList<string> projets, IStockageConfigurationNotes stockage) : IActionPreparee
    {
        public string Reformulation =>
            $"J'ajoute « {dossier} » comme dossier de projets ({Decrire(projets)}). Je confirme ?";

        public Task<IResult<string>> ExecuterAsync(CancellationToken annulation)
        {
            StringComparer comparaison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            return Task.FromResult(Enregistrer(
                stockage,
                c => c.DossiersProjets.Contains(dossier, comparaison) ? c : c with { DossiersProjets = [.. c.DossiersProjets, dossier] },
                $"Dossier de projets ajouté : {dossier} ({Decrire(projets)}). {SuiteNote}"));
        }

        private static string Decrire(IReadOnlyList<string> projets) => projets.Count switch
        {
            0 => "aucun projet dedans pour l'instant",
            1 => $"1 projet : {projets[0]}",
            <= ProjetsCitesMax => $"{projets.Count} projets : {string.Join(", ", projets)}",
            _ => $"{projets.Count} projets, dont {string.Join(", ", projets.Take(ProjetsCitesMax))}",
        };
    }
}
