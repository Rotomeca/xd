using System.Net;
using System.Text;
using System.Text.Json;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Boucle;
using Xd.Core.Execution;
using Xd.Core.Outils;

namespace Xd.Local.Tests;

public sealed class ModeleLocalTests
{
    private static readonly DefinitionOutil EtatMachine = new("etat_machine", "Métriques de la machine",
        Json("""{"type":"object","properties":{"aspect":{"type":"string","enum":["cpu","ram"]}},"required":["aspect"]}"""));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Reponse(string message, string finishReason) => $$"""
        { "choices": [ { "index": 0, "message": {{message}}, "finish_reason": "{{finishReason}}" } ] }
        """;

    private static (ModeleLocal Modele, ServeurFactice Serveur) Creer(HttpStatusCode statut, string corps)
    {
        var serveur = new ServeurFactice(statut, corps);
        return (new ModeleLocal(new HttpClient(serveur), new OptionsModeleLocal { PromptSysteme = "Sois bref." }), serveur);
    }

    private static Task<IResult<ReponseModele>> Envoyer(ModeleLocal modele, params Message[] historique) =>
        modele.EnvoyerAsync(historique, [EtatMachine], CancellationToken.None);

    private static ReponseModele Valeur(IResult<ReponseModele> resultat) =>
        resultat.Match(Ok: r => r, Err: e => throw new Xunit.Sdk.XunitException($"Échec inattendu : {e.Message}"));

    [Fact]
    public async Task Requete_ContientPromptSystemeHistoriqueEtOutils()
    {
        var (modele, serveur) = Creer(HttpStatusCode.OK, Reponse("""{ "role": "assistant", "content": "ok" }""", "stop"));

        await Envoyer(modele,
            new MessageUtilisateur("et la RAM ?"),
            new MessageAssistant([new BlocAppelOutil("a1", "etat_machine", Json("""{"aspect":"ram"}"""))]),
            new MessageResultatsOutils([new ResultatOutil("a1", "RAM : 40 %", false)]));

        Assert.Equal("http://127.0.0.1:8080/v1/chat/completions", serveur.Adresse?.ToString());
        JsonElement messages = serveur.Corps.GetProperty("messages");
        Assert.Equal(["system", "user", "assistant", "tool"], messages.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("Sois bref.", messages[0].GetProperty("content").GetString());
        Assert.False(serveur.Corps.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());

        JsonElement appel = messages[2].GetProperty("tool_calls")[0];
        Assert.Equal("a1", appel.GetProperty("id").GetString());
        Assert.Equal("etat_machine", appel.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("ram", Json(appel.GetProperty("function").GetProperty("arguments").GetString() ?? "").GetProperty("aspect").GetString());

        Assert.Equal("a1", messages[3].GetProperty("tool_call_id").GetString());
        Assert.Equal("RAM : 40 %", messages[3].GetProperty("content").GetString());

        JsonElement outil = serveur.Corps.GetProperty("tools")[0].GetProperty("function");
        Assert.Equal("etat_machine", outil.GetProperty("name").GetString());
        Assert.Equal(["cpu", "ram"], outil.GetProperty("parameters").GetProperty("properties").GetProperty("aspect").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ResultatEnErreur_EstSignaleDansLeTexte()
    {
        var (modele, serveur) = Creer(HttpStatusCode.OK, Reponse("""{ "role": "assistant", "content": "ok" }""", "stop"));

        await Envoyer(modele,
            new MessageUtilisateur("lance le mode jeu"),
            new MessageAssistant([new BlocAppelOutil("a1", "lancer_mode", Json("""{"mode":"jeu"}"""))]),
            new MessageResultatsOutils([new ResultatOutil("a1", "L'utilisateur a refusé l'action.", true)]));

        Assert.Equal("ERREUR : L'utilisateur a refusé l'action.", serveur.Corps.GetProperty("messages")[3].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("tool_calls")]
    [InlineData("stop")]
    public async Task AppelsDOutils_SontLusQuelleQueSoitLaRaisonDArret(string finishReason)
    {
        var (modele, _) = Creer(HttpStatusCode.OK, Reponse("""
            { "role": "assistant", "content": null, "tool_calls": [
              { "id": "c1", "type": "function", "function": { "name": "etat_machine", "arguments": "{\"aspect\":\"ram\"}" } } ] }
            """, finishReason));

        ReponseModele reponse = Valeur(await Envoyer(modele, new MessageUtilisateur("et la RAM ?")));

        Assert.Equal(RaisonArret.AppelOutils, reponse.Arret);
        BlocAppelOutil appel = Assert.IsType<BlocAppelOutil>(Assert.Single(reponse.Blocs));
        Assert.Equal(("c1", "etat_machine"), (appel.IdAppel, appel.NomOutil));
        Assert.Equal("ram", appel.Parametres.GetProperty("aspect").GetString());
    }

    [Fact]
    public async Task ArgumentsIllisibles_SontTransmisTelsQuelsPourEtreRefusesParLOutil()
    {
        var (modele, _) = Creer(HttpStatusCode.OK, Reponse("""
            { "role": "assistant", "tool_calls": [
              { "type": "function", "function": { "name": "etat_machine", "arguments": "{aspect: ram" } } ] }
            """, "tool_calls"));

        ReponseModele reponse = Valeur(await Envoyer(modele, new MessageUtilisateur("et la RAM ?")));

        BlocAppelOutil appel = Assert.IsType<BlocAppelOutil>(Assert.Single(reponse.Blocs));
        Assert.Equal("appel-0", appel.IdAppel);
        Assert.Equal(JsonValueKind.String, appel.Parametres.ValueKind);
    }

    [Theory]
    [InlineData("stop", RaisonArret.Fin)]
    [InlineData("length", RaisonArret.LimiteJetons)]
    [InlineData("autre_chose", RaisonArret.Autre)]
    public async Task RaisonDArret_EstTraduite(string finishReason, RaisonArret attendue)
    {
        var (modele, _) = Creer(HttpStatusCode.OK, Reponse("""{ "role": "assistant", "content": "ok" }""", finishReason));

        ReponseModele reponse = Valeur(await Envoyer(modele, new MessageUtilisateur("salut")));

        Assert.Equal(attendue, reponse.Arret);
    }

    [Theory]
    [InlineData(@"<think>l'utilisateur salue</think>\nBonjour !")]
    [InlineData(@"Okay, the user greets me. Keep it short.\n</think>\n\nBonjour !")]
    [InlineData("Bonjour !")]
    public async Task ReflexionDansLeTexte_EstRetiree(string contenu)
    {
        var (modele, _) = Creer(HttpStatusCode.OK, Reponse($$"""{ "role": "assistant", "content": "{{contenu}}" }""", "stop"));

        ReponseModele reponse = Valeur(await Envoyer(modele, new MessageUtilisateur("salut")));

        Assert.Equal(new BlocTexte("Bonjour !"), Assert.Single(reponse.Blocs));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{ "error": "boom" }""")]
    [InlineData(HttpStatusCode.OK, "pas du json")]
    [InlineData(HttpStatusCode.OK, """{ "choices": [] }""")]
    public async Task ReponseInvalide_DevientUneErreurSansException(HttpStatusCode statut, string corps)
    {
        var (modele, _) = Creer(statut, corps);

        IResult<ReponseModele> resultat = await Envoyer(modele, new MessageUtilisateur("salut"));

        Assert.False(resultat.IsSuccess);
    }

    [Fact]
    public async Task ServeurAbsent_DevientUneErreurSansException()
    {
        var modele = new ModeleLocal(new HttpClient(new ServeurFactice(new HttpRequestException("connexion refusée"))), new OptionsModeleLocal());

        IResult<ReponseModele> resultat = await Envoyer(modele, new MessageUtilisateur("salut"));

        Assert.False(resultat.IsSuccess);
    }

    [Fact]
    public async Task Annulation_DevientUneErreurSansException()
    {
        var (modele, _) = Creer(HttpStatusCode.OK, Reponse("""{ "role": "assistant", "content": "ok" }""", "stop"));
        using var veille = new CancellationTokenSource();
        await veille.CancelAsync();

        IResult<ReponseModele> resultat = await modele.EnvoyerAsync([new MessageUtilisateur("salut")], [EtatMachine], veille.Token);

        Assert.False(resultat.IsSuccess);
    }

    /// <summary>Serveur HTTP en mémoire : mémorise la requête et renvoie une réponse fixe.</summary>
    private sealed class ServeurFactice : HttpMessageHandler
    {
        private readonly HttpStatusCode _statut;
        private readonly string _corps;
        private readonly Exception? _exception;

        public ServeurFactice(HttpStatusCode statut, string corps)
        {
            _statut = statut;
            _corps = corps;
        }

        public ServeurFactice(Exception exception) : this(HttpStatusCode.OK, string.Empty)
        {
            _exception = exception;
        }

        public Uri? Adresse { get; private set; }
        public JsonElement Corps { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            annulation.ThrowIfCancellationRequested();
            if (_exception is not null)
                throw _exception;

            Adresse = requete.RequestUri;
            if (requete.Content is not null)
                Corps = Json(await requete.Content.ReadAsStringAsync(annulation));

            return new HttpResponseMessage(_statut) { Content = new StringContent(_corps, Encoding.UTF8, "application/json") };
        }
    }
}
