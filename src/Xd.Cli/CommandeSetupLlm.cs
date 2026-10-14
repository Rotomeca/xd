using Rotomeca.Core.Optionals;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Boucle;
using Xd.Local;
using Xd.Local.Installation;
using Xd.Local.Serveur;

namespace Xd.Cli;

/// <summary>
/// <c>xd setup llm [--palier leger|standard|confort]</c> : installe le LLM local puis vérifie qu'il répond.
/// </summary>
public sealed class CommandeSetupLlm
{
    private static readonly TimeSpan DelaiChargement = TimeSpan.FromMinutes(3);

    private readonly InstallateurLlm _installateur;
    private readonly HttpClient _http;

    public CommandeSetupLlm(InstallateurLlm installateur, HttpClient http)
    {
        _installateur = installateur;
        _http = http;
    }

    public async Task<int> ExecuterAsync(IReadOnlyList<string> options, AnnulationConsole annulation)
    {
        MayBe<PalierLlm> palier;
        switch (options)
        {
            case []:
                palier = MayBe<PalierLlm>.Null;
                break;
            case ["--palier", var valeur] when LirePalier(valeur) is { HasValue: true } lu:
                palier = lu;
                break;
            default:
                Console.Error.WriteLine("Usage : xd setup llm [--palier leger|standard|confort]");
                return 2;
        }

        IResult<ConfigurationLlm> installation = await _installateur.InstallerAsync(palier, annulation.Jeton);
        return await installation.Match(
            Ok: configuration => VerifierAsync(configuration, annulation),
            Err: erreur =>
            {
                Console.Error.WriteLine(erreur.Message);
                return Task.FromResult(1);
            });
    }

    /// <summary>Lance le serveur une fois et lui fait dire bonjour.</summary>
    private async Task<int> VerifierAsync(ConfigurationLlm configuration, AnnulationConsole annulation)
    {
        Console.WriteLine("Configuration écrite. Test du LLM local…");
        IResult<ServeurLlm> demarrage = await ServeurLlm.DemarrerAsync(configuration, _http, DelaiChargement, annulation.Jeton);

        return await demarrage.Match(
            Ok: async serveur =>
            {
                await using (serveur)
                {
                    var modele = new ModeleLocal(_http, new OptionsModeleLocal { Adresse = serveur.Adresse });
                    IResult<ReponseModele> reponse = await modele.EnvoyerAsync([new MessageUtilisateur("Dis bonjour en une phrase.")], [], annulation.Jeton);
                    return reponse.Match(
                        Ok: r =>
                        {
                            Console.WriteLine($"Réponse du modèle : {string.Join(" ", r.Blocs.OfType<BlocTexte>().Select(b => b.Texte))}");
                            Console.WriteLine("LLM local prêt. Lance : xd");
                            return 0;
                        },
                        Err: erreur =>
                        {
                            Console.Error.WriteLine(erreur.Message);
                            return 1;
                        });
                }
            },
            Err: erreur =>
            {
                Console.Error.WriteLine(erreur.Message);
                return Task.FromResult(1);
            });
    }

    private static MayBe<PalierLlm> LirePalier(string valeur) => valeur.ToLowerInvariant() switch
    {
        "leger" or "léger" => PalierLlm.Leger,
        "standard" => PalierLlm.Standard,
        "confort" => PalierLlm.Confort,
        _ => MayBe<PalierLlm>.Null,
    };
}
