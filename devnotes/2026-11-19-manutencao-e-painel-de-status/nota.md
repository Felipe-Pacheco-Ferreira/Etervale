# /manutencao e painel de status
Tipo: equipe

O canal de status agora tem um painel que o bot mantém sempre atualizado, e a equipe pode avisar quando os servidores entram em manutenção.

**Painel de status**
- 🟢 verde: todos os mundos no ar, com os jogadores online.
- 🔴 vermelho: algum mundo caiu. O aviso de queda e de volta sai com @everyone.
- 🟡 amarelo: manutenção ligada pela equipe.

**Como usar o /manutencao** (moderador ou acima)
- `/manutencao ativa: True motivo: Nova versão` liga a manutenção. Sai o aviso "Manutenção iniciada" com @everyone e o painel fica amarelo.
- Enquanto a manutenção estiver ligada, as quedas e voltas do servidor não são anunciadas.
- `/manutencao ativa: False` encerra. Sai o aviso "Manutenção concluída" e o painel volta ao normal.

Cada uso fica registrado no histórico dos GMs.
