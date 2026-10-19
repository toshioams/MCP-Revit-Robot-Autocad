# Padrão TMS de detalhamento (Revit) - válido para todos os projetos

Versão 2, 17/10/2026 (seção 3A: fabricação e montagem das peças). Versão 1, 10/10/2026. Origem: projeto 26-083 (laje alveolar / reforço metálico, André Rachid), adotado pelo usuário como padrão da TMS Projetos.
Como fazer (técnicas, scripts, erros e acertos): `ROTEIRO-DETALHAMENTO-ACO.md`. Regras gerais de prancha: `..\sessoes\CLAUDE.md` seção 7.
Em caso de conflito, vale o pedido do usuário no projeto; registrar a exceção no .md do projeto.

---

## 1. Sequência das folhas (estrutura metálica / ligações)
1. Plantas de forma (concreto, aço) por nível.
2. Isométricos gerais.
3. **Plantas de indicação de ligações**, uma por nível, em 1:50 e A1, **antes** dos detalhes.
4. Detalhes de montagem das ligações (cortes e vistas, 1:5).
5. Detalhes de fabricação das peças (uma vista por peça diferente).
6. Isométricos das ligações (vistas 3D travadas).
7. Folhas de tipos adicionais (ex.: placa base tipo 2), com legenda própria.
- Numeração sequencial, sem sufixo. Ao inserir folhas, renumerar as seguintes e **regenerar** todo texto que cita folha.
- A1 é o padrão; A2 só para folhas de fabricação ou isométricos pequenos.

## 2. Nomenclatura (ABNT; representação NBR 14611, cálculo NBR 8800)
| Prefixo | Significado |
|---|---|
| VM-n | viga metálica principal (apoio) |
| VS-n | viga metálica secundária |
| CH-nn | chapa (furação diferente = marca diferente: CH-01, CH-02) |
| L-n | cantoneira |
| PAR-n | parafuso (nunca "P", que é pilar) |
| S-n | solda |
| RC-n | recorte |
| **PB-n** | indicação de ligação aço-concreto (placa base tipo n) nas plantas |
| **LG-n** | indicação de ligação viga-viga tipo n nas plantas |
- A marca vai no parâmetro "Marca" do elemento (para as tabelas).
- Os títulos das vistas começam pela indicação: "PB-1 - FRENTE", "LG-2 - CORTE A", "ISO. LG-1 - LIG. TIPO 1". São títulos curtos, de uma linha.

## 3. Conteúdo obrigatório por tipo de ligação
1. **Montagem:** corte lateral e vista de frente (1:5), com o concreto existente identificado ("(EXIST.)").
2. **Fabricação:** uma vista por peça diferente (chapa, extremidade de perfil com recortes, cantoneira), com dimensões, espessura, furos (diâmetro, quantidade, bordas, passos), linhas de centro, quantidade total e peso. **Nunca** "igual ao tipo X exceto..." em vez de detalhe.
3. **Isométrico travado** por tipo (ilustrativo, sem escala; cotas nas folhas de detalhe).
4. **Símbolo de solda** em toda junta soldada (ABNT/AWS): filete, perna, todo o contorno quando for o caso, bandeira de solda de campo, marca S-n na cauda. Nos isométricos, o símbolo vai na folha, sobre a vista.
5. Indicação na planta de ligações (PB-n / LG-n) em **todas** as ligações, com chamada até o ponto.

## 3A. Conjunto de FABRICAÇÃO e MONTAGEM das peças (padrão do usuário, 17/10/2026, projeto 25-112)
1. **Folha de seções transversais:** todas as seções usadas no projeto, desenhadas em escala (1:2 ou 1:5) e cotadas: altura, largura, aba/enrijecedor, espessura (e raio quando relevante). Cada uma com o nome do perfil, material, peso por metro e as marcas que a usam.
2. **Uma vista por marca de peça** (pilar, viga, terça, barra de treliça, contraventamento, corrente):
   - elevação da peça com **cotas alinhadas à peça** (inclinadas quando a peça é inclinada; nunca só horizontais/verticais);
   - **comprimento de corte** total e, em cadeia a partir de uma ponta, a posição de cada furo, chapa, clipe, recorte e de **cada peça que chega** (interseção com pilares e vigas), com a total;
   - corte das pontas (ângulo ou chanfro cotado);
   - **identificação da peça**: marca escrita na elevação, perfil, comprimento de corte, quantidade, peso unitário e total;
   - seção transversal da peça e projeção das peças que chegam, com as marcas delas;
   - solda de oficina na folha da peça; solda de campo indicada na montagem.
3. **Plantas de montagem** (uma por nível, 1:50): a **marca de cada peça** (pilares, vigas, terças, treliças, contraventamento, correntes) junto da peça, para o montador colocar cada peça na posição certa; cotas de locação pelos eixos, **posição de cada pilar em relação às vigas** (interseção pilar x viga: distância à ponta da viga e entre pilares) e níveis de topo/base.
4. Elevações de montagem quando houver peças em vários níveis (posição vertical, topo e base de pilares).
5. Lista de material por marca (perfil, comprimento, quantidade, peso), chapas, chumbadores, parafusos, porcas e arruelas, gerada do modelo.
6. **Decisões levadas ao usuário sempre com imagem**: 3D com a peça destacada, marca da peça, o problema e as opções, num documento único (PDF), nunca só por id ou texto.

## 4. Notas e legendas (acima do carimbo, na largura da tabela de revisões)
- **NOTAS GERAIS** do conjunto, na ordem: unidades; nomenclatura; materiais (perfis ASTM A572 Gr.50, chapas e cantoneiras ASTM A36, salvo cálculo); parafusos; soldas (eletrodo revestido E70XX, campo/oficina, inspeção); furação (broca, proibido maçarico, folga); furos no concreto (broca diamantada, pacômetro, epóxi); contato pleno das chapas; recortes; proteção anticorrosiva; sequência de montagem; conferir medidas no local.
- **Nota de escopo**, no topo das notas gerais, em negrito e sem número, quando o escopo excluir elementos (ex.: "ESTE PROJETO NÃO CONTEMPLA ANÁLISE DE PILARES E FUNDAÇÕES POR LIMITAÇÃO DO ESCOPO."). Logo abaixo, em texto normal, o complemento que transfere a verificação: quem verifica, para quais cargas e antes de quê. Lembrar o usuário de que a exclusão também precisa constar na proposta/contrato e na ART.
- **LEGENDA DE PEÇAS E SOLDAS:** uma linha por marca (perfil/chapa, dimensões, material, quantidade por ligação, quantidade total, onde está o detalhe).
- **Tabela de indicações** em cada planta de ligações: indicação, descrição, quantidade no nível e detalhe/folha de montagem, fabricação e isométrico. **Gerada do modelo**, nunca digitada.
- Linhas novas em notas numeradas sem renumerar as existentes (`ListType.None`). Manter a formatação (FormattedText).

## 5. Cotas e anotação
- Cotas em mm (estilo de cota do usuário). Cadeia (bordas e passos) mais a total.
- Peças de Steel Connection: cotar em linhas de detalhe "<Linha de centro>" coincidentes, com medidas lidas do modelo (CustomExporter).
- **Zero sobreposição** (regra universal): nenhum texto, cota, símbolo, título ou tabela sobre outro, nenhuma chamada cruzando outra ou uma cota, nada sobre o carimbo, a moldura ou a tabela de revisões.
- **Só o que interessa em cada vista de detalhe** (regra do usuário, 17/10/2026):
  - vista de **fabricação** de peça: só a peça e as chapas soldadas nela;
  - vista de **treliça**: só as barras da treliça (sem tocos, terças, vigas, concreto);
  - vista de **ligação/conexão**: só as barras conectadas e a própria conexão (chapas, parafusos, chumbadores, soldas).
  - O resto fica oculto, não em meio-tom. Cortes e vistas de **forma** e plantas de **locação** de ligações podem mostrar tudo.
- **Vínculos e referências em subjacência (meio-tom)** em todas as vistas (padrão do usuário, 17/10/2026): vínculo RVT/IFC do concreto e DWG importados com meio-tom por elemento em cada vista, para não poluir o desenho do aço; vista nova já nasce assim.
- Símbolo de solda à esquerda do alvo, com o cotovelo à esquerda e a chamada passando por baixo dele. Nunca espelhado.

## 6. Modelo coerente com o desenho
- Antes de detalhar: inventário por código (tipos, quantidades, posições), interferências, duplicatas, furação real x detalhe. Mostrar a contagem ao usuário.
- Divergência entre modelo e detalhe: perguntar qual vale e corrigir o modelo (furação de Steel Connection pela interface do Revit; o Claude pré-seleciona os elementos e confere depois).
- Soldas do modelo com Localização coerente com as notas (campo/oficina).

## 7. Revisões
- Toda alteração entra numa revisão com descrição clara, aplicada em todas as folhas afetadas. A revisão do carimbo e o total de folhas são atualizados.

## 8. Fluxo e verificação (obrigatórios)
1. Salvar e fazer backup antes de alterar; salvar ao fim de cada etapa; em erro, desfazer a etapa.
2. Plano em ordem cronológica aprovado pelo usuário antes de executar.
3. **Depois de cada edição: agente `verificador-revit`** (código + imagem em alta resolução) e correção de tudo o que ele apontar antes de relatar.
4. Atualizar o .md do projeto (decisões, contagens, pendências) e, se houver lição nova, o `ROTEIRO-DETALHAMENTO-ACO.md` e este padrão.

## 9. Ferramentas
- `C:\Users\toshi\claude_verif_lib.py`: verificação de pranchas.
- `MCP REVIT\estudos\<projeto>\scripts\`: scripts por projeto (ex.: `26-083\scripts\tabela_ligacoes.py`, que gera as tabelas de indicações a partir do modelo).
- Família "Weld Symbol" (anotação genérica): carregar no projeto se não existir.
- Tipo de texto com borda 2 mm (indicações PB/LG): largura do texto + 2 × deslocamento da borda.
