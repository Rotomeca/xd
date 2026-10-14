namespace Xd.Cli;

/// <summary>
/// Ctrl+C annule l'opération en cours (demande, démarrage, téléchargement) au lieu de tuer le processus,
/// pour que les processus enfants soient arrêtés proprement.
/// </summary>
public sealed class AnnulationConsole : IDisposable
{
    private readonly object _verrou = new();
    private CancellationTokenSource _courante = new();

    public AnnulationConsole()
    {
        Console.CancelKeyPress += Intercepter;
    }

    /// <summary>Jeton de l'opération en cours.</summary>
    public CancellationToken Jeton
    {
        get
        {
            lock (_verrou)
                return _courante.Token;
        }
    }

    /// <summary>Prépare un nouveau jeton après une annulation, pour l'opération suivante.</summary>
    public void Reinitialiser()
    {
        lock (_verrou)
        {
            if (!_courante.IsCancellationRequested)
                return;

            _courante.Dispose();
            _courante = new CancellationTokenSource();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Console.CancelKeyPress -= Intercepter;
        lock (_verrou)
            _courante.Dispose();
    }

    private void Intercepter(object? expediteur, ConsoleCancelEventArgs evenement)
    {
        evenement.Cancel = true;
        lock (_verrou)
            _courante.Cancel();
    }
}
