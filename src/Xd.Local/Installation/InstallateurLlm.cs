using System.Runtime.InteropServices;
using Rotomeca.Core.Optionals;
using Rotomeca.Rop;
using Rotomeca.Rop.Interfaces;
using Xd.Local.Serveur;

namespace Xd.Local.Installation;

/// <summary>
/// Dialogue avec l'utilisateur pendant l'installation, indépendant de la console.
/// </summary>
public interface IInteractionInstallation
{
    /// <summary>Affiche une information.</summary>
    void Informer(string message);

    /// <summary>Pose une question ; seul un oui explicite renvoie <see langword="true"/>.</summary>
    Task<bool> ConfirmerAsync(string question, CancellationToken annulation);

    /// <summary>Crée un suivi de progression pour un téléchargement.</summary>
    IProgress<long> SuivreTelechargement(FichierDistant fichier);
}

/// <summary>
/// <c>xd setup llm</c> : choisit le palier, télécharge llama.cpp et le modèle depuis le catalogue épinglé
/// (après confirmation), vérifie les empreintes, extrait et écrit la configuration.
/// </summary>
public sealed class InstallateurLlm
{
    private const long MargeDisque = 1024L * 1024 * 1024;

    private readonly IMesureMemoireVideo _mesure;
    private readonly Telechargeur _telechargeur;
    private readonly IInteractionInstallation _interaction;
    private readonly EmplacementsXd _emplacements;

    public InstallateurLlm(IMesureMemoireVideo mesure, Telechargeur telechargeur, IInteractionInstallation interaction, EmplacementsXd emplacements)
    {
        _mesure = mesure;
        _telechargeur = telechargeur;
        _interaction = interaction;
        _emplacements = emplacements;
    }

    /// <summary>Installe le LLM local.</summary>
    /// <param name="palierImpose">Palier demandé explicitement, sinon choix automatique.</param>
    /// <param name="annulation">Annule l'installation.</param>
    /// <returns>La configuration écrite, ou une erreur (refus, téléchargement, extraction…).</returns>
    public async Task<IResult<ConfigurationLlm>> InstallerAsync(MayBe<PalierLlm> palierImpose, CancellationToken annulation)
    {
        MayBe<long> memoire = await _mesure.MesurerLibreAsync(annulation);
        _interaction.Informer(memoire.HasValue
            ? $"Carte graphique détectée : {Go(memoire.Value)} de mémoire vidéo libre."
            : "Aucune carte graphique NVIDIA détectée : le modèle tournera sur le processeur.");

        return await ChoixPalier.Choisir(memoire, palierImpose, CatalogueLlm.Modeles).Match(
            Ok: choix => InstallerChoixAsync(choix, annulation),
            Err: erreur => Task.FromResult(Result.Fail<ConfigurationLlm>(erreur)));
    }

    private async Task<IResult<ConfigurationLlm>> InstallerChoixAsync(ChoixInstallation choix, CancellationToken annulation)
    {
        MayBe<FichierDistant> archive = CatalogueLlm.ArchiveServeur(SystemeCourant(), RuntimeInformation.OSArchitecture, choix.Moteur);
        if (archive.IsEmpty || archive.Value is not { } archiveServeur)
            return Echec($"Plateforme non prise en charge : {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}.");

        string dossierServeur = Path.Combine(_emplacements.DossierLlm, "llama.cpp", $"{CatalogueLlm.VersionLlamaCpp}-{choix.Moteur.ToString().ToLowerInvariant()}");
        string cheminArchive = Path.Combine(_emplacements.DossierLlm, "telechargements", archiveServeur.Nom);
        string cheminModele = Path.Combine(_emplacements.DossierLlm, "modeles", choix.Modele.Fichier.Nom);

        bool serveurPresent = Extracteur.TrouverExecutable(dossierServeur, "llama-server").IsSuccess;
        bool modelePresent = File.Exists(cheminModele) && new FileInfo(cheminModele).Length == choix.Modele.Fichier.Taille;

        var aTelecharger = new List<(FichierDistant Fichier, string Destination)>();
        if (!serveurPresent)
            aTelecharger.Add((archiveServeur, cheminArchive));
        if (!modelePresent)
            aTelecharger.Add((choix.Modele.Fichier, cheminModele));

        _interaction.Informer($"Palier retenu : {choix.Modele.Palier} ({choix.Modele.Fichier.Nom}, {(choix.Modele.SurGpu ? "carte graphique" : "processeur")}).");

        if (aTelecharger.Count > 0)
        {
            IResult<bool> telechargement = await TelechargerAsync(aTelecharger, annulation);
            if (!telechargement.IsSuccess)
                return telechargement.Match(Ok: _ => Echec("Téléchargement interrompu."), Err: erreur => Result.Fail<ConfigurationLlm>(erreur));
        }
        else
        {
            _interaction.Informer("llama.cpp et le modèle sont déjà installés.");
        }

        // Un modèle déjà présent est revérifié : il a pu être tronqué ou modifié depuis.
        _interaction.Informer("Vérification de l'empreinte du modèle…");
        if (!await Telechargeur.EstPresentAsync(choix.Modele.Fichier, cheminModele, annulation))
            return Echec($"L'empreinte de {cheminModele} ne correspond pas au catalogue. Supprime ce fichier et relance xd setup llm.");

        if (!serveurPresent)
        {
            _interaction.Informer("Extraction de llama.cpp…");
            IResult<bool> extraction = await Extracteur.ExtraireAsync(cheminArchive, dossierServeur, annulation);
            if (!extraction.IsSuccess)
                return extraction.Match(Ok: _ => Echec("Extraction interrompue."), Err: erreur => Result.Fail<ConfigurationLlm>(erreur));
        }

        return Extracteur.TrouverExecutable(dossierServeur, "llama-server").Match(
            Ok: cheminServeur => EcrireConfiguration(new ConfigurationLlm(cheminServeur, cheminModele, choix.Modele.Palier, choix.CouchesGpu)),
            Err: erreur => Result.Fail<ConfigurationLlm>(erreur));
    }

    private async Task<IResult<bool>> TelechargerAsync(List<(FichierDistant Fichier, string Destination)> fichiers, CancellationToken annulation)
    {
        long total = fichiers.Sum(f => f.Fichier.Taille);
        _interaction.Informer("À télécharger :");
        foreach ((FichierDistant fichier, _) in fichiers)
            _interaction.Informer($"  - {fichier.Nom} ({Go(fichier.Taille)}) depuis {fichier.Source.Host}");
        _interaction.Informer($"Destination : {_emplacements.DossierLlm}");

        long disponible = new DriveInfo(DossierExistant(_emplacements.DossierLlm)).AvailableFreeSpace;
        if (disponible < total + MargeDisque)
            return Result.Fail<bool>(new Error($"Espace disque insuffisant : {Go(total)} nécessaires, {Go(disponible)} disponibles.", ErrorType.Validation));

        if (!await _interaction.ConfirmerAsync($"Télécharger {Go(total)} ?", annulation))
            return Result.Fail<bool>(new Error("Installation annulée : téléchargement refusé.", ErrorType.Validation));

        foreach ((FichierDistant fichier, string destination) in fichiers)
        {
            IResult<bool> resultat = await _telechargeur.TelechargerAsync(fichier, destination, _interaction.SuivreTelechargement(fichier), annulation);
            if (!resultat.IsSuccess)
                return resultat;
        }

        return Result.Ok(true);
    }

    private IResult<ConfigurationLlm> EcrireConfiguration(ConfigurationLlm configuration) =>
        FichierConfigurationLlm.Ecrire(_emplacements.FichierConfigurationLlm, configuration).Match(
            Ok: _ => Result.Ok(configuration),
            Err: erreur => Result.Fail<ConfigurationLlm>(erreur));

    private static OSPlatform SystemeCourant() =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows
        : OperatingSystem.IsLinux() ? OSPlatform.Linux
        : OSPlatform.Create("AUTRE");

    // Rien n'est créé avant la confirmation : on mesure l'espace sur le plus proche dossier existant.
    private static string DossierExistant(string chemin)
    {
        string? courant = Path.GetFullPath(chemin);
        while (courant is not null && !Directory.Exists(courant))
            courant = Path.GetDirectoryName(courant);

        return courant ?? Path.GetPathRoot(Path.GetFullPath(chemin)) ?? chemin;
    }

    private static string Go(long octets) => octets < 1024L * 1024 * 1024
        ? $"{octets / (1024d * 1024):0} Mo"
        : $"{octets / (1024d * 1024 * 1024):0.0} Go";

    private static IResult<ConfigurationLlm> Echec(string message) =>
        Result.Fail<ConfigurationLlm>(new Error(message, ErrorType.Unexpected));
}
