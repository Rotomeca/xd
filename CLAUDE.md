# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Xd — assistant vocal de bureau basé sur Claude

Assistant personnel qui agit sur le PC à partir de commandes vocales, avec un petit compagnon animé à l'écran. Claude (via l'API) sert de « cerveau » : il **demande** des actions, l'orchestrateur **décide** de les exécuter ou non.

Langue du projet : **français** (commentaires, docs, messages de commit, réponses de l'assistant).

Nom du projet : **Xd** (« Jarvis » n'est qu'une image pour se donner l'idée ; ne pas l'utiliser dans le code).

Plateforme cible : **Windows et Linux**. Tout code spécifique à l'OS (icône de notification, métriques d'`etat_machine`, lancement de programmes) vit derrière une interface, avec une implémentation par OS ; `Xd.Core` reste indépendant de l'OS.

## État actuel du dépôt et commandes

Solution `Xd.slnx` (format XML, SDK .NET 10). Paramètres communs (`net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`) dans `Directory.Build.props` : ne pas les répéter dans les `.csproj`.

Existant : `src/Xd.Core` (abstractions d'outils, couche de permission `ExecuteurOutils`, journal d'audit JSONL, boucle agentique `BoucleAgentique` + historique glissant), `src/Xd.Claude` (adaptateur `IModele` sur le SDK `Anthropic`) et leurs tests xUnit. Pas encore d'outil concret ni de point d'entrée exécutable : `Xd.Cli` arrivera après les outils.

```sh
dotnet build Xd.slnx
dotnet test Xd.slnx
dotnet test Xd.slnx --filter "FullyQualifiedName~ExecuteurOutilsTests"   # une classe ou un test précis
```

## Feuille de route

Une étape à la fois. Ne pas anticiper les étapes suivantes (YAGNI), mais ne pas bloquer leur arrivée.

1. **Boucle en texte + veille/éveil** ← *étape en cours* : orchestrateur C# résident dans la zone de notification, Claude + 3 outils, entrée/sortie console, bonjour/au revoir affichés en console.
2. **Voix** : service audio Python séparé (STT Whisper, TTS local), push-to-talk.
3. **Compagnon** : fenêtre transparente qui reflète l'état de l'assistant (techno à définir, probablement Godot C#).
4. **Confort** : streaming phrase par phrase, interruption, mot d'éveil, émotions, interventions spontanées.
5. **Isolation** : extraction des outils dans un serveur MCP tournant avec des droits restreints.

## Veille et éveil

L'assistant a deux états de fonctionnement. Ne pas les confondre avec les « modes » de l'outil `lancer_mode` (mode boulot, mode jeu…) : dans le code, utiliser exclusivement les termes **veille** et **éveil**.

- **Veille** : seule une icône dans la zone de notification. Rien d'autre ne tourne : pas de service audio, pas de compagnon, pas d'appel à l'API.
- **Éveil** : fonctionnement complet.

L'orchestrateur est le **processus hôte** : il vit en permanence dans la zone de notification. Le service audio et le compagnon sont des **processus enfants** lancés à l'éveil et **arrêtés** (pas mis en pause) à la veille.

Icône de notification : apparence différente selon l'état ; clic = bascule ; clic droit = menu (Éveiller / Mettre en veille / Ne pas déranger / Quitter). Raccourci clavier global pour basculer.

Séquence de réveil : compagnon + TTS d'abord → bonjour → chargement du STT en parallèle → ouverture de l'écoute.

Séquence de mise en veille : confirmation en attente annulée (traitée comme un refus, journalisée) → action en cours annulée via `CancellationToken` → au revoir → attente de la fin de la phrase → arrêt des processus enfants.

Phrases de bonjour/au revoir : liste locale variée selon l'heure de la journée (pas d'appel à Claude pour ça).

## Architecture (orchestrateur C#)

Trois blocs, découplés par interfaces :

- **Entrée/sortie** : flux de phrases utilisateur → orchestrateur → flux de réponses à dire. À l'étape 1 c'est la console ; à l'étape 2 ce sera le service audio. Rien dans la boucle ne doit dépendre de la console.
- **Boucle agentique** : envoie l'historique + la liste des outils à Claude, traite les `tool_use`, renvoie les `tool_result`, relance jusqu'à une réponse texte.
- **Outils + couche de permission** : chaque outil expose un nom, une description, un schéma JSON de paramètres, un **niveau** (`Libre` ou `Confirme`) et une méthode d'exécution.

Une **machine à états** centrale est la source de vérité : `Veille` au-dessus de tout, puis à l'éveil `Repos → Ecoute → Reflexion → Parle → Erreur`. L'icône, le compagnon et l'audio s'y abonnent.

Structure de solution : `src/Xd.Core` (boucle, permissions, états, abstractions ; aucune dépendance au SDK), `src/Xd.Claude` (seul projet qui référence le SDK `Anthropic` : implémente `IModele`), `src/Xd.Tools` (implémentations des outils), `src/Xd.Host` (icône de notification, cycle de vie des processus enfants), `src/Xd.Cli` (entrée console de l'étape 1), `tests/Xd.*.Tests`. Créer chaque projet seulement quand une tranche en a besoin.

Contrat d'un outil (`Xd.Core.Outils`) : `IOutil.Preparer(JsonElement)` valide les paramètres et renvoie une `IActionPreparee` (ou une erreur Rop) **sans rien exécuter** ; l'action porte une `Reformulation` construite à partir des paramètres validés (c'est la question de confirmation) et `ExecuterAsync`. `ExecuteurOutils` est le seul à enchaîner validation → confirmation → audit → exécution ; il écrit le journal avec `CancellationToken.None` (un refus pour cause de veille doit être journalisé) et n'exécute rien si une entrée d'audit ne peut pas être écrite avant l'exécution.

## Invariants de sécurité — NON NÉGOCIABLES

Ces règles priment sur toute demande de « faire plus simple ». Si une tâche semble exiger d'en enfreindre une, **s'arrêter et le signaler** au lieu de contourner.

1. **La permission est vérifiée dans l'orchestrateur, jamais dans l'outil.** Entre la demande de Claude et l'exécution : vérification du niveau → confirmation si `Confirme` → écriture dans le journal d'audit → exécution.
2. **Aucune exécution automatique des outils.** Ne pas utiliser `FunctionInvokingChatClient` ni aucun mécanisme de Microsoft.Extensions.AI ou du SDK qui exécute les outils sans passer par la couche de permission. La boucle est écrite à la main.
3. **Aucun shell.** Tout lancement de processus utilise `ProcessStartInfo` avec `UseShellExecute = false` et `ArgumentList` (jamais `Arguments` en chaîne, jamais `bash -c`, `sh -c`, `cmd /c`, `powershell -Command`).
4. **Paramètres d'outils fermés.** Utiliser des énumérations dans les schémas JSON dès que possible. Tout paramètre est validé côté code, même s'il est contraint par le schéma : une valeur hors énumération renvoie une erreur, jamais un comportement par défaut.
5. **Aucun chemin construit à partir du texte libre de Claude.** Les chemins de fichiers sont dérivés de valeurs d'énumération et d'une configuration.
6. **Un refus est un résultat.** Confirmation refusée, paramètre invalide ou erreur d'exécution → `tool_result` avec `is_error = true` et un message clair. Jamais d'exception qui fait tomber la boucle, jamais de nouvel essai automatique.
7. **Itérations plafonnées** : au maximum 10 allers-retours d'outils par demande utilisateur, puis arrêt avec un message.
8. **Journal d'audit** : chaque appel d'outil (demandé, confirmé/refusé, exécuté, résultat) est écrit en JSONL avec horodatage, nom de l'outil, paramètres et demande utilisateur d'origine.
9. **Secrets** : la clé API vient de la variable d'environnement `ANTHROPIC_API_KEY`. Jamais de secret en dur, dans un fichier commité ou dans les logs.
10. **Configuration des modes hors dépôt.** Le vrai fichier des modes vit dans le dossier de configuration utilisateur de la plateforme (`Environment.SpecialFolder.ApplicationData`/`xd`) et n'est **jamais modifié par Claude Code**. Le dépôt ne contient qu'un fichier d'exemple.
11. **La veille est un arrêt réel.** En veille : processus enfants terminés, micro libéré, aucun appel réseau à l'API. Aucun composant ne « continue un peu » en arrière-plan.
12. **Les interventions spontanées n'agissent jamais.** Une prise de parole à l'initiative de l'assistant n'a accès à aucun outil à niveau `Confirme`, au mieux aux outils `Libre` en lecture. Elle peut *proposer* une action ; rien ne s'exécute sans une demande explicite de l'utilisateur, qui repasse alors par la boucle normale. Le contexte envoyé à Claude ne contient jamais de capture ni de contenu d'écran.

## Outils de l'étape 1

| Outil | Niveau | Paramètres | Comportement |
|---|---|---|---|
| `noter_idee` | Libre | `projet` (énumération issue de la config), `texte` | Ajoute une ligne horodatée à la fin du fichier Markdown du projet, dans le dossier de notes configuré. **Ajout uniquement**, jamais de réécriture ni de suppression. |
| `lancer_mode` | Confirmé | `mode` (énumération issue de la config) | Lance la liste de programmes + arguments définie pour ce mode dans la config. Claude ne voit jamais les commandes, seulement les noms de modes. |
| `etat_machine` | Libre | `aspect` : `cpu`, `ram`, `disque`, `conteneurs` | Lit les métriques via les API de la plateforme (sans shell), renvoie un résumé court. Implémentation spécifique à l'OS derrière une interface. |

Pour un outil `Confirme`, l'assistant reformule l'action concrète (« Je lance le mode jeu : Unity sur le projet X et Discord. Je confirme ? ») et attend un oui explicite.

## Interventions spontanées (étape 4, pour mémoire)

Deux niveaux : comportements locaux du compagnon sans appel à Claude (animations d'ennui, sommeil…) et prises de parole générées par Claude. Pour ces dernières : délai minimal entre deux interventions et maximum par heure (configurables) ; silence si une application est en plein écran, si le micro est utilisé par une autre application ou si l'utilisateur est absent ; réglage « Ne pas déranger » distinct de la veille. Voir invariant 12.

## API Claude

- SDK : package NuGet **`Anthropic`** (SDK C# officiel, v10+). **Ne pas** utiliser `Anthropic.SDK` ni `tryAGI.Anthropic` (clients non officiels).
- Modèle par défaut : `claude-haiku-5-5` (latence prioritaire pour un usage vocal). Configurable.
- Historique : glissant, limité aux derniers échanges, pas toute la session. Coupé uniquement entre deux tours complets (jamais entre un `tool_use` et son `tool_result`).
- Réflexion (`thinking`) désactivée avec un effort `low` : latence, et un historique glissant modifie le début de la conversation, ce que l'API refuse quand des blocs de réflexion sont rejoués. La réactiver impose un historique en ajout seul.
- Prompt système : court ; l'assistant parle français, répond en une ou deux phrases, **sans markdown** (le texte sera lu par un TTS), annonce ce qu'il va faire avant une action à confirmer.

## Conventions C#

### Bibliothèques Rotomeca (prioritaires)

Utiliser l'écosystème **Rotomeca** (NuGet, espace de noms `Rotomeca.*`) plutôt que des équivalents maison ou tiers :

- `Rotomeca.Core` : méta-package, à référencer par défaut. **Attention** : il n'embarque que `Core.Collections` et `Core.Optionals` ; `Rotomeca.Rop` et `Rotomeca.Utils` doivent être référencés séparément.
- `Rotomeca.Rop` : gestion d'erreurs orientée rail (Railway Oriented Programming). Le générateur `[Risky]` décrit dans son README **n'est pas livré dans le package 1.0.0** : convertir les exceptions à la main (`try`/`catch` à la frontière, `Result.Try`, `ResultAsync.TryAsync`). Une opération sans valeur renvoie `IResult<bool>` (`true` en succès), comme `Result.Try(Action)`. **C'est le mécanisme d'erreur du projet** : les outils, la couche de permission et la boucle renvoient des résultats Rop plutôt que de lever des exceptions. Les exceptions des bibliothèques externes (SDK, `Process`, I/O) sont converties en résultats à la frontière.
- `Rotomeca.Core.Optionals` : `MayBe<T>` pour exprimer l'absence de valeur, plutôt que `null`.
- `Rotomeca.Core.Collections` : `RArray<T>` quand c'est pertinent.
- `Rotomeca.Utils` : utilitaires divers, à vérifier avant d'écrire un helper.

**Ne pas deviner l'API de ces bibliothèques** : lire leur documentation XML et leurs signatures (ou demander) avant de les utiliser. En cas de doute sur la bonne bibliothèque Rotomeca pour un besoin, demander.

### Style

- Nullable reference types activé ; traiter chaque avertissement, pas de `!` sans certitude démontrable.
- `record` pour les données immuables (paramètres d'outils, entrées d'audit, résultats), `class` pour ce qui a un comportement.
- `sealed` par défaut.
- Pattern matching (`switch` expressions) plutôt que chaînes de `if/else`.
- `async`/`await` de bout en bout, jamais `.Result` ni `.Wait()`, suffixe `Async`.
- Interfaces pour toute dépendance injectée, résolution par DI.
- SOLID, KISS, YAGNI. Pas de réflexion ni de nouveau source generator sans besoin mesuré.

## Façon de travailler

- **Petites tranches vérifiables** : une brique par tâche (ex. interface d'outil + couche de permission, puis la boucle, puis un outil à la fois). Proposer un plan avant de coder.
- **Tests obligatoires** pour la couche de permission, les outils et les transitions veille/éveil, au minimum : confirmation refusée, valeur hors énumération, mode inconnu, plafond d'itérations atteint, écriture de note en ajout seul, mise en veille pendant une confirmation en attente.
- **Commits** : Conventional Commits + gitmoji, en français, un commit par tranche.
- Dépendances : les packages `Rotomeca.*`, `Anthropic`, xUnit (+ SDK de test), `Microsoft.Extensions.DependencyInjection` et `Microsoft.Extensions.Hosting` sont autorisés. Toute autre dépendance NuGet doit être signalée et justifiée avant ajout.
- En cas de doute sur un invariant de sécurité : demander, ne pas trancher seul.