using Xd.Core.Permissions;

namespace Xd.Core.Tests.Permissions;

public sealed class InterpretationConfirmationTests
{
    [Theory]
    [InlineData("o")]
    [InlineData("oui")]
    [InlineData("Oui !")]
    [InlineData("go !")]
    [InlineData("vas-y")]
    [InlineData("ok merci")]
    [InlineData("d'accord")]
    [InlineData("Carrément.")]
    [InlineData("oui, vas-y stp")]
    [InlineData("c'est ça")]
    [InlineData("je confirme")]
    public void OuiFranc_Confirme(string reponse)
    {
        Assert.Equal(ReponseConfirmation.Oui, InterpretationConfirmation.Interpreter(reponse));
    }

    [Theory]
    [InlineData("n")]
    [InlineData("non")]
    [InlineData("Non merci")]
    [InlineData("nan")]
    [InlineData("annule")]
    [InlineData("laisse tomber")]
    [InlineData("surtout pas !")]
    public void NonFranc_Refuse(string reponse)
    {
        Assert.Equal(ReponseConfirmation.Non, InterpretationConfirmation.Interpreter(reponse));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("euh")]
    [InlineData("je sais pas")]
    [InlineData("oui mais dans Xd")]
    [InlineData("oui non")]
    [InlineData("pourquoi ?")]
    [InlineData("ouioui")]
    [InlineData("nonobstant")]
    [InlineData("go to hell")]
    [InlineData("non, mets-le dans Xd")]
    public void ReponseLibre_EstAutre(string? reponse)
    {
        Assert.Equal(ReponseConfirmation.Autre, InterpretationConfirmation.Interpreter(reponse));
    }
}
