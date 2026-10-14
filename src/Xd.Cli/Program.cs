using Microsoft.Extensions.DependencyInjection;
using Xd.Cli;
using Xd.Core.Outils;
using Xd.Core.Permissions;
using Xd.Local.Installation;
using Xd.Local.Serveur;
using Xd.Tools.EtatMachine;
using Xd.Tools.Notes;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

await using ServiceProvider services = new ServiceCollection()
    .AddSingleton(EmplacementsXd.Defaut())
    // Le LLM local peut être lent sur processeur : délai large, Ctrl+C reste possible.
    .AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
    .AddSingleton<IConfirmateur, ConfirmateurConsole>()
    .AddSingleton<CommandeDiscussion>()
    .AddSingleton<IMesuresMachine>(_ => OperatingSystem.IsWindows() ? new MesuresWindows()
        : OperatingSystem.IsLinux() ? new MesuresLinux()
        : throw new PlatformNotSupportedException("Xd fonctionne sous Windows et Linux."))
    .AddSingleton<ISourceConteneurs, ConteneursDocker>()
    .AddSingleton<IOutil, OutilEtatMachine>()
    .AddSingleton(TimeProvider.System)
    .AddSingleton<IStockageConfigurationNotes>(fournisseur =>
        new FichierConfigurationNotes(Path.Combine(fournisseur.GetRequiredService<EmplacementsXd>().Configuration, "notes.json")))
    .AddSingleton(LecturePhraseChemin.Plateforme())
    .AddSingleton<IOutil, OutilNoterIdee>()
    .AddSingleton<IOutil, OutilConfigurerNotes>()
    .AddSingleton<IMesureMemoireVideo, MesureNvidiaSmi>()
    .AddSingleton<IInteractionInstallation, InteractionConsole>()
    // Téléchargements de plusieurs gigaoctets : pas de délai global, Ctrl+C pour interrompre.
    .AddSingleton(_ => new Telechargeur(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }))
    .AddSingleton<InstallateurLlm>()
    .AddSingleton<CommandeSetupLlm>()
    .BuildServiceProvider();

using var annulation = new AnnulationConsole();

return args switch
{
    [] => await services.GetRequiredService<CommandeDiscussion>().ExecuterAsync(annulation),
    ["setup", "llm", .. var options] => await services.GetRequiredService<CommandeSetupLlm>().ExecuterAsync(options, annulation),
    ["aide" or "--aide" or "-h" or "--help"] => Aide(0),
    _ => Aide(2),
};

static int Aide(int code)
{
    Console.WriteLine("""
        Usage :
          xd                 discuter avec l'assistant (LLM local)
          xd setup llm       installer le LLM local (palier choisi selon la carte graphique)
            [--palier leger|standard|confort]   imposer un palier
          xd aide            afficher cette aide
        """);
    return code;
}
