using System.Text.Json;
using Anthropic.Models.Messages;
using Xd.Core.Boucle;
using Xd.Core.Execution;
using Xd.Core.Outils;
using MessageXd = Xd.Core.Boucle.Message;

namespace Xd.Claude.Tests;

public sealed class ConversionMessagesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement Serialiser<T>(T valeur) => JsonSerializer.SerializeToElement(valeur);

    private static Anthropic.Models.Messages.Message Reponse(string stopReason, string contenu) =>
        Anthropic.Models.Messages.Message.FromRawUnchecked(Json($$"""
            {
              "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-haiku-5-5",
              "content": {{contenu}},
              "stop_reason": "{{stopReason}}", "stop_sequence": null,
              "usage": { "input_tokens": 10, "output_tokens": 5 }
            }
            """).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));

    [Fact]
    public void ResultatEnErreur_EstEnvoyeAvecIsError()
    {
        IReadOnlyList<MessageXd> historique =
        [
            new MessageUtilisateur("lance le mode jeu"),
            new MessageAssistant([new BlocAppelOutil("a1", "lancer_mode", Json("""{"mode":"jeu"}"""))]),
            new MessageResultatsOutils([new ResultatOutil("a1", "L'utilisateur a refusé l'action.", true)]),
        ];

        List<MessageParam> parametres = ConversionMessages.VersParametres(historique);

        JsonElement resultat = Serialiser(parametres[2]).GetProperty("content")[0];
        Assert.Equal("user", Serialiser(parametres[2]).GetProperty("role").GetString());
        Assert.Equal("tool_result", resultat.GetProperty("type").GetString());
        Assert.Equal("a1", resultat.GetProperty("tool_use_id").GetString());
        Assert.True(resultat.GetProperty("is_error").GetBoolean());
    }

    [Fact]
    public void AppelOutil_EstRenvoyeAvecSesParametres()
    {
        IReadOnlyList<MessageXd> historique =
        [
            new MessageUtilisateur("lance le mode jeu"),
            new MessageAssistant([new BlocTexte("Je lance le mode jeu."), new BlocAppelOutil("a1", "lancer_mode", Json("""{"mode":"jeu"}"""))]),
        ];

        JsonElement assistant = Serialiser(ConversionMessages.VersParametres(historique)[1]);

        Assert.Equal("assistant", assistant.GetProperty("role").GetString());
        JsonElement appel = assistant.GetProperty("content")[1];
        Assert.Equal("tool_use", appel.GetProperty("type").GetString());
        Assert.Equal("lancer_mode", appel.GetProperty("name").GetString());
        Assert.Equal("jeu", appel.GetProperty("input").GetProperty("mode").GetString());
    }

    [Fact]
    public void SchemaDOutil_EstTransmisTelQuel()
    {
        var definition = new DefinitionOutil("etat_machine", "Métriques de la machine",
            Json("""{"type":"object","properties":{"aspect":{"type":"string","enum":["cpu","ram"]}},"required":["aspect"]}"""));

        JsonElement outil = Serialiser(ConversionMessages.VersOutils([definition])[0]);

        Assert.Equal("etat_machine", outil.GetProperty("name").GetString());
        JsonElement enumeration = outil.GetProperty("input_schema").GetProperty("properties").GetProperty("aspect").GetProperty("enum");
        Assert.Equal(["cpu", "ram"], enumeration.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void ReponseAvecAppelDOutil_EstConvertie()
    {
        ReponseModele reponse = ConversionMessages.VersReponse(Reponse("tool_use", """
            [
              { "type": "text", "text": "Je regarde." },
              { "type": "tool_use", "id": "toolu_1", "name": "etat_machine", "input": { "aspect": "ram" } }
            ]
            """));

        Assert.Equal(RaisonArret.AppelOutils, reponse.Arret);
        Assert.Equal(new BlocTexte("Je regarde."), reponse.Blocs[0]);
        BlocAppelOutil appel = Assert.IsType<BlocAppelOutil>(reponse.Blocs[1]);
        Assert.Equal(("toolu_1", "etat_machine"), (appel.IdAppel, appel.NomOutil));
        Assert.Equal("ram", appel.Parametres.GetProperty("aspect").GetString());
    }

    [Theory]
    [InlineData("end_turn", RaisonArret.Fin)]
    [InlineData("max_tokens", RaisonArret.LimiteJetons)]
    [InlineData("refusal", RaisonArret.Refus)]
    [InlineData("pause_turn", RaisonArret.Autre)]
    [InlineData("raison_future_inconnue", RaisonArret.Autre)]
    public void RaisonDArret_EstTraduite(string stopReason, RaisonArret attendue)
    {
        ReponseModele reponse = ConversionMessages.VersReponse(Reponse(stopReason, """[{ "type": "text", "text": "ok" }]"""));

        Assert.Equal(attendue, reponse.Arret);
    }
}
