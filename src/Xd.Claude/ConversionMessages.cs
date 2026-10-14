using System.Text.Json;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Xd.Core.Boucle;
using Xd.Core.Outils;
using MessageXd = Xd.Core.Boucle.Message;

namespace Xd.Claude;

/// <summary>
/// Traduction entre les messages de <c>Xd.Core</c> et les types du SDK Anthropic.
/// </summary>
internal static class ConversionMessages
{
    public static List<MessageParam> VersParametres(IReadOnlyList<MessageXd> historique) =>
        [.. historique.Select(VersParametre)];

    public static List<ToolUnion> VersOutils(IReadOnlyList<DefinitionOutil> outils) =>
        [.. outils.Select(o => (ToolUnion)new Tool
        {
            Name = o.Nom,
            Description = o.Description,
            InputSchema = InputSchema.FromRawUnchecked(VersDictionnaire(o.SchemaParametres)),
        })];

    public static ReponseModele VersReponse(Anthropic.Models.Messages.Message message)
    {
        var blocs = new List<BlocAssistant>();
        foreach (ContentBlock bloc in message.Content)
        {
            if (bloc.TryPickText(out TextBlock? texte))
                blocs.Add(new BlocTexte(texte.Text));
            else if (bloc.TryPickToolUse(out ToolUseBlock? appel))
                blocs.Add(new BlocAppelOutil(appel.ID, appel.Name, JsonSerializer.SerializeToElement(appel.Input)));
        }

        return new ReponseModele(blocs, VersRaisonArret(message.StopReason));
    }

    private static MessageParam VersParametre(MessageXd message) => message switch
    {
        MessageUtilisateur utilisateur => new MessageParam { Role = Role.User, Content = utilisateur.Texte },
        MessageAssistant assistant => new MessageParam { Role = Role.Assistant, Content = assistant.Blocs.Select(VersBlocParametre).ToList() },
        MessageResultatsOutils resultats => new MessageParam
        {
            Role = Role.User,
            Content = resultats.Resultats
                .Select(r => (ContentBlockParam)new ToolResultBlockParam { ToolUseID = r.IdAppel, Content = r.Contenu, IsError = r.EstErreur })
                .ToList(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Type de message inconnu."),
    };

    private static ContentBlockParam VersBlocParametre(BlocAssistant bloc) => bloc switch
    {
        BlocTexte texte => new TextBlockParam { Text = texte.Texte },
        BlocAppelOutil appel => new ToolUseBlockParam { ID = appel.IdAppel, Name = appel.NomOutil, Input = VersDictionnaire(appel.Parametres) },
        _ => throw new ArgumentOutOfRangeException(nameof(bloc), bloc.GetType().Name, "Type de bloc inconnu."),
    };

    // Valeur brute : une raison inconnue du SDK ne doit pas lever d'exception.
    private static RaisonArret VersRaisonArret(ApiEnum<string, StopReason>? raison) => raison?.Raw() switch
    {
        "end_turn" or "stop_sequence" => RaisonArret.Fin,
        "tool_use" => RaisonArret.AppelOutils,
        "max_tokens" => RaisonArret.LimiteJetons,
        "refusal" => RaisonArret.Refus,
        _ => RaisonArret.Autre,
    };

    private static Dictionary<string, JsonElement> VersDictionnaire(JsonElement objet) =>
        objet.ValueKind == JsonValueKind.Object
            ? objet.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())
            : [];
}
