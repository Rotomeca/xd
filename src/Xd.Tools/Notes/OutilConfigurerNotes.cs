using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.Notes;

/// <summary>
/// <c>configurer_notes</c> (niveau <see cref="NiveauOutil.Confirme"/>) : enregistre le dossier de notes que l'utilisateur vient d'indiquer.
/// </summary>
/// <remarks>
/// L'outil n'a <b>aucun paramètre de chemin</b> : le chemin est lu dans les paroles de l'utilisateur
/// (<see cref="ContexteAppel.Paroles"/>, la plus récente d'abord), validé, puis montré tel quel dans la question de confirmation
/// (invariant 5). À la voix, les raccourcis (« dans mes documents, dossier TEST », « par défaut ») remplacent un chemin dicté.
/// </remarks>
public sealed class OutilConfigurerNotes : IOutil
{
    // Une note demandée avant la configuration attend encore : le modèle doit la reprendre (nouvelle décision de sa part,
    // pas un nouvel essai automatique du code).
    private const string SuiteNote = "Si l'utilisateur avait demandé une note qui n'a pas encore été écrite, appelle maintenant noter_idee avec ce texte.";

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
        "Enregistre le dossier où ranger les notes, que l'utilisateur vient d'indiquer. Cet outil n'a pas de paramètre de chemin : "
        + "Xd lit le dossier directement dans les paroles de l'utilisateur. Appelle-le juste après qu'il a indiqué un dossier.";

    /// <inheritdoc/>
    public JsonElement SchemaParametres { get; } = JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>(),
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
            return Invalide($"paramètre inconnu : « {propriete.Name} ». Cet outil ne prend pas de chemin : il le lit dans les paroles de l'utilisateur.");

        // Les paroles les plus récentes de l'utilisateur d'abord (une réponse à une confirmation corrige la demande).
        var chemin = contexte.Paroles.Reverse()
            .Select(_lecture.Extraire)
            .FirstOrDefault(trouve => trouve.HasValue);
        if (chemin.IsEmpty || chemin.Value is not { } trouve)
        {
            return Invalide("je n'ai pas trouvé de dossier dans les paroles de l'utilisateur. Redemande-lui où ranger ses notes : "
                + "« dans mes documents », « dans mes documents, dossier Notes », « par défaut », ou un chemin complet s'il écrit au clavier.");
        }

        return _lecture.Valider(trouve).Match(
            Ok: valide => Result.Ok<IActionPreparee>(new ActionDossierNotes(valide, _lecture.Existe(valide), _configuration)),
            Err: erreur => Invalide($"{erreur.Message} Redemande le dossier à l'utilisateur."));
    }

    private static IResult<IActionPreparee> Invalide(string message) =>
        Result.Fail<IActionPreparee>(new Error(message, ErrorType.Validation));

    private sealed class ActionDossierNotes(string dossier, bool existe, IStockageConfigurationNotes stockage) : IActionPreparee
    {
        public IReadOnlyList<string> ElementsACiter => [dossier];

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

            IResult<string> resultat = stockage.Lire().Match(
                Ok: configuration => stockage.Ecrire(configuration with { DossierNotes = dossier }).Match(
                    Ok: _ => Result.Ok($"Dossier de notes enregistré : {dossier}. {SuiteNote}"),
                    Err: erreur => Result.Fail<string>(erreur)),
                Err: erreur => Result.Fail<string>(erreur));
            return Task.FromResult(resultat);
        }
    }
}
