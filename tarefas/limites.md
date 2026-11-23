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

## HUD de celular no navegador

- A Auto-caça livre do celular ainda não foi vista no jogo local nem num celular, só nos testes
  ([17-auto-caca-livre-navegador.md](17-auto-caca-livre-navegador.md)). Ela vale só no HUD de celular; o HUD de PC do
  navegador continua com a caça de missão. A aba Grupo depende de um servidor com a mensagem `PartyHunt`; num
  servidor antigo ela conta só os golpes do grupo que o próprio cliente vê. O registro e os totais ficam neste
  navegador (no `client-settings.json`), gravados a cada 30 s e ao fechar o painel: fechar a aba de repente pode
  perder os últimos segundos. Os ícones do catálogo de coleta aparecem só para os itens cuja figura o cliente já tem. Os textos
  das outras línguas foram traduzidos por mim e merecem revisão de quem fala cada língua.
- Os tutoriais do celular ainda não foram vistos no jogo local nem num celular, só nos testes. Um toque na janela do
  alvo vira um clique nele: o arrastar e o toque longo não passam. Antes de apontar para Atacar, o cartão de missão
  não é dobrado como no Android, e pode ficar por cima. Num navegador sem popover, o treinador fica por baixo de uma
  Bolsa ou Status abertos. Os textos nas outras línguas são traduções minhas e merecem revisão.
- O editor de HUD do celular ainda não foi visto no jogo local nem num celular, só nos testes. O minimapa não se move
  (o jogo o desenha no canvas); o quadro do alvo e as placas do grupo só podem ser movidos enquanto aparecem. A
  posição fica guardada só neste navegador, não na conta. Os botões aumentados podem ficar um pouco borrados, porque
  a página aumenta o desenho pronto.
- Foi visto no jogo local, mas só no navegador do app em tamanho de celular (844x390), nunca num celular de verdade
  com o dedo. O quadro do alvo contra um monstro e os botões com habilidades e poções reais não foram vistos no jogo;
  o personagem de teste não tinha nenhum dos dois.
- As capturas da nota (`devnotes/2026-11-21-hud-de-celular-no-navegador`) mostram 0 a 2 FPS porque o navegador do app
  reduz a velocidade; num celular o número é o real.
- O cartão de missão e o diário do celular ainda não foram vistos no jogo local nem num celular, só nos testes. Do
  Android ficaram de fora a bússola até o guia, "Trocar asas", "Ir buscar missão" e "Resgatar recompensa" das missões
  de habilidade: dependem do navegador de missões do Android, que o site não tem. A recompensa mostrada é a de
  `HudQuestDocuments.AvalonReward` (a mesma da janela do PC), não a linha "Recompensa" lida da descrição.
- O minimapa e o mapa da região do celular ainda não foram vistos no jogo local nem num celular, só nos testes. O
  minimapa continua desenhado pelo jogo no canvas, com o visual do Android; o mapa da região usa a imagem do mapa do
  campo do jogo original, não a imagem própria que o Android tem para cada campo. O mapa mostra só os NPCs à vista
  (como o Android), e a rota só enquanto o guia da missão anda.
- A Bolsa do celular ainda não foi vista no jogo local nem num celular, só nos testes. Não dá para arrastar itens
  dentro da bolsa (como no Android, os itens só mudam pelos botões da ficha); lojas, armazém e outros serviços
  continuam abrindo o inventário do PC no canvas. "Usar" fecha a Bolsa, porque a confirmação de itens usáveis
  (cristais, teletransporte) ainda é a caixa do PC. O "Usar item de missão" das missões de elite do Android não entrou.
- Status e Habilidades do celular ainda não foram vistos no jogo local nem num celular, só nos testes. A resposta do
  servidor aos pontos e ao treino aparece no chat, não numa linha dentro da janela como no Android. O "Ir ao Mestre de
  Habilidades" do Android não entrou (depende do navegador de missões do Android).
- Segurar um botão de habilidade para escolher a habilidade ainda não foi visto no jogo local nem num celular, só nos
  testes. Os botões que se completam sozinhos ainda podem pegar uma habilidade passiva aprendida, como no Android
  (`TouchSkillSlots.Fill` não usa `Eligible`); a lista e a ficha só oferecem as que se lançam.
- O diálogo com NPC do celular ainda não foi visto no jogo local nem num celular, só nos testes. Não tem o retrato 3D
  vivo do NPC que o Android desenha à esquerda da placa. A janela de clã, de castelo e de Bellatra e a caixa de
  treino do Mestre de Habilidades continuam as do PC no canvas. A placa aparece em até 250 ms depois de falar com o
  NPC, porque a página lê o jogo nesse ritmo, e o diálogo de missão não avança sozinho como no Android.
- O registro de combate do celular ainda não foi visto no jogo local nem num celular, só nos testes. Fica onde o Android
  o põe (à direita, acima da roda de ações); numa tela baixa ele pode ficar por cima do cartão de missão, e isso só dá
  para julgar vendo no jogo. Só conta as mortes de monstros em que o golpe final foi do próprio jogador; o abate de um
  companheiro de grupo não entra. A contagem de abates recomeça a cada entrada no mundo.
- O grupo do celular ainda não foi visto no jogo local nem num celular, só nos testes; o jogo com dois jogadores
  (convite, pedido e comandos) não foi exercitado. Os botões aparecem só depois de alcançar o jogador, como no PC, e
  não só por mirá-lo, como no Android. A janela de grupo do PC (com as abas de amigos e clã) continua no canvas
  quando aberta pelo menu; ela não abre mais sozinha ao formar o grupo, e Amigo adiciona sem abrir a aba de amigos.
  Aceitar uma troca abre a janela de troca do PC no canvas. Numa tela baixa a fileira de botões pode cobrir o
  contador de jogadores sob o quadro do alvo.
- A placa de morte e a janela Eventos & Recompensas do celular ainda não foram vistas no jogo local nem num celular,
  só nos testes. A placa de morte não oferece "Sair do jogo" (o Android também não). Se a escolha for recusada, a placa
  volta sem dizer por quê, como a janela do PC; o Android mostra um aviso. Na janela de recompensas, a resposta do
  servidor a um resgate aparece no rodapé, não num aviso flutuante. Os cartões de dia e as fileiras não têm a borda
  dourada animada do Android, só uma borda dourada fixa.
