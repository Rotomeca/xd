using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xd.Core.Boucle;
using Xd.Core.Outils;

namespace Xd.Local;

/// <summary>
/// Traduction entre les messages de <c>Xd.Core</c> et le format « chat completions » compatible OpenAI de <c>llama-server</c>.
/// </summary>
internal static class ConversionMessages
{
    /// <summary>
    /// Le format OpenAI n'a pas de champ <c>is_error</c> : un résultat en erreur est signalé dans son texte.
    /// </summary>
    internal const string PrefixeErreur = "ERREUR : ";

    private static readonly Regex BaliseReflexion = new(@"<think>.*?(</think>|$)", RegexOptions.Singleline);

    public static JsonObject VersRequete(string promptSysteme, int maxJetons, IReadOnlyList<Message> historique, IReadOnlyList<DefinitionOutil> outils)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = promptSysteme });
        foreach (Message message in historique)
        {
            foreach (JsonObject converti in VersMessages(message))
                messages.Add(converti);
        }

        var requete = new JsonObject
        {
            ["messages"] = messages,
            ["max_tokens"] = maxJetons,
            // Pas de raisonnement (latence vocale) ; ignoré par les gabarits qui ne le connaissent pas.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };
        if (outils.Count > 0)
            requete["tools"] = new JsonArray([.. outils.Select(VersOutil)]);

        return requete;
    }

    /// <summary>Lit le corps d'une réponse <c>/v1/chat/completions</c>.</summary>
    /// <exception cref="JsonException">Corps illisible ou sans <c>choices</c>.</exception>
    public static ReponseModele VersReponse(JsonElement corps)
    {
        JsonElement choix = corps.GetProperty("choices")[0];
        JsonElement message = choix.GetProperty("message");

        var blocs = new List<BlocAssistant>();
        if (message.TryGetProperty("content", out JsonElement contenu) && contenu.ValueKind == JsonValueKind.String)
        {
            string texte = SansReflexion(contenu.GetString() ?? string.Empty);
            if (texte.Length > 0)
                blocs.Add(new BlocTexte(texte));
        }

        if (message.TryGetProperty("tool_calls", out JsonElement appels) && appels.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement appel in appels.EnumerateArray())
                blocs.Add(VersBlocAppel(appel, index++));
        }

        string? raison = choix.TryGetProperty("finish_reason", out JsonElement fin) && fin.ValueKind == JsonValueKind.String ? fin.GetString() : null;
        return new ReponseModele(blocs, VersRaisonArret(raison, blocs.OfType<BlocAppelOutil>().Any()));
    }

    private static IEnumerable<JsonObject> VersMessages(Message message) => message switch
    {
        MessageUtilisateur utilisateur => [new JsonObject { ["role"] = "user", ["content"] = utilisateur.Texte }],
        MessageAssistant assistant => [VersMessageAssistant(assistant)],
        MessageResultatsOutils resultats => resultats.Resultats.Select(r => new JsonObject
        {
            ["role"] = "tool",
            ["tool_call_id"] = r.IdAppel,
            ["content"] = r.EstErreur ? PrefixeErreur + r.Contenu : r.Contenu,
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(message), message.GetType().Name, "Type de message inconnu."),
    };

    private static JsonObject VersMessageAssistant(MessageAssistant assistant)
    {
        string texte = string.Join(" ", assistant.Blocs.OfType<BlocTexte>().Select(b => b.Texte));
        BlocAppelOutil[] appels = [.. assistant.Blocs.OfType<BlocAppelOutil>()];

        var message = new JsonObject { ["role"] = "assistant", ["content"] = texte.Length > 0 ? texte : null };
        if (appels.Length > 0)
        {
            message["tool_calls"] = new JsonArray([.. appels.Select(a => new JsonObject
            {
                ["id"] = a.IdAppel,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = a.NomOutil,
                    // Le format OpenAI transporte les arguments sous forme de chaîne JSON.
                    ["arguments"] = a.Parametres.ValueKind == JsonValueKind.String ? a.Parametres.GetString() : a.Parametres.GetRawText(),
                },
            })]);
        }

        return message;
    }

    private static JsonObject VersOutil(DefinitionOutil outil) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = outil.Nom,
            ["description"] = outil.Description,
            ["parameters"] = JsonNode.Parse(outil.SchemaParametres.GetRawText()),
        },
    };

    private static BlocAppelOutil VersBlocAppel(JsonElement appel, int index)
    {
        string id = appel.TryGetProperty("id", out JsonElement valeurId) && valeurId.ValueKind == JsonValueKind.String && valeurId.GetString() is { Length: > 0 } texteId
            ? texteId
            : $"appel-{index}";

        JsonElement fonction = appel.GetProperty("function");
        string nom = fonction.GetProperty("name").GetString() ?? string.Empty;
        return new BlocAppelOutil(id, nom, LireArguments(fonction));
    }

    /// <summary>
    /// Arguments illisibles : renvoyés tels quels sous forme de chaîne, pour que la validation de l'outil
    /// les refuse et que le refus revienne au modèle (invariant 6), plutôt que d'interrompre la boucle.
    /// </summary>
    private static JsonElement LireArguments(JsonElement fonction)
    {
        if (!fonction.TryGetProperty("arguments", out JsonElement arguments))
            return JsonSerializer.SerializeToElement(new { });

        if (arguments.ValueKind != JsonValueKind.String)
            return arguments.Clone();

        string brut = arguments.GetString() ?? string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(brut);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(brut);
        }
    }

    // Avec des appels d'outils, la raison d'arrêt varie selon les modèles (« stop » ou « tool_calls ») :
    // la présence d'appels fait foi, sauf réponse tronquée.
    private static RaisonArret VersRaisonArret(string? raison, bool contientAppels) => (raison, contientAppels) switch
    {
        ("length", _) => RaisonArret.LimiteJetons,
        (_, true) => RaisonArret.AppelOutils,
        ("stop", false) => RaisonArret.Fin,
        _ => RaisonArret.Autre,
    };

    /// <summary>Retire un raisonnement <c>&lt;think&gt;…&lt;/think&gt;</c> laissé dans le texte par certains modèles.</summary>
    /// <remarks>
    /// Le gabarit peut ouvrir lui-même la balise <c>&lt;think&gt;</c> : la réponse ne contient alors que la fin du raisonnement
    /// et <c>&lt;/think&gt;</c>. Tout ce qui précède la dernière balise fermante est donc retiré.
    /// </remarks>
    private static string SansReflexion(string texte)
    {
        int fin = texte.LastIndexOf("</think>", StringComparison.Ordinal);
        string reponse = fin >= 0 ? texte[(fin + "</think>".Length)..] : texte;
        return BaliseReflexion.Replace(reponse, string.Empty).Trim();
    }
}
