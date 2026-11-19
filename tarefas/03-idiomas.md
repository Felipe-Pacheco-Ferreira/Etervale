# Idiomas: espanhol, japonês, vietnamita, chinês e filipino

**Objetivo:** o jogo inteiro (interface, textos do servidor, dados do jogo, página web) em `es`, `ja`, `vi`, `zh` e
`fil`, como o seletor de idioma do Zephira. Hoje só existem `en` e `pt-BR`; o painel web oferece só esses dois.

**Onde está cada texto** (ver `docs/client-settings.md`, parágrafos sobre idiomas):
- `ClientSettings.SupportedLanguages` (registrar o código BCP 47) e o catálogo de idiomas do servidor.
- `Client/PristonTale.Gameplay/Localization/ClientText.resx` + `ClientText.pt-BR.resx` (interface e `Game.<GameTextId>`).
- `DataText.<idioma>.resx` (habilidades, títulos de classe, missões, itens do castelo).
- `Client/PristonTale/Texts/<idioma>/...` (ajuda, guia de nível, pet ajudante, diálogos de missão) — `HudTextFiles`.
- Página: `Web/PristonTale.Web` (Jogar.cshtml, textos `say(pt, en)` em `wwwroot/*.js` do cliente web, `settings-options.js`).
- Fonte: o jogo só desenha os caracteres da sua fonte (`Content.mgcb`, fonte da UI). Japonês e chinês precisam de uma
  fonte com CJK (atlas maior ou fonte dinâmica): é a parte técnica mais pesada.

**Caminho sugerido:**
1. Ler `docs/client-settings.md`, `LocalizationTests` e `HelpGuideTests` (eles conferem catálogos e placeholders).
2. Trocar o par fixo `say(pt, en)` das páginas por um catálogo por idioma (sem quebrar os testes JS existentes).
3. Gerar as traduções (pode usar tradução automática revisada; manter placeholders `{0}` e termos do jogo).
4. Resolver a fonte para CJK e vietnamita (acentos combinados) no cliente desktop, Android e navegador.
5. Liberar os idiomas no painel web (`languages` em `settings-options.js`) e no painel do jogo (`NextLanguage`).

**Pronto quando:** cada idioma passa em `LocalizationTests`/`HelpGuideTests`, as capturas mostram os textos sem
caracteres faltando nas três plataformas, e a troca de idioma no painel funciona.
