using Anthropic;
using Anthropic.Models.Messages;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Core.Boucle;
using Xd.Core.Outils;
using MessageXd = Xd.Core.Boucle.Message;

namespace Xd.Claude;

/// <summary>
/// Accès à Claude par le SDK officiel <c>Anthropic</c>. Un seul appel <c>messages.create</c> par envoi :
/// aucun outil n'est exécuté ici, la boucle et la couche de permission restent dans <c>Xd.Core</c>.
/// </summary>
public sealed class ModeleAnthropic : IModele
{
    private readonly IAnthropicClient _client;
    private readonly OptionsModeleAnthropic _options;

    public ModeleAnthropic(IAnthropicClient client, OptionsModeleAnthropic options)
    {
        _client = client;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<IResult<ReponseModele>> EnvoyerAsync(IReadOnlyList<MessageXd> historique, IReadOnlyList<DefinitionOutil> outils, CancellationToken annulation)
    {
        var parametres = new MessageCreateParams
        {
            Model = _options.Modele,
            MaxTokens = _options.MaxJetons,
            System = _options.PromptSysteme,
            // Réflexion désactivée : latence vocale, et aucun bloc de réflexion à rejouer dans un historique glissant.
            // L'API n'accepte la désactivation qu'avec un effort au plus « high ».
            Thinking = new ThinkingConfigDisabled(),
            OutputConfig = new OutputConfig { Effort = Effort.Low },
            Tools = ConversionMessages.VersOutils(outils),
            Messages = ConversionMessages.VersParametres(historique),
        };

        try
        {
            Anthropic.Models.Messages.Message reponse = await _client.Messages.Create(parametres, annulation);
            return Result.Ok(ConversionMessages.VersReponse(reponse));
        }
        catch (Exception ex)
        {
            // Frontière avec le SDK : réseau, API, annulation… tout devient une erreur Rop.
            return Result.Fail<ReponseModele>(new Error($"Appel à Claude impossible : {ex.Message}", ErrorType.Unexpected, exception: ex));
        }
    }
}
