using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rotomeca.Core.Optionals;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Outils;
using Xd.Core.Permissions;

namespace Xd.Core.Execution;

/// <summary>
/// Seul point de passage entre une demande du modèle et l'exécution d'un outil :
/// validation → confirmation si <see cref="NiveauOutil.Confirme"/> (ou si l'action l'exige) → journal d'audit → exécution.
/// Ne lève jamais d'exception : tout échec devient un <see cref="ResultatOutil"/> en erreur.
/// </summary>
/// <remarks>
/// <para>
/// Le journal est écrit avec <see cref="CancellationToken.None"/> pour qu'un refus dû à une annulation
/// (mise en veille) soit tout de même journalisé. Si une entrée ne peut pas être écrite avant l'exécution,
/// l'action n'est pas exécutée.
/// </para>
/// <para>
/// Confirmation : le modèle peut rédiger la question (paramètre <see cref="ParametreQuestion"/>, ajouté à chaque schéma) ;
/// elle n'est affichée que si elle cite les <see cref="IActionPreparee.ElementsACiter"/>, sinon la
/// <see cref="IActionPreparee.Reformulation"/> la remplace. La réponse est interprétée ici, jamais par le modèle :
/// oui franc → exécution, non franc → refus, autre chose → refus et réponse rendue au modèle comme nouvelle consigne.
/// </para>
/// </remarks>
public sealed class ExecuteurOutils
{
    /// <summary>Paramètre ajouté à chaque outil : la question de confirmation rédigée par le modèle.</summary>
    public const string ParametreQuestion = "question_confirmation";

    private const int LongueurQuestionMax = 250;
    private const string RefusParDefaut = "L'utilisateur a refusé l'action.";

    private readonly IReadOnlyDictionary<string, IOutil> _outils;
    private readonly IReadOnlyList<IOutil> _ordre;
    private readonly IConfirmateur _confirmateur;
    private readonly IJournalAudit _journal;
    private readonly TimeProvider _horloge;

    public ExecuteurOutils(IEnumerable<IOutil> outils, IConfirmateur confirmateur, IJournalAudit journal, TimeProvider horloge)
    {
        var parNom = new Dictionary<string, IOutil>(StringComparer.Ordinal);
        var ordre = new List<IOutil>();
        foreach (IOutil outil in outils)
        {
            if (!parNom.TryAdd(outil.Nom, outil))
                throw new ArgumentException($"Deux outils portent le nom « {outil.Nom} ».", nameof(outils));

            ordre.Add(outil);
        }

        _outils = parNom;
        _ordre = ordre;
        _confirmateur = confirmateur;
        _journal = journal;
        _horloge = horloge;
    }

    /// <summary>
    /// Outils exposés au modèle : exactement ceux que cet exécuteur sait traiter, dans un ordre fixe, chacun avec le
    /// paramètre facultatif <see cref="ParametreQuestion"/>. Recalculés à chaque lecture, car un schéma peut dépendre de la configuration.
    /// </summary>
    public IReadOnlyList<DefinitionOutil> Definitions =>
        [.. _ordre.Select(o => new DefinitionOutil(o.Nom, o.Description, AvecQuestion(o.SchemaParametres)))];

    /// <summary>Traite une demande d'outil de bout en bout.</summary>
    /// <param name="appel">Demande émise par le modèle.</param>
    /// <param name="annulation">Annule la confirmation en attente ou l'action en cours.</param>
    public async Task<ResultatOutil> ExecuterAsync(AppelOutil appel, CancellationToken annulation)
    {
        if (!await JournaliserAsync(appel, EtapeAudit.Demande, null))
            return Erreur(appel, "Journal d'audit indisponible, action non exécutée.");

        if (!_outils.TryGetValue(appel.NomOutil, out IOutil? outil))
            return await TerminerEnErreurAsync(appel, EtapeAudit.Rejete, $"Outil inconnu : {appel.NomOutil}.");

        (JsonElement parametres, MayBe<string> question) = SeparerQuestion(appel.Parametres);
        return await Preparer(outil, appel, parametres).Match(
            Ok: action => ConfirmerPuisExecuterAsync(outil, action, appel, question, annulation),
            Err: erreur => TerminerEnErreurAsync(appel, EtapeAudit.Rejete, $"Paramètres invalides : {erreur.Message}"));
    }

    private static IResult<IActionPreparee> Preparer(IOutil outil, AppelOutil appel, JsonElement parametres)
    {
        try
        {
            var contexte = new ContexteAppel(appel.DemandeUtilisateur) { ReponsesUtilisateur = appel.ReponsesUtilisateur };
            return outil.Preparer(parametres, contexte);
        }
        catch (Exception ex)
        {
            return Result.Fail<IActionPreparee>(new Error(ex.Message, ErrorType.Validation, exception: ex));
        }
    }

    private async Task<ResultatOutil> ConfirmerPuisExecuterAsync(
        IOutil outil, IActionPreparee action, AppelOutil appel, MayBe<string> questionModele, CancellationToken annulation)
    {
        if (ExigeConfirmation(outil, action))
        {
            string question = ChoisirQuestion(questionModele, action);
            Decision decision = await DemanderConfirmationAsync(question, action, annulation);
            if (!decision.Confirme)
            {
                await JournaliserAsync(appel, EtapeAudit.Refuse, decision.Motif);
                return Erreur(appel, decision.Motif) with { ReponseUtilisateur = decision.ReponseLibre };
            }

            if (!await JournaliserAsync(appel, EtapeAudit.Confirme, question))
                return Erreur(appel, "Journal d'audit indisponible, action non exécutée.");
        }

        (EtapeAudit etape, string contenu, bool estErreur) = await ExecuterSansExceptionAsync(action, annulation);
        await JournaliserAsync(appel, etape, contenu);
        return new ResultatOutil(appel.IdAppel, contenu, estErreur);
    }

    /// <summary>Issue d'une confirmation : confirmée, ou refusée avec le motif renvoyé au modèle.</summary>
    private sealed record Decision(bool Confirme, string Motif, MayBe<string> ReponseLibre);

    private async Task<Decision> DemanderConfirmationAsync(string question, IActionPreparee action, CancellationToken annulation)
    {
        try
        {
            MayBe<string> reponse = await _confirmateur.PoserQuestionAsync(question, annulation);
            if (reponse.IsEmpty || reponse.Value is not { } texte)
                return new Decision(false, "Pas de réponse de l'utilisateur, action non exécutée.", MayBe<string>.Null);

            return InterpretationConfirmation.Interpreter(texte) switch
            {
                ReponseConfirmation.Oui => new Decision(true, string.Empty, MayBe<string>.Null),
                ReponseConfirmation.Non => new Decision(false, MessageSiRefus(action), MayBe<string>.Null),
                _ => new Decision(
                    false,
                    $"L'utilisateur n'a pas confirmé ; il a répondu : « {texte.Trim()} ». Rien n'a été fait. {MessageSiRefus(action)} Tiens compte de sa réponse.",
                    texte.Trim()),
            };
        }
        catch (OperationCanceledException)
        {
            return new Decision(false, "Confirmation annulée, action non exécutée.", MayBe<string>.Null);
        }
        catch (Exception ex)
        {
            return new Decision(false, $"Confirmation impossible ({ex.Message}), action non exécutée.", MayBe<string>.Null);
        }
    }

    /// <summary>
    /// Question du modèle si elle cite tous les éléments clés de l'action vérifiée ; sinon la reformulation de l'action.
    /// Le modèle ne peut donc pas faire confirmer une chose en en montrant une autre.
    /// </summary>
    private static string ChoisirQuestion(MayBe<string> questionModele, IActionPreparee action)
    {
        string reformulation = action.Reformulation;
        if (questionModele.IsEmpty || questionModele.Value is not { } brute)
            return reformulation;

        string question = string.Join(' ', brute.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        IReadOnlyList<string> elements;
        try
        {
            elements = action.ElementsACiter;
        }
        catch (Exception)
        {
            return reformulation;
        }

        string normalisee = Normaliser(question);
        bool citeTout = elements.Count > 0 && elements.All(e => normalisee.Contains(Normaliser(e), StringComparison.Ordinal));
        return citeTout && question.Length is > 0 and <= LongueurQuestionMax ? question : reformulation;
    }

    // Au moindre doute (propriété qui lève), on demande : une confirmation de trop vaut mieux qu'une de moins.
    private static bool ExigeConfirmation(IOutil outil, IActionPreparee action)
    {
        if (outil.Niveau == NiveauOutil.Confirme)
            return true;

        try
        {
            return action.ConfirmationRequise;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static string MessageSiRefus(IActionPreparee action)
    {
        try
        {
            return string.IsNullOrWhiteSpace(action.MessageSiRefus) ? RefusParDefaut : action.MessageSiRefus;
        }
        catch (Exception)
        {
            return RefusParDefaut;
        }
    }

    /// <summary>Retire <see cref="ParametreQuestion"/> des paramètres avant validation par l'outil, qui ne le connaît pas.</summary>
    private static (JsonElement Parametres, MayBe<string> Question) SeparerQuestion(JsonElement parametres)
    {
        if (parametres.ValueKind != JsonValueKind.Object || !parametres.TryGetProperty(ParametreQuestion, out JsonElement question))
            return (parametres, MayBe<string>.Null);

        var sansQuestion = new Dictionary<string, JsonElement>();
        foreach (JsonProperty propriete in parametres.EnumerateObject())
        {
            if (propriete.Name != ParametreQuestion)
                sansQuestion[propriete.Name] = propriete.Value;
        }

        MayBe<string> texte = question.ValueKind == JsonValueKind.String ? question.GetString() ?? string.Empty : MayBe<string>.Null;
        return (JsonSerializer.SerializeToElement(sansQuestion), texte);
    }

    private static JsonElement AvecQuestion(JsonElement schema)
    {
        try
        {
            if (JsonNode.Parse(schema.GetRawText()) is not JsonObject objet || objet["properties"] is not JsonObject proprietes)
                return schema;

            proprietes[ParametreQuestion] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Seulement si l'action peut demander une confirmation : la question courte à poser à l'utilisateur, "
                    + "dans ton style, en citant précisément ce qui va être fait (destination, nom, chemin…).",
            };
            return JsonSerializer.SerializeToElement(objet);
        }
        catch (JsonException)
        {
            return schema;
        }
    }

    /// <summary>Minuscules sans accents, pour vérifier qu'une question cite un élément.</summary>
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

    private static async Task<(EtapeAudit Etape, string Contenu, bool EstErreur)> ExecuterSansExceptionAsync(IActionPreparee action, CancellationToken annulation)
    {
        try
        {
            IResult<string> resultat = await action.ExecuterAsync(annulation);
            return resultat.Match(
                Ok: contenu => (EtapeAudit.Execute, contenu, false),
                Err: erreur => (EtapeAudit.Echec, erreur.Message, true));
        }
        catch (OperationCanceledException)
        {
            return (EtapeAudit.Echec, "Action annulée.", true);
        }
        catch (Exception ex)
        {
            return (EtapeAudit.Echec, $"Erreur pendant l'exécution : {ex.Message}", true);
        }
    }

    private async Task<ResultatOutil> TerminerEnErreurAsync(AppelOutil appel, EtapeAudit etape, string motif)
    {
        await JournaliserAsync(appel, etape, motif);
        return Erreur(appel, motif);
    }

    private async Task<bool> JournaliserAsync(AppelOutil appel, EtapeAudit etape, string? detail)
    {
        var entree = new EntreeAudit(_horloge.GetUtcNow(), appel.IdAppel, appel.NomOutil, appel.Parametres, appel.DemandeUtilisateur, etape, detail);
        try
        {
            IResult<bool> ecriture = await _journal.EcrireAsync(entree, CancellationToken.None);
            return ecriture.IsSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ResultatOutil Erreur(AppelOutil appel, string message) => new(appel.IdAppel, message, true);
}
