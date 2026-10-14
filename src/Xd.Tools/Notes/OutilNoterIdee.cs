using System.Globalization;
using System.Text;
using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Outils;

namespace Xd.Tools.Notes;

/// <summary>
/// <c>noter_idee</c> (niveau <see cref="NiveauOutil.Libre"/>) : ajoute une ligne horodatée à la fin d'un fichier de notes.
/// Ajout uniquement, jamais de réécriture ni de suppression. Sans projet, la note va dans les notes générales.
/// </summary>
/// <remarks>
/// Fichiers : <c>{dossier}/general.md</c> et <c>{dossier}/projets/{projet}.md</c>. Le dossier vient de la configuration,
/// le nom de projet de la liste fermée des projets connus (invariant 5).
/// </remarks>
public sealed class OutilNoterIdee : IOutil
{
    internal const int LongueurMax = 1000;
    private const string ParametreTexte = "texte";
    private const string ParametreProjet = "projet";

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
        "Note une idée ou un rappel de l'utilisateur. Sans projet, la note va dans les notes générales ; "
        + "le projet est facultatif et doit être l'un des projets connus.";

    /// <inheritdoc/>
    /// <remarks>La liste des projets est relue à chaque fois : un dossier de projets ajouté apparaît aussitôt.</remarks>
    public JsonElement SchemaParametres
    {
        get
        {
            var proprietes = new Dictionary<string, object>
            {
                [ParametreTexte] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Texte de la note, reformulé brièvement si besoin.",
                },
            };

            IReadOnlyList<string> projets = ProjetsConnus();
            if (projets.Count > 0)
            {
                proprietes[ParametreProjet] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = projets,
                    ["description"] = "Projet concerné, seulement si l'utilisateur en nomme un.",
                };
            }

            return JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = proprietes,
                ["required"] = new[] { ParametreTexte },
                ["additionalProperties"] = false,
            });
        }
    }

    /// <inheritdoc/>
    public NiveauOutil Niveau => NiveauOutil.Libre;

    /// <inheritdoc/>
    public IResult<IActionPreparee> Preparer(JsonElement parametres, ContexteAppel contexte)
    {
        if (parametres.ValueKind != JsonValueKind.Object)
            return Invalide("les paramètres doivent être un objet JSON.");

        foreach (JsonProperty propriete in parametres.EnumerateObject())
        {
            if (propriete.Name is not ParametreTexte and not ParametreProjet)
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
            Ok: configuration => PreparerAvec(configuration, parametres, texte),
            Err: erreur => Result.Fail<IActionPreparee>(erreur));
    }

    private IResult<IActionPreparee> PreparerAvec(ConfigurationNotes configuration, JsonElement parametres, string texte)
    {
        if (string.IsNullOrWhiteSpace(configuration.DossierNotes))
        {
            return Invalide("aucun dossier de notes n'est configuré. Demande à l'utilisateur où ranger ses notes "
                + "(un chemin complet, « dans mes documents » ou « par défaut »), puis appelle configurer_notes.");
        }

        if (!parametres.TryGetProperty(ParametreProjet, out JsonElement valeurProjet))
            return Result.Ok<IActionPreparee>(new ActionNoter(Path.Combine(configuration.DossierNotes, "general.md"), configuration.DossierNotes, "les notes générales", texte, _horloge));

        IReadOnlyList<string> projets = CatalogueProjets.Lister(configuration.DossiersProjets);
        string projet = valeurProjet.ValueKind == JsonValueKind.String ? valeurProjet.GetString() ?? string.Empty : string.Empty;

        // Nom exact de la liste fermée uniquement (invariant 4) : il devient un nom de fichier (invariant 5).
        if (!projets.Contains(projet, StringComparer.Ordinal) || !CatalogueProjets.EstNomValide(projet))
        {
            return Invalide(projets.Count == 0
                ? $"projet inconnu : « {projet} ». Aucun dossier de projets n'est déclaré : la note peut aller dans les notes générales (sans projet)."
                : $"projet inconnu : « {projet} ». Projets connus : {string.Join(", ", projets)}. Sans projet, la note va dans les notes générales.");
        }

        string fichier = Path.Combine(configuration.DossierNotes, "projets", projet + ".md");
        return Result.Ok<IActionPreparee>(new ActionNoter(fichier, configuration.DossierNotes, $"les notes du projet {projet}", texte, _horloge));
    }

    /// <summary>Une note = une ligne : retours à la ligne et caractères de contrôle remplacés par des espaces.</summary>
    internal static string Aplatir(string texte)
    {
        var resultat = new StringBuilder(texte.Length);
        foreach (char caractere in texte)
            resultat.Append(char.IsControl(caractere) ? ' ' : caractere);

        return string.Join(' ', resultat.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
