using System.IO.Pipes;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Tools.EtatMachine;

/// <summary>
/// Conteneurs en marche via l'API du moteur Docker, sans shell ni client <c>docker</c> :
/// tube nommé <c>docker_engine</c> sous Windows, socket <c>/var/run/docker.sock</c> sous Linux.
/// Seule la lecture <c>GET /containers/json</c> est utilisée.
/// </summary>
public sealed class ConteneursDocker : ISourceConteneurs, IDisposable
{
    private static readonly TimeSpan DelaiMax = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;

    public ConteneursDocker()
    {
        var gestionnaire = new SocketsHttpHandler { ConnectCallback = ConnecterAsync };
        _http = new HttpClient(gestionnaire) { BaseAddress = new Uri("http://docker/"), Timeout = DelaiMax };
    }

    /// <inheritdoc/>
    public async Task<IResult<IReadOnlyList<Conteneur>>> ListerAsync(CancellationToken annulation)
    {
        try
        {
            JsonElement corps = await _http.GetFromJsonAsync<JsonElement>("containers/json", annulation);
            return Result.Ok(Lire(corps));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or SocketException or TimeoutException
                                       or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return Result.Fail<IReadOnlyList<Conteneur>>(new Error(
                "Docker n'est pas accessible (service arrêté ou non installé).", ErrorType.Unexpected, exception: ex));
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    /// <summary>Lit la réponse de <c>/containers/json</c> en ignorant les champs absents.</summary>
    internal static IReadOnlyList<Conteneur> Lire(JsonElement corps)
    {
        if (corps.ValueKind != JsonValueKind.Array)
            throw new JsonException("Réponse Docker inattendue : un tableau était attendu.");

        return [.. corps.EnumerateArray().Select(c => new Conteneur(
            Texte(c, "Names", premierDuTableau: true).TrimStart('/'),
            Texte(c, "Image"),
            Texte(c, "Status")))];
    }

    private static string Texte(JsonElement objet, string nom, bool premierDuTableau = false)
    {
        if (!objet.TryGetProperty(nom, out JsonElement valeur))
            return "?";

        if (premierDuTableau && valeur.ValueKind == JsonValueKind.Array)
            valeur = valeur.EnumerateArray().FirstOrDefault();

        return valeur.ValueKind == JsonValueKind.String ? valeur.GetString() ?? "?" : "?";
    }

    private static async ValueTask<Stream> ConnecterAsync(SocketsHttpConnectionContext contexte, CancellationToken annulation)
    {
        if (OperatingSystem.IsWindows())
        {
            var tube = new NamedPipeClientStream(".", "docker_engine", PipeDirection.InOut, PipeOptions.Asynchronous);
            await tube.ConnectAsync(annulation);
            return tube;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/run/docker.sock"), annulation);
        return new NetworkStream(socket, ownsSocket: true);
    }
}
