using System.Net.Http.Json;
using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Boucle;
using Xd.Core.Outils;

namespace Xd.Local;

/// <summary>
/// Accès au LLM local servi par <c>llama-server</c>, via son API compatible OpenAI.
/// Un seul appel HTTP par envoi : aucun outil n'est exécuté ici, la boucle et la couche de permission restent dans <c>Xd.Core</c>.
/// </summary>
public sealed class ModeleLocal : IModele
{
    private readonly HttpClient _http;
    private readonly OptionsModeleLocal _options;
    private readonly Uri _point;

    public ModeleLocal(HttpClient http, OptionsModeleLocal options)
    {
        _http = http;
        _options = options;
        _point = new Uri(options.Adresse, "v1/chat/completions");
    }

    /// <inheritdoc/>
    public async Task<IResult<ReponseModele>> EnvoyerAsync(IReadOnlyList<Message> historique, IReadOnlyList<DefinitionOutil> outils, CancellationToken annulation)
    {
        var requete = ConversionMessages.VersRequete(_options.PromptSysteme, _options.MaxJetons, historique, outils);

        try
        {
            using HttpResponseMessage reponse = await _http.PostAsJsonAsync(_point, requete, annulation);
            if (!reponse.IsSuccessStatusCode)
                return Echec($"le serveur a répondu {(int)reponse.StatusCode} {reponse.ReasonPhrase}.");

            JsonElement corps = await reponse.Content.ReadFromJsonAsync<JsonElement>(annulation);
            return Result.Ok(ConversionMessages.VersReponse(corps));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                                       or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            // Frontière avec le réseau : serveur absent, délai dépassé, annulation, réponse illisible… tout devient une erreur Rop.
            return Echec(ex.Message, ex);
        }
    }

    private static IResult<ReponseModele> Echec(string detail, Exception? exception = null) =>
        Result.Fail<ReponseModele>(new Error($"Appel au LLM local impossible : {detail}", ErrorType.Unexpected, exception: exception));
}
