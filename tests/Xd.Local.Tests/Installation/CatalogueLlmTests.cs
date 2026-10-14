using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Rotomeca.Core.Optionals;
using Xd.Local.Installation;

namespace Xd.Local.Tests.Installation;

public sealed class CatalogueLlmTests
{
    public static TheoryData<FichierDistant> TousLesFichiers()
    {
        var donnees = new TheoryData<FichierDistant>();
        foreach (ModeleCatalogue modele in CatalogueLlm.Modeles)
            donnees.Add(modele.Fichier);
        foreach (OSPlatform systeme in new[] { OSPlatform.Windows, OSPlatform.Linux })
        {
            foreach (MoteurLlm moteur in Enum.GetValues<MoteurLlm>())
                donnees.Add(CatalogueLlm.ArchiveServeur(systeme, Architecture.X64, moteur).Value!);
        }

        return donnees;
    }

    [Theory]
    [MemberData(nameof(TousLesFichiers))]
    public void ChaqueFichier_EstEpingleEnHttpsAvecUneEmpreinte(FichierDistant fichier)
    {
        Assert.Equal(Uri.UriSchemeHttps, fichier.Source.Scheme);
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), fichier.Sha256);
        Assert.True(fichier.Taille > 0);
        Assert.DoesNotContain("/latest/", fichier.Source.AbsoluteUri);
        Assert.DoesNotContain("/resolve/main/", fichier.Source.AbsoluteUri);
        Assert.EndsWith(fichier.Nom, fichier.Source.AbsoluteUri);
    }

    [Fact]
    public void Modeles_SontDuPlusGrosAuPlusLeger_EtLeDernierTourneSurProcesseur()
    {
        Assert.Equal([PalierLlm.Confort, PalierLlm.Standard, PalierLlm.Leger], CatalogueLlm.Modeles.Select(m => m.Palier));
        Assert.False(CatalogueLlm.Modeles[^1].SurGpu);
    }

    [Theory]
    [InlineData("WINDOWS", MoteurLlm.Vulkan, "llama-b11429-bin-win-vulkan-x64.zip")]
    [InlineData("WINDOWS", MoteurLlm.Cpu, "llama-b11429-bin-win-cpu-x64.zip")]
    [InlineData("LINUX", MoteurLlm.Vulkan, "llama-b11429-bin-ubuntu-vulkan-x64.tar.gz")]
    [InlineData("LINUX", MoteurLlm.Cpu, "llama-b11429-bin-ubuntu-x64.tar.gz")]
    public void ArchiveServeur_CorrespondALaPlateforme(string systeme, MoteurLlm moteur, string nomAttendu)
    {
        MayBe<FichierDistant> archive = CatalogueLlm.ArchiveServeur(OSPlatform.Create(systeme), Architecture.X64, moteur);

        Assert.True(archive.HasValue);
        Assert.Equal(nomAttendu, archive.Value?.Nom);
    }

    [Fact]
    public void PlateformeNonPriseEnCharge_NeRenvoieRien()
    {
        Assert.True(CatalogueLlm.ArchiveServeur(OSPlatform.Windows, Architecture.Arm64, MoteurLlm.Cpu).IsEmpty);
        Assert.True(CatalogueLlm.ArchiveServeur(OSPlatform.OSX, Architecture.X64, MoteurLlm.Cpu).IsEmpty);
    }
}
