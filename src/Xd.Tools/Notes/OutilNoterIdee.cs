using System.Globalization;
using System.Text;
using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.Notes;

/// <summary>
/// <c>noter_idee</c> (niveau <see cref="NiveauOutil.Libre"/>) : ajoute une ligne horodatée à la fin d'un carnet de notes.
/// Ajout uniquement, jamais de réécriture ni de suppression.
/// </summary>
/// <remarks>
/// <para>
/// Destination : <c>general</c>, un carnet existant, ou un <b>nouveau carnet</b> dont le nom vient des paroles de l'utilisateur
/// (vérifié, comme un chemin : invariant 5) ; créer un carnet demande toujours confirmation.
/// </para>
/// <para>
/// Une destination existante que l'utilisateur n'a pas citée est une proposition du modèle : l'action exige alors une confirmation,
/// et un refus renvoie au modèle la question à poser.
/// </para>
/// </remarks>
public sealed class OutilNoterIdee : IOutil
{
    internal const int LongueurMax = 1000;
    private const string ParametreTexte = "texte";
    private const string ParametreDestination = "destination";

    private readonly IStockageConfigurationNotes _configuration;
    private readonly TimeProvider _horloge;

    public OutilNoterIdee(IStockageConfigurationNotes configuration, TimeProvider horloge)
    {
        _configuration = configuration;
        _horloge = horloge;
    }

    /// <inheritdoc/>
    public string Nom => "noter_idee";

    /// <inheritdoc/>
    public string Description =>
        "Note une idée ou un rappel de l'utilisateur dans un carnet : « general » pour les notes générales, un carnet existant, "
        + "ou un nouveau carnet si l'utilisateur en nomme un (il sera créé après confirmation). "
        + "Si l'utilisateur n'a pas dit où ranger la note, propose « general » : il devra confirmer.";

    /// <inheritdoc/>
    /// <remarks>
    /// Pas d'énumération : un nouveau carnet a un nom libre, validé dans le code. Les carnets existants sont relus à chaque fois.
    /// </remarks>
    public JsonElement SchemaParametres => JsonSerializer.SerializeToElement(new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            [ParametreTexte] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "Texte de la note, reformulé brièvement si besoin.",
            },
            [ParametreDestination] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = $"Carnet, tel que l'utilisateur l'a nommé. Carnets existants : {string.Join(", ", CarnetsConnus())}.",
            },
        },
        ["required"] = new[] { ParametreTexte, ParametreDestination },
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
            if (propriete.Name is not ParametreTexte and not ParametreDestination)
                return Invalide($"paramètre inconnu : « {propriete.Name} ».");
        }

        if (!parametres.TryGetProperty(ParametreTexte, out JsonElement valeurTexte) || valeurTexte.ValueKind != JsonValueKind.String)
            return Invalide($"le paramètre « {ParametreTexte} » est obligatoire.");

        string texte = Aplatir(valeurTexte.GetString() ?? string.Empty);
        if (texte.Length == 0)
            return Invalide("la note est vide.");
        if (texte.Length > LongueurMax)
            return Invalide($"la note dépasse {LongueurMax} caractères.");

        if (!parametres.TryGetProperty(ParametreDestination, out JsonElement valeurDestination) || valeurDestination.ValueKind != JsonValueKind.String
            || (valeurDestination.GetString() ?? string.Empty).Trim() is not { Length: > 0 } destination)
            return Invalide($"le paramètre « {ParametreDestination} » est obligatoire.");

        return _configuration.Lire().Match(
            Ok: configuration => PreparerAvec(configuration, texte, destination, contexte),
            Err: erreur => Result.Fail<IActionPreparee>(erreur));
    }

    private IResult<IActionPreparee> PreparerAvec(ConfigurationNotes configuration, string texte, string destination, ContexteAppel contexte)
    {
        if (string.IsNullOrWhiteSpace(configuration.DossierNotes) || configuration.DossierNotes is not { } dossier)
        {
            return Invalide("aucun dossier de notes n'est configuré. Demande à l'utilisateur où ranger ses notes "
                + "(« dans mes documents », « par défaut », ou un chemin complet au clavier), puis appelle configurer_notes.");
        }

        IReadOnlyList<string> carnets = CatalogueCarnets.Lister(dossier);
        string siRefus = $"L'utilisateur a refusé ce carnet. {QuestionCarnet(carnets)}";
        var existant = CatalogueCarnets.Trouver(carnets, destination);

        if (existant.Value is { } carnet)
        {
            // Carnet non cité par l'utilisateur : c'est une proposition du modèle, que l'utilisateur doit valider.
            bool aConfirmer = !CatalogueCarnets.EstCite(contexte.Paroles, carnet);
            return Result.Ok<IActionPreparee>(new ActionNoter(dossier, carnet, Libelle(carnet), texte, Creation: false, aConfirmer, siRefus, _horloge));
        }

        // Nouveau carnet : son nom devient un nom de fichier, il doit venir de l'utilisateur (invariant 5).
        if (!CatalogueCarnets.EstNomValide(destination))
            return Invalide($"« {destination} » ne peut pas servir de nom de carnet. {QuestionCarnet(carnets)}");
        if (!CatalogueCarnets.EstCite(contexte.Paroles, destination))
            return Invalide($"l'utilisateur n'a pas nommé de carnet « {destination} ». {QuestionCarnet(carnets)} Ne choisis pas le nom à sa place.");

        return Result.Ok<IActionPreparee>(new ActionNoter(dossier, destination, Libelle(destination), texte, Creation: true, AConfirmer: true, siRefus, _horloge));
    }

    /// <summary>Une note = une ligne : retours à la ligne et caractères de contrôle remplacés par des espaces.</summary>
    internal static string Aplatir(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char caractere in texte)
            resultat.Append(char.IsControl(caractere) ? ' ' : caractere);

        return string.Join(' ', resultat.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Libelle(string carnet) => carnet == CatalogueCarnets.General ? "les notes générales" : $"le carnet {carnet}";

    private static string QuestionCarnet(IReadOnlyList<string> carnets) => carnets.Count > 1
        ? $"Demande-lui : dans les notes générales, dans un carnet existant ({string.Join(", ", carnets.Skip(1))}) ou dans un nouveau carnet ?"
        : "Demande-lui : dans les notes générales ou dans un nouveau carnet (et lequel) ?";

    private IReadOnlyList<string> CarnetsConnus() =>
        _configuration.Lire().Match(
            Ok: c => c.DossierNotes is { } dossier ? CatalogueCarnets.Lister(dossier) : [CatalogueCarnets.General],
            Err: _ => [CatalogueCarnets.General]);

    private static IResult<IActionPreparee> Invalide(string message) =>
        Result.Fail<IActionPreparee>(new Error(message, ErrorType.Validation));

    private sealed record ActionNoter(
        string DossierNotes, string Carnet, string Libelle, string Texte, bool Creation, bool AConfirmer, string SiRefus, TimeProvider Horloge)
        : IActionPreparee
    {
        public string Reformulation => (Creation, AConfirmer) switch
        {
            (true, _) => $"Je crée le carnet « {Carnet} » et j'y note « {Texte} ». Je confirme ?",
            (false, true) => $"Je note « {Texte} » dans {Libelle}. Je confirme ?",
            _ => $"Je note dans {Libelle} : « {Texte} ».",
        };

        public bool ConfirmationRequise => AConfirmer;

        public string MessageSiRefus => SiRefus;

        // Une question rédigée par le modèle doit nommer le carnet (« générales » ou son nom).
        public IReadOnlyList<string> ElementsACiter => [Carnet == CatalogueCarnets.General ? "générales" : Carnet];

        public async Task<IResult<string>> ExecuterAsync(CancellationToken annulation)
        {
            // Le dossier de notes a pu être supprimé ou débranché : on ne le recrée pas sans l'utilisateur.
            if (!Directory.Exists(DossierNotes))
            {
                return Result.Fail<string>(new Error(
                    $"le dossier de notes « {DossierNotes} » est introuvable. Demande à l'utilisateur s'il faut en choisir un autre.",
                    ErrorType.NotFound));
            }

            string fichier = CatalogueCarnets.Fichier(DossierNotes, Carnet);
            string horodatage = Horloge.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fichier) ?? DossierNotes);
                string entete = File.Exists(fichier) ? string.Empty : $"# {Carnet}{Environment.NewLine}{Environment.NewLine}";

                // Ajout en fin de fichier uniquement.
                await File.AppendAllTextAsync(fichier, $"{entete}- {horodatage} — {Texte}{Environment.NewLine}", annulation);
                return Result.Ok($"Noté dans {Libelle}, fichier : {fichier}. Donne ce chemin tel quel si l'utilisateur demande où est la note.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Fail<string>(new Error($"écriture de la note impossible : {ex.Message}", ErrorType.Unexpected, exception: ex));
            }
        }
    }
}
