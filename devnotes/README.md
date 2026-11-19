# Development notes

One folder per new system players will see, named `yyyy-MM-dd-subject`, holding `nota.md` and up to four images
(`.png`, `.jpg`, `.jpeg`, `.webp`, `.gif`, in name order). The Discord bot carries this folder and publishes each new
note once: to Discord's development channel and to the site's news, which the game shows. See
[docs/discord-bot.md](../docs/discord-bot.md#development-notes).

`nota.md`, in Portuguese, for players:

```markdown
# Novo sistema: título curto

Resumo em uma frase (primeiro parágrafo).

Outros parágrafos: o que mudou e como usar. **Negrito** aparece em negrito no jogo e no Discord.
- Uma linha por item de lista.
```

The game draws only its own font's characters: leave emoji out of the text.

Three kinds, by a line under the title:

| Line | Kind | Where |
| --- | --- | --- |
| none | New system (🛠️) | Discord's development channel and the site's and the game's news |
| `Tipo: melhoria` | Improvement ("✨ Nova melhoria:") | Discord's development channel and the site's and the game's news, with the improvement badge (not announced in Discord's announcements) |
| `Tipo: equipe` | Staff tool ("🧰 Ferramenta da equipe:") | The staff's development channel only, for game masters |

The owner chooses the kind when the work is finished: ask before writing the note.
