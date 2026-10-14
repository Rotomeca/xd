using Rotomeca.Rop.Interfaces;
using Xd.Core.Audit;
using Xd.Core.Boucle;
using Xd.Core.Execution;
using Xd.Core.Outils;
using Xd.Core.Permissions;
using Xd.Local;
using Xd.Local.Serveur;

namespace Xd.Cli;

/// <summary>
/// <c>xd</c> : lance le LLM local et discute avec la boucle agentique en console.
/// </summary>
public sealed class CommandeDiscussion
{
    private static readonly TimeSpan DelaiChargement = TimeSpan.FromMinutes(3);
    private static readonly string[] MotsDeSortie = ["quitter", "au revoir", "exit"];

    private readonly EmplacementsXd _emplacements;
    private readonly HttpClient _http;
    private readonly IConfirmateur _confirmateur;
    private readonly IEnumerable<IOutil> _outils;

    public CommandeDiscussion(EmplacementsXd emplacements, HttpClient http, IConfirmateur confirmateur, IEnumerable<IOutil> outils)
    {
        _emplacements = emplacements;
        _http = http;
        _confirmateur = confirmateur;
        _outils = outils;
    }

    public async Task<int> ExecuterAsync(AnnulationConsole annulation)
    {
        IResult<ConfigurationLlm> configuration = FichierConfigurationLlm.Lire(_emplacements.FichierConfigurationLlm);
        return await configuration.Match(
            Ok: c => DemarrerPuisDiscuterAsync(c, annulation),
            Err: erreur => Task.FromResult(Echouer(erreur.Message)));
    }

    private async Task<int> DemarrerPuisDiscuterAsync(ConfigurationLlm configuration, AnnulationConsole annulation)
    {
        Console.WriteLine($"Démarrage du LLM local (palier {configuration.Palier}, {Path.GetFileName(configuration.CheminModele)})…");
        IResult<ServeurLlm> demarrage = await ServeurLlm.DemarrerAsync(configuration, _http, DelaiChargement, annulation.Jeton);

        return await demarrage.Match(
            Ok: serveur => DiscuterPuisArreterAsync(serveur, annulation),
            Err: erreur => Task.FromResult(Echouer(erreur.Message)));
    }

    private async Task<int> DiscuterPuisArreterAsync(ServeurLlm serveur, AnnulationConsole annulation)
    {
        await using (serveur)
            await DiscuterAsync(serveur, annulation);

        Console.WriteLine("LLM local arrêté.");
        return 0;
    }

    private async Task DiscuterAsync(ServeurLlm serveur, AnnulationConsole annulation)
    {
        var modele = new ModeleLocal(_http, new OptionsModeleLocal { Adresse = serveur.Adresse });
        using var journal = new JournalAuditJsonl(_emplacements.JournalAudit);
        var executeur = new ExecuteurOutils(_outils, _confirmateur, journal, TimeProvider.System);
        var boucle = new BoucleAgentique(modele, executeur, new HistoriqueConversation(toursMax: 10));

        Console.WriteLine("Bonjour ! Je t'écoute. (« quitter » pour sortir, Ctrl+C pour interrompre une réponse)");
        while (true)
        {
            annulation.Reinitialiser();
            Console.Write("> ");
            string? phrase = Console.ReadLine();

            // null : fin de l'entrée standard, ou Ctrl+C pendant la saisie.
            if (phrase is null || MotsDeSortie.Contains(phrase.Trim().ToLowerInvariant()))
                break;
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            Console.WriteLine(await boucle.TraiterAsync(phrase.Trim(), annulation.Jeton));
        }

        Console.WriteLine("Au revoir !");
    }

    private static int Echouer(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
