Bundled font assets for deterministic settings previews.

These fonts are included so the workbench can render code/UI font options even
when they are not installed on Windows. The source projects distribute these
faces under open font licenses or similarly permissive terms:

- Cascadia Code: https://github.com/microsoft/cascadia-code
- JetBrains Mono: https://github.com/JetBrains/JetBrainsMono
- Fira Code: https://github.com/tonsky/FiraCode
- Source Code Pro: https://github.com/adobe-fonts/source-code-pro
- IBM Plex Mono and IBM Plex Sans: https://github.com/IBM/plex
- Hack: https://github.com/source-foundry/Hack
- Iosevka: https://github.com/be5invis/Iosevka
- Victor Mono: https://github.com/rubjo/victor-mono
- Roboto: https://github.com/googlefonts/roboto-3-classic

Aptos and SF Pro are not bundled because they are proprietary system fonts.
Their settings entries keep the system font name first and use visible local
fallbacks when the exact face is unavailable.
