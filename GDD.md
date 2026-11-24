# GDD — Game Design Document

> Documento vivo. Atualizado a partir do `changelog.md`. Última atualização: 2026-11-24 (título final).

## 1. Visão geral
- **Título:** *Always at Your Service* (em português: *Sempre ao Seu Serviço*; o menu mostra o título no idioma
  escolhido). O nome do produto (janela do Windows, executável `AlwaysAtYourService.exe` e página do navegador) usa o
  título em inglês.
- **Gênero:** terror de sobrevivência em primeira pessoa, com geração procedural
- **Plataforma:** Windows (x86_64), resolução 1920×1080 (tela cheia em janela), publicado no itch.io. Também há uma
  versão para navegador (WebGL, janela de 1280×720 com botão de tela cheia), jogável na própria página do itch.io
  (mesma qualidade gráfica do Windows, mas texturas de paredes, pisos e objetos em 1K em vez de 2K, para o download
  caber no limite de 200 MB por arquivo do itch.io; cerca de 118 MB)
- **Idiomas:** inglês e português (alternável no menu)
- **Público-alvo:** fãs de terror e de jogos de perseguição/furtividade
- **Inspiração:** o ritmo de jogos de terror como Outlast (o jogador nunca descansa: está sempre se escondendo ou fugindo), com estética de maid café

## 2. Conceito
O jogador está trancado em um maid café de vários andares. As "maids" de porcelana que trabalham no
local caçam o jogador. Em cada andar é preciso cumprir um objetivo principal, dividido em etapas menores,
para liberar o elevador até o próximo andar. No último andar, o elevador é a saída.

Frase de efeito: *"Bem-vindo de volta, Mestre. Você nunca vai sair."*

## 3. Loop de jogo
1. Chegar ao andar pelo elevador (tela preta com número do andar e objetivo).
2. Explorar no escuro, achar itens e estações do objetivo, evitando as maids.
3. Ser visto → fugir, quebrar a linha de visão e se esconder.
4. Concluir o objetivo → o elevador se abre → entrar → próximo andar.
5. No último andar, a saída leva à tela de vitória com estatísticas.

## 4. Geração procedural
- Cada partida tem uma *seed*. A quantidade de andares (entre 3 e 10) vem da seed.
- Cada andar é uma grade de células de 4×4 m. **Tamanho:** o 1º andar tem 30 células (480 m²) e cada andar
  seguinte tem 15% a mais de área, até o limite de 70 células (1.120 m², alcançado no 8º andar).
- **Formato:** a planta não precisa ser retangular. Cerca de 90% dos andares têm contorno irregular: recortes nos
  cantos e nas laterais e, às vezes, um pátio interno fechado. Contornos em L, T ou U "puros" são rejeitados, assim
  como partes com menos de 2 células de largura. Os outros 10% são retângulos.
- A planta é dividida em blocos retangulares grandes, e cada bloco é dividido em salas por BSP (partição binária
  do espaço). Às vezes são criados corredores de 1 célula de largura.
- As salas são ligadas por portas: primeiro uma árvore geradora aleatória (garante que tudo seja alcançável),
  depois portas extras que criam **ciclos**, para que seja possível despistar as maids durante as perseguições.
- **Rotas alternativas:** entre quaisquer duas áreas principais (sala de chegada, sala de saída e toda sala maior
  que 2×2 células) existem pelo menos duas rotas que não compartilham nenhuma sala nem porta. Nenhum corredor,
  sala ou porta pode ser o único caminho entre elas, então não existe o padrão "um corredor central com salas
  penduradas". Só salas pequenas (2×2) podem ser becos sem saída, e todo corredor liga pelo menos duas salas.
  Um validador rejeita qualquer planta que quebre essas regras (e o formato acima) antes de montar o andar.
- O elevador de chegada e o de saída ficam na parede externa, o mais longe possível um do outro.
- Tipos de sala: salão, cozinha, depósito, escritório do gerente, sala dos funcionários, banheiro, palco, corredor.
- Depois de montar o andar, o jogo gera a malha de navegação (NavMesh) e **valida** se a saída, os
  esconderijos, as baterias e os itens do objetivo podem ser alcançados. Se algo falhar, o andar é gerado
  de novo com a próxima seed, então o jogador nunca recebe um andar impossível.
- O número de maids e a dificuldade também aumentam com a profundidade.

## 5. Objetivos por andar
O tipo de objetivo de cada andar é sorteado. Os quatro tipos são embaralhados por partida, então dois
andares seguidos nunca repetem o mesmo objetivo.

| Objetivo | Etapas |
|---|---|
| Restaurar a energia | Achar 2–3 fusíveis → colocá-los no quadro de luz → puxar a alavanca principal (barulhenta) |
| O cartão do gerente | Achar a chave do gerente → destrancar a mesa (revela o código do cofre) → abrir o cofre → passar o cartão no elevador |
| Atender o cliente especial | Reunir chá, bolo e açúcar → servir na mesa reservada (com uma boneca de porcelana) → tocar o sino (muito barulhento) |
| Silenciar a melodia | Silenciar 3–4 caixinhas de música → silenciar o gramofone que começa a tocar |

Itens importantes brilham levemente para serem encontrados no escuro. Interagir com algo que ainda não
está pronto mostra uma dica explicando o que falta.

## 6. Mecânicas do jogador
- **Movimento:** andar, correr (fôlego limitado), agachar (mais silencioso e mais difícil de ver). Correr estando
  agachado levanta o jogador na hora e ele já sai correndo (se houver espaço acima dele; embaixo de algo baixo ele
  continua agachado). Para agachar de novo, basta apertar agachar outra vez.
- **Interação:** olhar para o objeto e apertar ou segurar E. As ações longas mostram uma barra de progresso.
- **Celular com visão noturna:** imagem verde monocromática com granulação; gasta bateria. Há baterias
  pelo andar. Usar a visão noturna não deixa o jogador mais visível para as maids.
- **Esconderijos:** armários (com fresta para espiar) e mesas com toalha (visão rente ao chão).
- **Prender a respiração:** escondido, segurar Espaço. É limitado; se acabar, o jogador ofega alto. Escondido e sem
  prender, ouve-se a própria respiração assustada (inspira e expira); ao prender, uma inspiração rápida e o silêncio;
  ao soltar, o ar saindo.
- **Cansaço:** correndo, a respiração acelerada aparece quando o fôlego cai abaixo de 70% e fica mais alta conforme
  ele acaba; sem fôlego, o jogador arfa até recuperar o suficiente para correr de novo. Esses sons são só para o
  jogador: as maids ouvem os ruídos do jogo (passos, portas, ofego) e a respiração dentro do esconderijo, não estes.
- **Ruído:** passos (correr faz mais barulho), portas, tarefas barulhentas.

## 7. Inimigas — as maids de porcelana
- **Visão:** cone de visão com linha de visada. O alcance depende de o jogador estar na luz ou no escuro,
  agachado ou correndo, e com o celular ligado ou não.
- **Medidor de atenção:** ver o jogador enche o medidor (mais rápido de perto). Na metade, ela vai investigar;
  cheio, começa a perseguição.
- **Audição:** ruídos atraem as maids. A audição é calculada por *ray tracing* de som (ver seção 12, "Áudio"):
  ruídos abafados por paredes ou portas fechadas são ouvidos de mais perto e localizados com menos precisão.
  Na perseguição, depois de perder o jogador de vista, a maid segue os barulhos que ele faz.
- **Estados:** patrulha → investigação → perseguição → busca → patrulha.
- **Maid de patrulha:** grita quando começa uma perseguição, chamando as outras.
- **Violino da perseguição:** quando uma maid começa a perseguir o jogador, um golpe de violino dissonante soa
  na hora e se repete num ritmo inversamente proporcional à distância até ela (quanto mais perto, mais rápido;
  ver seção 12, "Áudio"). É também uma pista: o ritmo diz o quanto ela está perto sem precisar olhar para trás.
- **Maid-chefe:** mais forte. Vagueia em direção à área do jogador e responde a todos os alarmes.
  É a terceira caçadora da escada de ameaça (abaixo): aparece no 5º andar no Normal, no 7º no Fácil e no 4º no
  Hardcore. Nunca há mais de uma.
- **Quantidade de maids por andar (escada de ameaça):** a pressão cresce aos poucos, no máximo uma maid a mais por
  andar e uma novidade de cada vez. Cada degrau ímpar acrescenta uma caçadora (maid de patrulha; a terceira é a
  maid-chefe) e cada degrau par coloca uma maid do sino junto às caçadoras do degrau anterior. No Normal, o andar n
  é o degrau n; o Hardcore começa um degrau à frente; o Fácil sobe dois degraus a cada três andares.
  P = maid de patrulha, C = maid-chefe, S = maid do sino.

  | Andar | Fácil | Normal | Hardcore |
  |---|---|---|---|
  | 1 | 1P | 1P | 1P + S |
  | 2 | 1P | 1P + S | 2P |
  | 3 | 1P + S | 2P | 2P + S |
  | 4 | 2P | 2P + S | 2P + C |
  | 5 | 2P | 2P + C | 2P + C + S |
  | 6 | 2P + S | 2P + C + S | 3P + C |
  | 7 | 2P + C | 3P + C | 3P + C + S |
  | 8 | 2P + C | 3P + C + S | 4P + C |
  | 9 | 2P + C + S | 4P + C | 4P + C + S |
  | 10 | 3P + C | 4P + C + S | 5P + C |

  As caixas de doces acompanham as maids do sino: andar sem maid do sino não tem doces.
- **Sem acampar o esconderijo:** as maids não ficam esperando o jogador sair. Depois de 12 s escondido, se nenhuma
  maid está na pista dele (perseguindo, pegando ou ainda ouvindo a respiração), elas desistem da área: param de
  procurar e vão patrulhar outras partes do andar. Enquanto ele continua escondido, e por mais 15 s depois que ele
  sai, os destinos de patrulha evitam um raio de 8 m em volta do esconderijo e a maid-chefe para de vaguear em
  direção ao jogador. Barulhos continuam atraindo as maids normalmente.
- **Maids espalhadas:** ao escolher o próximo ponto de patrulha, cada maid prefere lugares a pelo menos 12 m das
  outras maids e de para onde elas estão indo; numa busca, duas maids não vão verificar o mesmo esconderijo.
- **Maid do sino:** fica parada no seu posto e nunca ataca. Se vê o jogador por tempo suficiente (medidor cheio),
  toca o sino e chama a maid mais próxima. Se o jogador carrega caixas de doces, ela hesita antes de tocar:
  +1,5 s por caixa, no máximo +4 s, e o jogador precisa continuar à vista dela durante esse tempo. Sem doces,
  ela toca assim que o medidor enche. Entregar uma caixa de doces a acalma pelo resto do andar.
- **Regras justas de esconderijo:** uma maid só tira o jogador do esconderijo se o viu entrar (ela o viu há
  menos de 0,6 s e está olhando para o esconderijo, sem nada no caminho) ou se, durante uma busca, está ao lado
  do esconderijo e ouve o jogador respirar (ele não está prendendo a respiração). Paredes abafam a respiração
  como qualquer outro som, então ela não a ouve do outro lado de uma parede.
- **Vozes:** cada modelo de maid tem a sua voz (fofa, de mulher adulta) e fala no idioma do jogo (inglês ou
  português). Quando uma maid de patrulha ou a chefe começa a ouvir a respiração do jogador escondido, ela diz uma
  frase ("Eu ouvi alguma coisa?", "Eu juro que ouvi alguma coisa!", "De onde vem esse barulho?"…). É o aviso para
  prender a respiração: ela precisa ouvir por 1,4 s para descobrir o esconderijo. A maid do sino fala quando nota o
  jogador ("Hum… tem alguém aí?") e chama as outras quando começa a tocar ("Pessoal! Chegou um cliente!"). Cada maid
  espera 7 s entre frases, e duas maids nunca falam ao mesmo tempo.
- Há um período de graça depois de nascer ou de voltar a um ponto seguro.

## 8. Dificuldades
| Dificuldade | Inimigas | Ao ser pego |
|---|---|---|
| Fácil | Menos maids (a escada de ameaça sobe mais devagar), e mais lentas | Volta à última etapa do objetivo concluída |
| Normal | Padrão (andar n = degrau n da escada de ameaça, ver seção 7) | Reinicia o andar atual (mesma seed) |
| Hardcore | Mais maids (um degrau à frente), e mais rápidas | Fim de jogo; tentar de novo gera um café novo (nova seed) |

## 9. Controles
| Ação | Teclado/Mouse | Controle |
|---|---|---|
| Mover | WASD | Analógico esquerdo |
| Olhar | Mouse | Analógico direito |
| Correr | Shift | L3 |
| Agachar | Ctrl / C | B |
| Interagir | E | A |
| Celular (visão noturna) | Botão direito / F | LT |
| Prender a respiração | Espaço | RB |
| Pausar | Esc | Start |

## 10. Estados do jogo e tratamento de erros
Menu principal → Carregando andar → Jogando ⇄ Pausado → Pego → (ponto seguro | reiniciar andar | fim de jogo)
→ Transição de elevador → … → Vitória → Menu principal.
- O menu de pausa tem a opção **Voltar ao último ponto seguro**, para quando o jogador ficar preso
  (não pode ser usada durante uma perseguição).
- **Vigia de segurança:** se o jogador cair para fora do mapa, ele é colocado de volta na última posição segura.
- Validação de cada andar gerado (ver seção 4).
- Telas de vitória e de fim de jogo com estatísticas: andar alcançado, tempo, vezes pego, dificuldade e seed.

## 11. Interface
- Menu principal com fundo "vivo": uma sala gerada onde uma maid de porcelana fica parada sob uma luz
  piscando, enquanto a cantiga de ninar das caixinhas de música (a mesma do objetivo "Silenciar a melodia") toca em
  loop na sala. Opções: Jogar (dificuldade + seed opcional), Configurações, Como jogar, Créditos, Sair (a versão para
  navegador não tem o botão Sair, nem no menu de pausa: uma aba do navegador não pode ser fechada pelo jogo).
- Configurações: idioma, sensibilidade do mouse, inverter o eixo Y, campo de visão, volume, brilho,
  modo de agachar, contador de FPS, qualidade das texturas (Alta 2K / Média 1K / Baixa 512, aplicada na hora; para
  PCs com pouca memória de vídeo) e tela cheia (no navegador, pede a tela cheia ao navegador). Tudo é salvo.
- HUD: mira, aviso de interação com barra de progresso, objetivo e etapa atual, dicas, bateria do celular,
  fôlego, respiração, banner do andar e medidor de ruído.
  - **Medidor de ruído** (canto inferior esquerdo, inspirado nas partes furtivas dos jogos Batman: Arkham): barras
    animadas em forma de onda sonora mostram, em tempo real, quanto barulho o jogador está fazendo: agachado < andando
    < correndo, mais portas de armário, itens, tarefas do objetivo, o ofego quando o fôlego acaba e a respiração
    dentro do esconderijo. Uma marca mostra a partir de que nível alguma maid ouviria o jogador naquele momento. Ela
    usa a mesma regra de audição das maids (distância, sensibilidade de cada maid e paredes pelo áudio com
    *ray tracing*), então o que o medidor mostra é o que elas realmente ouvem. Cores: verde-menta abaixo de 70% da
    marca, âmbar perto dela e vermelho quando uma maid ouve. Sem nenhuma maid ao alcance, a marca fica apagada no fim
    da escala.
- Pós-processamento: vinheta e granulação de base. Durante perseguições, uma pulsação vermelha com
  aberração cromática.

## 12. Arte e áudio
- Cenário: um maid café que já foi fofo e alegre e apodreceu com os anos. As cores pastel continuam lá, mas
  desbotadas, manchadas, descascando e encardidas. Paredes, pisos, tetos e acabamentos usam texturas PBR (cor,
  relevo e brilho) de no máximo 2K: conjuntos da Poly Haven (CC0) recoloridos para a paleta do café e três papéis de
  parede próprios. Mobiliado com o Kenney Furniture Kit (CC0) conforme o tema de cada sala.
  - Salão: papel de parede rosa listrado com corações, barra de renda e rodapé framboesa, descascando em manchas
    úmidas; piso xadrez rosa e creme encardido. Corredores: papel creme e menta com bolinhas rosa, piso de madeira
    gasta. Sala dos funcionários: papel lilás com estrelas. Cozinha: tinta verde institucional caindo do concreto e
    piso xadrez preto e creme sujo. Banheiro: reboco azul-esverdeado e mosaico menta. Depósito: tijolo pintado de azul.
    Escritório do gerente: painéis de madeira escura e parquet espinha de peixe. Palco: veludo vermelho e tábuas escuras.
  - Em todo lugar: teto com manchas de infiltração, pilares e batentes de madeira com tinta creme lascada, armários
    azul-bebê com portas rosa enferrujando, toalhas de mesa xadrez rosa com manchas de chá, elevadores de chapa
    enferrujada com piso de chapa xadrez.
  - Os papéis de parede cobrem exatamente a altura da parede (3 m): o rodapé, a barra de renda, as escorridas de água
    do teto e a sujeira junto ao chão ficam sempre na altura certa.
  - Objetos das missões (modelos 3D texturizados em escala real, licenças em `credits.md`): fusíveis de cartucho, um
    quadro de luz antigo com a porta aberta encostada na parede e uma chave-faca ao lado como alavanca principal (ao
    puxar, a lâmina fecha); a chave dourada do gerente, uma mesa de escritório de metal velha com o caderno de
    anotações (o código) e um cofre de ferro antigo; bule e açucareiro de um jogo de chá de porcelana floral e um bolo
    de chocolate com morangos; na mesa reservada, prato e xícara do mesmo jogo, uma campainha de balcão de cobre e a
    "cliente": uma boneca de porcelana antiga de pé numa cadeira pintada; caixinhas de música de madeira com a tampa
    aberta (espelho e vidro sobre o mecanismo) e um gramofone de corneta sobre um criado-mudo. Pilhas AA e uma caixa
    de bombons em forma de coração (o presente da maid do sino) completam os itens. Todos ficam sobre criados-mudos
    antigos (dois modelos, escolhidos por posição). Os itens que se pegam brilham levemente (emissão da própria
    textura) para serem achados no escuro.
  - Música: *stingers* de violino (ver "Áudio" abaixo).
- Maids: cinco modelos anime prontos (licença verificada, ver `credits.md`): três variações da maid de patrulha
  (marias-chiquinhas, loira e vestido longo), a chefe e a maid do sino, sem sexualização. Visual de boneca de porcelana *bisque*: a pele usa a textura original de cada modelo,
  parcialmente dessaturada e clareada, com rachaduras finas e acabamento acetinado (pouco brilho), para parecer
  porcelana pintada e não plástico branco. Olhos: órbitas pretas com íris que brilham conforme a atenção da maid.
  Cabelo e roupas mantêm as cores originais de cada modelo, com saturação reforçada e sem o reflexo do céu
  padrão (que deixava tudo acinzentado); a chefe das maids usa um uniforme rosa, que a distingue das outras.
- Animações próprias (feitas no Blender) compartilhadas por todas as maids. Paradas, andando ou procurando, elas
  ficam eretas com as mãos juntas na frente, na cintura, a direita sobre a esquerda (a postura clássica de espera de
  uma maid), ajustada por IK ao corpo de cada modelo. Em todas as animações, um "cone da saia" medido em cada modelo
  impede que mãos e antebraços atravessem a saia ou o avental.
- A maid do sino segura o sino de verdade: o cabo fica dentro da mão fechada e o sino pende para baixo. Ao tocar,
  ela levanta só o braço direito e sacode o sino; a mão esquerda continua na cintura.
- As maids têm corpo sólido: cápsulas ajustadas à forma de cada modelo (cabeça, tronco, saia, braços, pernas, pés)
  acompanham a animação, então o jogador não as atravessa. Elas não bloqueiam a visão nem o alcance das próprias maids.

### Áudio (som com *ray tracing*)
- Todo o som do mundo usa o SDK **Vercidium Audio** 1.11.1 ("Powered by Vercidium Audio", créditos no jogo e em
  `credits.md`). Ele lança raios de som em *threads* de segundo plano da CPU, sem custo na placa de vídeo.
- Cada andar gerado é copiado para o "mundo acústico" como uma grade de *voxels* de 0,25 m (paredes, pisos, tetos,
  pilares e as cabines dos elevadores; os móveis ficam de fora). Isso leva cerca de 6 ms, durante a tela de
  carregamento. As portas dos elevadores e as portas dos armários são caixas móveis: fechadas, abafam o som.
- **O que o jogador ouve:** o volume e o abafamento de cada som seguem o caminho real até o ouvido do jogador
  (direto, contornando cantos, passando por portas ou atravessando uma parede, que corta principalmente os agudos).
  A reverberação da sala (tempo de decaimento, reflexões) também é medida pelos raios e muda de sala para sala.
  Dentro de um armário, o mundo lá fora soa abafado.
- **O que as maids ouvem:** cada maid tem um "ouvido" que mede quanto de um ruído chega até ela. Quanto mais
  abafado, menor a distância de audição (até 30% do alcance normal) e maior o erro de localização (até 3 m).
  Na perseguição, ao perder o jogador de vista, ela continua seguindo os barulhos dele (passos de corrida):
  andar devagar e agachado é a forma de despistá-la.
- **Sons gravados** (19 gravações CC0 do Freesound, cortadas e normalizadas; autores em `credits.md` e nos créditos
  do jogo):
  - Passos do jogador de tênis, diferentes para andar, correr e agachar, e conforme o piso: madeira (corredores,
    escritório, palco) ou piso duro (salão, cozinha, banheiro, depósito e o piso de chapa do elevador). Tecido ao
    agachar e ao levantar.
  - Respiração: assustada no esconderijo, a inspiração ao prender, o ar saindo ao soltar, o ofego quando o fôlego
    acaba, a respiração acelerada da corrida e o arfar do cansaço (ver seção 6).
  - Portas: o armário range ao abrir e bate ao fechar; a toalha da mesa farfalha ao entrar ou sair de baixo dela; as
    portas do elevador têm um som para abrir e outro para fechar, e o sinal do elevador avisa a saída liberada.
  - Sinos: o sino de mão da maid do sino (em *loop* enquanto ela toca) e a campainha de balcão da cliente reservada.
- **Vozes das maids** (ver seção 7): geradas com IA na ElevenLabs. Três vozes criadas para o jogo (maid de patrulha
  de marias-chiquinhas, por volta de 22 anos, alegre e cantada; a chefe, perto dos 30, educada e fria; a maid do
  sino, por volta de 21, tímida e sussurrada) e duas vozes prontas da ElevenLabs para as outras patrulhas (Jessica,
  a loira; Laura, a de vestido longo). Cinco falas por maid, em inglês e português.
- **Stingers de violino** (trilha, não diegéticos: só o jogador ouve, as maids não, e não passam pelo *ray tracing*):
  golpes curtos e secos de violino em acordes dissonantes originais (segundas menores, trítonos, nonas menores),
  montados com notas *spiccato* reais da biblioteca VSCO 2 Community Edition (Versilian Studios, CC0), afinadas um
  pouco "erradas" de propósito, em estéreo e com uma cauda de reverberação escura.
  - **Sem perseguição:** de vez em quando (a cada 35-80 s, ao acaso) soa um golpe isolado, para manter a tensão.
    Nunca logo depois de entrar no andar ou de uma perseguição.
  - **Perseguição:** um golpe no instante em que ela começa; depois, golpes num ritmo de 7 ÷ distância por segundo
    (20 m: um a cada 2,9 s; 7 m: um por segundo; 3,5 m: dois por segundo; no máximo quatro por segundo), recalculado
    a cada quadro. Um "calor" da perseguição (proximidade + tempo de perseguição) só sobe enquanto ela dura: os
    golpes ficam mais agudos (oito acordes, do grave e quente ao agudo e estridente, e até +2 semitons), mais fortes e
    mais saturados.
  - **Saturação harmônica:** os golpes passam por um saturador (curva tanh levemente assimétrica, que acrescenta
    harmônicos pares e ímpares) cuja intensidade segue o calor. O pico continua o mesmo e as partes mais baixas e a
    cauda sobem, então o som parece cada vez mais alto e mais sujo sem estourar (cerca de +9 dB de volume médio do
    começo de uma perseguição até ela encostar no jogador, com o pico em -3 dBFS).
  - **Captura:** um último golpe com todos os registros, na saturação máxima.
- **Ainda sintetizados** (sons provisórios feitos em código, melodias originais): salto de porcelana das maids, o grito
  da maid de patrulha, itens coletados, a alavanca do disjuntor, as caixas de música (tocam até serem silenciadas,
  então dá para encontrá-las pelo som), o gramofone e o arranhão da agulha. Se um som gravado faltar, o jogo usa o
  sintetizado no lugar (respiração e vozes ficam em silêncio).
- Os arquivos ficam em `Resources/Audio/<nome>/` e são gerados pelo script `ArtSource/Work/build_audio.py` a partir
  das gravações originais.
- **Sem o SDK** (por exemplo, numa cópia do repositório sem a biblioteca, que não pode ser redistribuída fora de uma
  build), o jogo continua funcionando: o som e a audição das maids voltam à regra anterior de linha de visada.
- **Versão para navegador (WebGL):** o mesmo *ray tracing* roda na versão WebAssembly do SDK, carregada pela página do
  jogo em paralelo ao Unity; as maids ouvem pelas mesmas regras e os sons são abafados e reverberam como no Windows.
  Como o navegador ignora os filtros de áudio do Unity, o abafamento (um filtro passa-baixa por som) e a reverberação
  da sala (convolução com o tempo de decaimento, o amortecimento dos agudos e o nível medidos pelos raios) são
  recriados com a Web Audio API. O SDK usa *threads*, então a página precisa de isolamento entre origens (no itch.io,
  a opção "SharedArrayBuffer support"); sem isso, a versão para navegador usa a regra de linha de visada.
  Os *stingers* de violino tocam no navegador sem o saturador (o navegador não permite o mixer próprio), com o mesmo
  ritmo, acordes, altura e volume.

## 13. Histórico de mudanças
Ver `changelog.md`.
