namespace Xd.Core.Audit;

/// <summary>
/// Étape du cycle de vie d'un appel d'outil, écrite dans le journal d'audit.
/// </summary>
public enum EtapeAudit
{
    /// <summary>Claude a demandé l'outil.</summary>
    Demande,

    /// <summary>L'appel a été rejeté avant toute confirmation (outil inconnu, paramètres invalides).</summary>
    Rejete,

    /// <summary>L'utilisateur a confirmé l'action.</summary>
    Confirme,

    /// <summary>L'utilisateur a refusé l'action, ou la confirmation a été annulée.</summary>
    Refuse,

    /// <summary>L'action s'est exécutée avec succès.</summary>
    Execute,

    /// <summary>L'exécution a échoué.</summary>
    Echec,
}
