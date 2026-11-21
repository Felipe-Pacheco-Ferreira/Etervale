# Limites conhecidos para ajustar depois

O que ficou de fora ou ficou aquém nas tarefas feitas no branch `claude/web-client-85f760`, reunido das conclusões de
cada tarefa. Cada item diz onde mexer. Para quem for corrigir: leia [AGENTS.md](../AGENTS.md) e o doc da área antes.

## Geral (vale para tudo)

- **Nada foi testado em jogo de verdade** por mim: só builds, testes automáticos e capturas da HUD. Antes de publicar
  para jogadores, jogar cada sistema num servidor local (a sessão da HUD jogou localmente só o HUD de celular).
- **O Android não foi compilado** em nenhuma tarefa. Arquivos do Android mudaram (`Client/PristonTale.VulkanAndroid/`,
  por exemplo `RicartenProbe.cs`); compilar e testar no aparelho. O Android também não mostra títulos, arenas, duelos
  nem Battle Royale (não tem as caixas de nome e a HUD dessas partes).
- **Dois testes falham desde antes** e não são de nenhuma tarefa: o endereço `wss://` em `BinaryProtocolTests` e a
  posição da janela em `WindowPlacementTests`. Às vezes um terceiro falha por tempo e passa ao rodar de novo.
- **Só Windows** foi compilado; Linux e macOS não foram verificados.

## Tradução do chat (04)

- O dono precisa pôr a chave em `ChatTranslation` no `appsettings.json` do servidor (`Provider` e `ApiKey`).
- O DeepL não está mapeado para filipino e vietnamita (`ChatTranslators.cs`): com DeepL, quem lê nesses idiomas vê a
  linha original. O Google cobre os dois.

## Duelos (11)

- Não há placar de duelos (vitórias e derrotas guardadas); ficou para depois.

## Arenas (12)

- Os prêmios de uma semana só são pagos se o servidor estiver ligado em algum momento da semana seguinte
  (`ArenaScoreService`); com o servidor desligado a semana inteira, aquela semana não é paga.

## Battle Royale (13)

- Os prêmios ficam em memória até serem pagos (`RoyalePrizeService`): se o servidor parar entre o fim do evento e o
  pagamento, os prêmios daquele evento se perdem. Guardar o resultado no banco antes de pagar resolve.
- Uma ação com itens durante o evento é recusada com a mensagem genérica "personagem não alterado"; uma mensagem própria
  ("não durante o Battle Royale") seria mais clara.
- O kit de nível 100 dá aos conjuradores (Mago, Sacerdotisa, Xamã) botas, luvas e braceletes fracos, e ao Arqueiro e à
  Atalanta armas de nível 80 e a armadura de Natal (`RoyaleKits`): os requisitos das peças de nível 100 não fecham com
  a distribuição de pontos. Ajustar a tabela `Spending` ou escolher peças à mão.
- Companheiros invocados pelo personagem substituto não foram conferidos em detalhe.
- Não há NPC de inscrição: só o comando `/royale` e o menu Mais.
- Na captura da nota o minimapa diz "Ricarten" (a sonda usa esse mapa); no jogo mostra o mapa certo.

## Títulos (15)

- Um item +14 recebido numa troca também dá o título Artesão (os itens que o personagem tem contam na entrada).
- Num título lendário, o dono vê duas linhas no chat (o aviso geral e o pessoal).
- Na primeira entrada depois da atualização, personagens que já estão no nível 160 têm o Imortal anunciado a todos.
- Um título de posição sem ocupante (semana sem partidas, castelo sem dono) continua com quem tinha.

## Roleta de boss (14)

- O teto de 30% por jogador só vale com 4 ou mais participantes; com 2 ou 3, quem causou mais dano pode ter perto de
  90% de chance. Se o dono preferir, dar chance igual quando houver poucos.
- Um boss de horário morto por golpe de outro monstro (o golpe em área do Guardian Saint) não sorteia nem dá drops.
- Os avisos mostram o nome do prêmio como está no catálogo do servidor (em inglês, "Super Aging Stone").
- É preciso estar no mapa quando o boss morre; quem morreu ou saiu antes perde a vaga.

## Idiomas (03)

- **As traduções foram feitas por IA** (filipino, vietnamita, chinês simplificado e japonês) e não passaram por um
  falante nativo. Vale pedir uma revisão, principalmente dos nomes de habilidades, classes, lugares e monstros.
- O importador do mundo não foi rodado contra um banco de verdade com `fil`, `vi`, `zh-CN` e `ja`; só conferi que os
  JSON são válidos e têm as mesmas chaves do espanhol.
- Nos idiomas latinos, o seletor de idioma mostra o chinês e o japonês em letras latinas ("Chinês (simplificado)",
  "Japonês"), porque a fonte latina não desenha os caracteres. A página do navegador mostra "简体中文" e "日本語".
- As fontes do chinês e do japonês usam 13,8 MB e 11,5 MB de memória de textura (formato `Color`), só para quem joga
  nesses idiomas. Um formato de textura menor reduziria isso.
- Os rótulos `Stat.SpellDamage` ("Spell power") e `Stat.SpellPower` ("Spell damage") parecem trocados em todos os
  idiomas; vale conferir no jogo qual é qual.
- O chat digita só os caracteres que a fonte tem: os das traduções e os comuns (GB2312 nível 1 no chinês, kana e kanji
  de nível 1 do JIS X 0208 no japonês). Um caractere raro digitado é ignorado.

## Fonte latina

- A ajuda e as falas do pet usavam setas e um emoticon coreano que a fonte não desenhava; as setas entraram na fonte e o
  emoticon foi trocado por "T_T". O teste `FontCoverageTests` acusa qualquer caractere novo que falte.
