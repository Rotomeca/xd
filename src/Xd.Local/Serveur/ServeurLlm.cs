using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;

namespace Xd.Local.Serveur;

/// <summary>
/// <c>llama-server</c> lancé comme processus enfant. L'arrêt (<see cref="DisposeAsync"/>) tue le processus :
/// le modèle est déchargé et la mémoire vidéo libérée (invariant 11).
/// </summary>
public sealed class ServeurLlm : IAsyncDisposable
{
    private const int LignesJournalConservees = 20;

    private readonly Process _processus;
    private readonly Queue<string> _dernieresLignes = new();

    private ServeurLlm(Process processus, Uri adresse)
    {
        _processus = processus;
        Adresse = adresse;
    }

    /// <summary>Adresse de base du serveur.</summary>
    public Uri Adresse { get; }

    /// <summary>Lance le serveur et attend qu'il ait chargé le modèle.</summary>
    /// <param name="configuration">Exécutable, modèle et réglages.</param>
    /// <param name="http">Client utilisé pour interroger <c>/health</c>.</param>
    /// <param name="delaiMax">Durée maximale du chargement.</param>
    /// <param name="annulation">Annule le démarrage ; le processus est alors arrêté.</param>
    /// <returns>Le serveur prêt, ou une erreur (fichier absent, port occupé, échec du chargement…).</returns>
    public static async Task<IResult<ServeurLlm>> DemarrerAsync(ConfigurationLlm configuration, HttpClient http, TimeSpan delaiMax, CancellationToken annulation)
    {
        if (!File.Exists(configuration.CheminServeur))
            return Echec($"llama-server introuvable : {configuration.CheminServeur}. Relance : xd setup llm");
        if (!File.Exists(configuration.CheminModele))
            return Echec($"Modèle introuvable : {configuration.CheminModele}. Relance : xd setup llm");

        // Un autre programme sur ce port recevrait nos conversations : on refuse de démarrer.
        if (!PortLibre(configuration.Port))
            return Echec($"Le port {configuration.Port} est déjà utilisé par un autre programme.");

        Process processus;
        try
        {
            processus = Process.Start(CreerDemarrage(configuration))
                ?? throw new InvalidOperationException("Process.Start n'a renvoyé aucun processus.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return Echec($"Lancement de llama-server impossible : {ex.Message}", ex);
        }

        var serveur = new ServeurLlm(processus, configuration.Adresse);
        processus.OutputDataReceived += (_, e) => serveur.Conserver(e.Data);
        processus.ErrorDataReceived += (_, e) => serveur.Conserver(e.Data);
        processus.BeginOutputReadLine();
        processus.BeginErrorReadLine();

        IResult<bool> pret = await serveur.AttendreQuIlSoitPretAsync(http, delaiMax, annulation);
        if (pret.IsSuccess)
            return Result.Ok(serveur);

        await serveur.DisposeAsync();
        return pret.Match(Ok: _ => Echec("Démarrage interrompu."), Err: erreur => Result.Fail<ServeurLlm>(erreur));
    }

    /// <summary>Arrête le serveur et attend la fin du processus.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_processus.HasExited)
            {
                _processus.Kill(entireProcessTree: true);
                await _processus.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // Processus déjà terminé.
        }
        finally
        {
            _processus.Dispose();
        }
    }

    private static ProcessStartInfo CreerDemarrage(ConfigurationLlm configuration)
    {
        // Aucun shell (invariant 3) : exécutable et arguments séparés.
        var demarrage = new ProcessStartInfo(configuration.CheminServeur)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(configuration.CheminServeur) ?? string.Empty,
        };

        string[] arguments =
        [
            "--model", configuration.CheminModele,
            "--host", "127.0.0.1",
            "--port", configuration.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", configuration.TailleContexte.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-gpu-layers", configuration.CouchesGpu,
            // Pas de raisonnement visible : latence vocale.
            "--reasoning-budget", "0",
            // Ni accès réseau ni interface web : le serveur ne sert qu'à Xd.
            "--offline",
            "--no-webui",
        ];
        foreach (string argument in arguments)
            demarrage.ArgumentList.Add(argument);

        return demarrage;
    }

    private async Task<IResult<bool>> AttendreQuIlSoitPretAsync(HttpClient http, TimeSpan delaiMax, CancellationToken annulation)
    {
        var sante = new Uri(Adresse, "health");
        var chrono = Stopwatch.StartNew();

        while (chrono.Elapsed < delaiMax)
        {
            if (annulation.IsCancellationRequested)
                return Result.Fail<bool>(new Error("Démarrage de llama-server annulé.", ErrorType.Unexpected));

            if (_processus.HasExited)
                return Result.Fail<bool>(new Error($"llama-server s'est arrêté pendant le chargement (code {_processus.ExitCode}).{Journal()}", ErrorType.Unexpected));

            try
            {
                using HttpResponseMessage reponse = await http.GetAsync(sante, annulation);
                if (reponse.StatusCode == HttpStatusCode.OK)
                    return Result.Ok(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                // Pas encore à l'écoute, ou annulation : vérifiée au tour suivant.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), annulation);
            }
            catch (OperationCanceledException)
            {
                // Vérifiée au tour suivant.
            }
        }

        return Result.Fail<bool>(new Error($"llama-server n'a pas fini de charger le modèle en {delaiMax.TotalSeconds:0} s.{Journal()}", ErrorType.Unexpected));
    }

    private void Conserver(string? ligne)
    {
        if (string.IsNullOrWhiteSpace(ligne))
            return;

        lock (_dernieresLignes)
        {
            _dernieresLignes.Enqueue(ligne);
            while (_dernieresLignes.Count > LignesJournalConservees)
                _dernieresLignes.Dequeue();
        }
    }

    private string Journal()
    {
        lock (_dernieresLignes)
            return _dernieresLignes.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, _dernieresLignes);
    }

    private static bool PortLibre(int port)
    {
        try
        {
            var ecoute = new TcpListener(IPAddress.Loopback, port);
            ecoute.Start();
            ecoute.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static IResult<ServeurLlm> Echec(string message, Exception? exception = null) =>
        Result.Fail<ServeurLlm>(new Error(message, ErrorType.Unexpected, exception: exception));
}
