using Rotomeca.Core.Optionals;
using Rotomeca.Rop.Interfaces;
using Xd.Local.Installation;

namespace Xd.Local.Tests.Installation;

public sealed class ChoixPalierTests
{
    private const long Go = 1024L * 1024 * 1024;

    private static ChoixInstallation Choisir(MayBe<long> memoire, MayBe<PalierLlm> impose = default) =>
        ChoixPalier.Choisir(memoire, impose, CatalogueLlm.Modeles)
            .Match(Ok: c => c, Err: e => throw new Xunit.Sdk.XunitException(e.Message));

    [Theory]
    [InlineData(16, PalierLlm.Confort)]
    [InlineData(13, PalierLlm.Confort)]
    [InlineData(11, PalierLlm.Standard)]
    [InlineData(9, PalierLlm.Standard)]
    [InlineData(4, PalierLlm.Leger)]
    public void PlusGrosPalierQuiTient_EstChoisi(long goLibres, PalierLlm attendu)
    {
        ChoixInstallation choix = Choisir(goLibres * Go);

        Assert.Equal(attendu, choix.Modele.Palier);
        Assert.Equal(MoteurLlm.Vulkan, choix.Moteur);
    }

    [Fact]
    public void SansCarteGraphique_PalierLegerSurProcesseur()
    {
        ChoixInstallation choix = Choisir(MayBe<long>.Null);

        Assert.Equal(PalierLlm.Leger, choix.Modele.Palier);
        Assert.Equal(MoteurLlm.Cpu, choix.Moteur);
        Assert.Equal("0", choix.CouchesGpu);
    }

    [Fact]
    public void PalierImpose_EstRespecteMemeSiLaMemoireManque()
    {
        ChoixInstallation choix = Choisir(4 * Go, PalierLlm.Confort);

        Assert.Equal(PalierLlm.Confort, choix.Modele.Palier);
        Assert.Equal("all", choix.CouchesGpu);
    }

    [Fact]
    public void PalierGpuImposeSansCarteGraphique_EstRefuse()
    {
        IResult<ChoixInstallation> resultat = ChoixPalier.Choisir(MayBe<long>.Null, PalierLlm.Standard, CatalogueLlm.Modeles);

        Assert.False(resultat.IsSuccess);
    }
}
