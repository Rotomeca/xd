using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.Notes;

/// <summary>
/// <c>noter_idee</c> (niveau <see cref="NiveauOutil.Libre"/>) : ajoute une ligne horodatée à la fin d'un fichier de notes.
/// Ajout uniquement, jamais de réécriture ni de suppression.
/// </summary>
/// <remarks>
/// <para>
/// Destination obligatoire : <c>general</c> (notes générales) ou un projet connu. Elle doit être <b>citée dans la phrase
/// de l'utilisateur</b> (<see cref="ContexteAppel.DemandeUtilisateur"/>) : le modèle ne choisit jamais à sa place,
/// il redemande.
/// </para>
/// <para>
/// Fichiers : <c>{dossier}/general.md</c> et <c>{dossier}/projets/{projet}.md</c>. Le dossier vient de la configuration,
/// le nom de projet de la liste fermée des projets connus (invariant 5).
/// </para>
/// </remarks>
public sealed class OutilNoterIdee : IOutil
{
    internal const int LongueurMax = 1000;
    internal const string General = "general";
    private const string ParametreTexte = "texte";
    private const string ParametreDestination = "destination";

    private static readonly string[] FormulationsGenerales = ["general", "sans projet", "aucun projet", "divers"];

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
        "Note une idée ou un rappel de l'utilisateur. destination : « general » pour les notes générales, ou le nom d'un projet. "
        + "Si l'utilisateur n'a pas dit où ranger la note, demande-le-lui au lieu de choisir.";

    /// <inheritdoc/>
    /// <remarks>La liste des projets est relue à chaque fois : un dossier de projets ajouté apparaît aussitôt.</remarks>
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
                ["enum"] = Destinations(ProjetsConnus()),
                ["description"] = "Où ranger la note, telle que l'utilisateur l'a dit.",
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

        return _configuration.Lire().Match(
            Ok: configuration => PreparerAvec(configuration, parametres, texte, contexte),
            Err: erreur => Result.Fail<IActionPreparee>(erreur));
    }

    private IResult<IActionPreparee> PreparerAvec(ConfigurationNotes configuration, JsonElement parametres, string texte, ContexteAppel contexte)
    {
        if (string.IsNullOrWhiteSpace(configuration.DossierNotes))
        {
            return Invalide("aucun dossier de notes n'est configuré. Demande à l'utilisateur où ranger ses notes "
                + "(un chemin complet, « dans mes documents » ou « par défaut »), puis appelle configurer_notes.");
        }

        IReadOnlyList<string> destinations = Destinations(CatalogueProjets.Lister(configuration.DossiersProjets));
        string destination = parametres.TryGetProperty(ParametreDestination, out JsonElement valeur) && valeur.ValueKind == JsonValueKind.String
            ? valeur.GetString() ?? string.Empty
            : string.Empty;

        // Valeur exacte de la liste fermée (invariant 4) : elle devient un nom de fichier (invariant 5).
        if (!destinations.Contains(destination, StringComparer.Ordinal) || !CatalogueProjets.EstNomValide(destination))
            return Invalide($"destination inconnue : « {destination} ». {QuestionDestination(destinations)}");

        // La destination doit venir de l'utilisateur, pas d'une supposition du modèle.
        if (!EstCitee(contexte.DemandeUtilisateur, destination))
            return Invalide($"l'utilisateur n'a pas dit où ranger cette note. {QuestionDestination(destinations)} Ne choisis pas à sa place.");

        return Result.Ok<IActionPreparee>(destination == General
            ? new ActionNoter(Path.Combine(configuration.DossierNotes, "general.md"), configuration.DossierNotes, "les notes générales", texte, _horloge)
            : new ActionNoter(Path.Combine(configuration.DossierNotes, "projets", destination + ".md"), configuration.DossierNotes, $"les notes du projet {destination}", texte, _horloge));
    }

    /// <summary>La phrase de l'utilisateur nomme-t-elle cette destination (accents et casse ignorés, mot entier) ?</summary>
    internal static bool EstCitee(string phrase, string destination)
    {
        string normalisee = Normaliser(phrase);
        return destination == General
            ? FormulationsGenerales.Any(f => ContientMot(normalisee, f, motEntier: false))
            : ContientMot(normalisee, Normaliser(destination), motEntier: true);
    }

    /// <summary>Une note = une ligne : retours à la ligne et caractères de contrôle remplacés par des espaces.</summary>
    internal static string Aplatir(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char caractere in texte)
            resultat.Append(char.IsControl(caractere) ? ' ' : caractere);

        return string.Join(' ', resultat.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string QuestionDestination(IReadOnlyList<string> destinations) => destinations.Count > 1
        ? $"Demande-lui : dans les notes générales ou dans un projet ({string.Join(", ", destinations.Skip(1))}) ?"
        : "Demande-lui s'il veut la mettre dans les notes générales.";

    // « general » en premier ; un projet nommé « general » se confondrait avec les notes générales.
    private static string[] Destinations(IReadOnlyList<string> projets) =>
        [General, .. projets.Where(p => !string.Equals(p, General, StringComparison.OrdinalIgnoreCase))];

    // « general » couvre « générale(s) » : un préfixe suffit. Un projet doit être cité en mot entier (« Xd » n'est pas « xdebug »).
    private static bool ContientMot(string texte, string mot, bool motEntier) =>
        Regex.IsMatch(texte, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(mot)}" + (motEntier ? @"(?![\p{L}\p{N}])" : string.Empty));

    private static string Normaliser(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char caractere in texte.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
                resultat.Append(caractere);
        }

        return resultat.ToString();
    }

    private IReadOnlyList<string> ProjetsConnus() =>
        _configuration.Lire().Match(Ok: c => CatalogueProjets.Lister(c.DossiersProjets), Err: _ => []);

    private static IResult<IActionPreparee> Invalide(string message) =>
        Result.Fail<IActionPreparee>(new Error(message, ErrorType.Validation));

    private sealed class ActionNoter(string fichier, string dossierNotes, string destination, string texte, TimeProvider horloge) : IActionPreparee
    {
        public string Reformulation => $"Je note dans {destination} : « {texte} ».";

        public async Task<IResult<string>> ExecuterAsync(CancellationToken annulation)
        {
            // Le dossier de notes a pu être supprimé ou débranché : on ne le recrée pas sans l'utilisateur.
            if (!Directory.Exists(dossierNotes))
            {
                return Result.Fail<string>(new Error(
                    $"le dossier de notes « {dossierNotes} » est introuvable. Demande à l'utilisateur s'il faut en choisir un autre.",
                    ErrorType.NotFound));
            }

            string horodatage = horloge.GetLocalNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fichier) ?? dossierNotes);
                string entete = File.Exists(fichier) ? string.Empty : $"# {Path.GetFileNameWithoutExtension(fichier)}{Environment.NewLine}{Environment.NewLine}";

                // Ajout en fin de fichier uniquement.
                await File.AppendAllTextAsync(fichier, $"{entete}- {horodatage} — {texte}{Environment.NewLine}", annulation);
                return Result.Ok($"Noté dans {destination}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Result.Fail<string>(new Error($"écriture de la note impossible : {ex.Message}", ErrorType.Unexpected, exception: ex));
            }
        }
    }
}
