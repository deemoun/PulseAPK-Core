# PulseAPK

**PulseAPK** est une interface graphique professionnelle pour l’ingénierie inverse Android et l’analyse de sécurité, construite avec Avalonia (.NET 8). Elle combine la puissance de `apktool` avec des capacités avancées d’analyse statique, le tout dans une interface performante inspirée du cyberpunk. PulseAPK rationalise l’ensemble du flux de travail, de la décompilation à l’analyse, la reconstruction et la signature.

[Voir la démo sur YouTube](https://youtu.be/Mkdt0c-7Wwg)

![PulseAPK UI](images/pulse_apk_decompile.png)

Use the Analysis tab to select the decompiled project folder and run Smali analysis.

![PulseAPK Smali Analysis](images/apktool_analysis.png)

Si vous souhaitez construire (et signer si nécessaire) le dossier Smali, utilisez la section « Build APK ».

![PulseAPK Build APK](images/pulse_apk_build.png)

## Fonctionnalités clés

- **🛡️ Analyse de sécurité statique** : Analyse automatiquement le code Smali pour détecter des vulnérabilités, notamment la détection du root, les vérifications d’émulateur, les identifiants codés en dur et l’utilisation non sécurisée de SQL/HTTP.
- **⚙️ Moteur de règles dynamique** : Règles d’analyse entièrement personnalisables via `smali_analysis_rules.json`. Modifiez les modèles de détection à la volée sans redémarrer l’application. Utilise un cache pour des performances optimales.
- **🚀 Modern UI/UX**: A responsive, dark-themed interface designed for efficiency, with real-time console feedback.
- **📦 Flux de travail complet** : Décompiler, analyser, éditer, recompiler et signer des APKs dans un environnement unifié.
- **⚡ Sûr et robuste** : Comprend une validation intelligente et une prévention des crashs pour protéger votre espace de travail et vos données.
- **🔧 Entièrement configurable** : Gérez facilement les chemins des outils (Java, Apktool), les paramètres de l’espace de travail et de l’analyse.

## Capacités avancées

### Analyse de sécurité
PulseAPK inclut un analyseur statique intégré qui scanne le code décompilé pour détecter des indicateurs de sécurité courants :
- **Détection du root** : Identifie les vérifications pour Magisk, SuperSU et les binaires root courants.
- **Détection d’émulateur** : Trouve les vérifications pour QEMU, Genymotion et des propriétés système spécifiques.
- **Données sensibles** : Analyse les clés API, tokens et en-têtes basic auth codés en dur.
- **Réseau non sécurisé** : Signale l’utilisation de HTTP et les points potentiels de fuite de données.

*Les règles sont définies dans `smali_analysis_rules.json` et peuvent être personnalisées selon vos besoins.*

### Gestion des APK
- **Décompilation** : Décoder facilement les ressources et sources avec des options configurables.
- **Recompilation** : Reconstruire vos projets modifiés en APKs valides.
- **Signature** : Gestion intégrée du keystore pour signer les APKs reconstruits, prêts à être installés sur un appareil.

## Prérequis

1.  **Java Runtime Environment (JRE)** : Requis pour `apktool`. Assurez-vous que `java` est dans votre `PATH`.
2.  **Apktool** : Téléchargez `apktool.jar` depuis [ibotpeaches.github.io](https://ibotpeaches.github.io/Apktool/).
3.  **Ubersign (Uber APK Signer)** : Requis pour signer les APKs reconstruits. Téléchargez la dernière version de `uber-apk-signer.jar` depuis les [releases GitHub](https://github.com/patrickfav/uber-apk-signer/releases).
4.  **.NET 8.0 Runtime** : Requis pour exécuter PulseAPK sous Windows.

## Guide de démarrage rapide

1.  **Télécharger et construire**
    ```powershell
    dotnet build
    dotnet run
    ```

2.  **Configuration**
    - Ouvrez **Settings**.
    - Renseignez le chemin vers `apktool.jar`.
    - PulseAPK détectera automatiquement votre installation Java à partir des variables d’environnement.

3.  **Analyser un APK**
    - **Décompilez** votre APK cible dans l’onglet Decompile.
    - Passez à l’onglet **Analysis**.
    - Sélectionnez le dossier du projet décompilé.
    - Cliquez sur **Analyze Smali** pour générer un rapport de sécurité.

4.  **Modifier & reconstruire**
    - Éditez les fichiers dans le dossier du projet.
    - Utilisez l’onglet **Build** pour reconstruire un nouvel APK.
    - Utilisez l’onglet **Sign** pour signer l’APK de sortie.

## Architecture technique

PulseAPK utilise une architecture MVVM (Model-View-ViewModel) propre :

- **Core** : .NET 8.0, Avalonia.
- **Analysis** : Moteur d’analyse statique personnalisé basé sur des regex avec règles rechargées à chaud.
- **Services** : services dédiés pour l’interaction avec Apktool, la surveillance du système de fichiers et la gestion des paramètres.

## Licence

Ce projet est open source et disponible sous la [Apache License 2.0](LICENSE.md).

### ❤️ Soutenir le projet

Si PulseAPK vous est utile, vous pouvez soutenir son développement en cliquant sur le bouton « Support » en haut.

Mettre une étoile au dépôt aide aussi beaucoup.

### Contribuer

Nous accueillons les contributions ! Notez que tous les contributeurs doivent signer notre [Contributor License Agreement (CLA)](CLA.md) afin que leur travail puisse être distribué légalement.
En soumettant une pull request, vous acceptez les termes du CLA.
