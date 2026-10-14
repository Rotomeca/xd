using Microsoft.Extensions.DependencyInjection;
using Xd.Cli;
using Xd.Core.Permissions;
using Xd.Local.Serveur;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

await using ServiceProvider services = new ServiceCollection()
    .AddSingleton(EmplacementsXd.Defaut())
    // Le LLM local peut être lent sur processeur : délai large, Ctrl+C reste possible.
    .AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
    .AddSingleton<IConfirmateur, ConfirmateurConsole>()
    .AddSingleton<CommandeDiscussion>()
    .BuildServiceProvider();

using var annulation = new AnnulationConsole();

return args switch
{
    [] => await services.GetRequiredService<CommandeDiscussion>().ExecuterAsync(annulation),
    ["aide" or "--aide" or "-h" or "--help"] => Aide(0),
    _ => Aide(2),
};

static int Aide(int code)
{
    Console.WriteLine("""
        Usage :
          xd                 discuter avec l'assistant (LLM local)
          xd aide            afficher cette aide
        """);
    return code;
}
