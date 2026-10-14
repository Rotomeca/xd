using Xd.Local.Installation;

namespace Xd.Cli;

/// <summary>
/// Dialogue d'installation en console : messages, question oui/non, progression des téléchargements.
/// </summary>
public sealed class InteractionConsole : IInteractionInstallation
{
    /// <inheritdoc/>
    public void Informer(string message) => Console.WriteLine(message);

    /// <inheritdoc/>
    public async Task<bool> ConfirmerAsync(string question, CancellationToken annulation)
    {
        Console.Write($"{question} (o/N) ");
        string? reponse = await Console.In.ReadLineAsync(annulation);
        return reponse?.Trim().ToLowerInvariant() is "o" or "oui";
    }

    /// <inheritdoc/>
    public IProgress<long> SuivreTelechargement(FichierDistant fichier) => new ProgressionConsole(fichier);

    /// <summary>Affiche le pourcentage sur une seule ligne, à chaque point gagné.</summary>
    private sealed class ProgressionConsole(FichierDistant fichier) : IProgress<long>
    {
        private int _dernierPourcentage = -1;

        public void Report(long recus)
        {
            int pourcentage = (int)(recus * 100 / Math.Max(1, fichier.Taille));
            if (pourcentage == _dernierPourcentage)
                return;

            _dernierPourcentage = pourcentage;
            Console.Write($"\r  {fichier.Nom} : {pourcentage,3} %");
            if (pourcentage >= 100)
                Console.WriteLine();
        }
    }
}
