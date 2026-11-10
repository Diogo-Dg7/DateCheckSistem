#!/bin/bash
# Valida cada commit de um intervalo num WORKTREE LIMPO (sem bin/obj), como pede o CLAUDE.md, sem engolir falhas.
#
# Uso (no Git Bash, a partir de qualquer pasta do repositório):
#   tools/validacao/validar-commits.sh [intervalo-ou-commit] [pasta-de-logs]
#   tools/validacao/validar-commits.sh                 # main..HEAD
#   tools/validacao/validar-commits.sh 1a2b3c4         # só esse commit
#
# Para cada commit: `dotnet build` e `dotnet test` da solução inteira, com a saída COMPLETA guardada em logs.
# O commit só é "OK" se: o build terminou com código 0, o test terminou com código 0, CADA projeto de teste que
# existia no commit imprimiu uma linha de resultado (um host de testes que cai sem resultado não passa
# despercebido) e nenhum teste falhou. Sai com código diferente de zero se qualquer commit não for OK.
# Os testes de integração usam o .\SQLEXPRESS real (ver CLAUDE.md); para pular: edite a chamada do dotnet test.

RAIZ=$(git rev-parse --show-toplevel) || exit 99
cd "$RAIZ" || exit 99

ALVO="${1:-main..HEAD}"
LOGS="${2:-${TMPDIR:-/tmp}/dc-validacao-logs}"
WT="../dc-validacao"
mkdir -p "$LOGS"

if [[ "$ALVO" == *..* ]]; then
  COMMITS=$(git rev-list --reverse "$ALVO")
else
  COMMITS=$(git rev-parse "$ALVO")
fi

FALHOU=0
for c in $COMMITS; do
  curto=$(git rev-parse --short "$c")
  titulo=$(git log --format=%s -1 "$c")

  if ! git worktree add -q --detach "$WT" "$c"; then
    echo "$curto => FALHOU(worktree) | $titulo"
    FALHOU=1
    continue
  fi

  (cd "$WT" && dotnet build > "$LOGS/$curto.build.log" 2>&1)
  rc_build=$?

  rc_test="-"
  resultados=0
  testes_com_falha=0
  if [ $rc_build -eq 0 ]; then
    (cd "$WT" && dotnet test --no-build > "$LOGS/$curto.test.log" 2>&1)
    rc_test=$?
    # Um projeto de teste ainda sem testes imprime "Não há nenhum teste disponível" (legítimo, ex.: no commit que
    # cria o projeto). Um host que CAI não imprime nenhuma das duas coisas e continua reprovado.
    resultados=$(grep -cE "^(Aprovado|Com falha)!|Não há nenhum teste disponível" "$LOGS/$curto.test.log")
    testes_com_falha=$(grep -E "^(Aprovado|Com falha)!" "$LOGS/$curto.test.log" | grep -vc "Com falha:     0,")
  fi

  # Projetos de teste que existiam NESTE commit (a lista cresce ao longo das fases).
  esperados=$(git ls-tree -d --name-only "$c" tests/ | wc -l)

  status=OK
  [ $rc_build -ne 0 ] && status="FALHOU(build rc=$rc_build)"
  [ "$rc_test" != "-" ] && [ "$rc_test" != "0" ] && status="FALHOU(test rc=$rc_test)"
  [ $rc_build -eq 0 ] && [ "$resultados" -ne "$esperados" ] && status="FALHOU(projetos com resultado: $resultados de $esperados)"
  [ "$testes_com_falha" -ne 0 ] && status="FALHOU(testes com falha)"
  [ "$status" != "OK" ] && FALHOU=1

  echo "$curto build=$rc_build test=$rc_test resultados=$resultados/$esperados => $status | $titulo"
  git worktree remove --force "$WT"
done

echo "=== FIM (falhou=$FALHOU) | logs em $LOGS"
exit $FALHOU
