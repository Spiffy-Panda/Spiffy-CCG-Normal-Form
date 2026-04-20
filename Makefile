# Makefile — primary developer interface for the CCGNF project.
#
# This wraps `dotnet` CLI commands so CI and local dev share one surface.
# See CLAUDE.md "Build conventions" for the policy.
#
# Linux is the first-class target. Targets below should be POSIX-compatible;
# avoid GNU-only extensions where a portable form exists.

SOLUTION  := Ccgnf.sln
CONFIG    ?= Debug
RESULTS   := TestResults
# Override to point at a specific dotnet binary. Useful under WSL where the
# Windows install is reachable as dotnet.exe but not plain dotnet:
#   make DOTNET=dotnet.exe ci
DOTNET    ?= dotnet

.PHONY: all help restore build test clean format ci rest \
        web web-dev web-build \
        ccgnf-lint ccgnf-build card-distribution \
        bench-run bench-summary bench-matrix

all: build

help:
	@echo "CCGNF project — make targets"
	@echo ""
	@echo "  make build      $(DOTNET) build ($(CONFIG))"
	@echo "  make test       $(DOTNET) test; results in $(RESULTS)/"
	@echo "  make restore    $(DOTNET) restore"
	@echo "  make clean      $(DOTNET) clean and remove bin/obj/$(RESULTS)/"
	@echo "  make format     $(DOTNET) format (whitespace + analyzer fixes)"
	@echo "  make ci         restore + build + test (invoked by GitHub Actions)"
	@echo "  make rest       run Ccgnf.Rest on http://localhost:19397"
	@echo ""
	@echo "  make web        alias for web-build (npm install + vite build)"
	@echo "  make web-dev    run the Vite dev server on http://localhost:5173"
	@echo "  make web-build  build web/ into src/Ccgnf.Rest/wwwroot"
	@echo ""
	@echo "  make ccgnf-lint (future) validate all .ccgnf source files"
	@echo "  make ccgnf-build (future) preprocess .ccgnf into intermediates"
	@echo ""
	@echo "  make bench-run TOURNAMENT=PairCorrectly [OUT=...]   post + summarise"
	@echo "  make bench-summary RESULTS=... [BASELINE=...] [MATCHUPS=1]"
	@echo "  make bench-matrix  RESULTS=... [BASELINE=...]       decks x bots slice"
	@echo ""
	@echo "Overrides:"
	@echo "  make CONFIG=Release build       debug vs release configuration"
	@echo "  make DOTNET=dotnet.exe test     useful from WSL when only"
	@echo "                                  Windows dotnet is installed"

restore:
	$(DOTNET) restore $(SOLUTION)

build: restore
	$(DOTNET) build $(SOLUTION) --configuration $(CONFIG) --no-restore

test: build
	$(DOTNET) test $(SOLUTION) \
		--configuration $(CONFIG) \
		--no-build \
		--logger "trx;LogFileName=test-results.trx" \
		--logger "console;verbosity=normal" \
		--results-directory $(RESULTS)

clean:
	$(DOTNET) clean $(SOLUTION) 2>/dev/null || true
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
	rm -rf $(RESULTS)
	rm -rf build
	rm -rf .ccgnf-cache
	find . -type f -name '*.ccgnf.expanded' -delete 2>/dev/null || true

format:
	$(DOTNET) format $(SOLUTION)

ci: restore build test

# Start the REST host on the default port. Override CCGNF_HTTP_PORT to change it.
rest: build
	$(DOTNET) run --project src/Ccgnf.Rest --no-build

# ---------------------------------------------------------------------------
# Frontend (Vite + TypeScript) under web/. The build output lands in
# src/Ccgnf.Rest/wwwroot/ so the REST host serves it as static content. Node
# (>= 18) and npm must be on PATH. CI stays dotnet-only; run web-build by
# hand before committing updated wwwroot/ contents.
# ---------------------------------------------------------------------------

web: web-build

web-dev:
	cd web && npm install && npm run dev

web-build:
	cd web && npm install && npm run build

# ---------------------------------------------------------------------------
# Future targets — will be wired up once the CCGNF grammar engine lands.
# Kept here as a documented contract for the linter / preprocessor invocation.
# ---------------------------------------------------------------------------

ccgnf-lint:
	@echo "ccgnf-lint: not yet implemented (see grammar/GrammarSpec.md)"
	@exit 0

ccgnf-build:
	@echo "ccgnf-build: not yet implemented (see grammar/GrammarSpec.md §4)"
	@exit 0

# ---------------------------------------------------------------------------
# Regenerate encoding/cards/DISTRIBUTION.md from the current card files.
# Safe to run anytime; idempotent.
# ---------------------------------------------------------------------------

card-distribution:
	python3 tools/update-card-distribution.py

# ---------------------------------------------------------------------------
# Tournament benches.
#
#   make bench-run TOURNAMENT=PairCorrectly [OUT=...]
#       Posts ai-testing-data/<TOURNAMENT>.tournament.json to a running REST
#       server (start one with `make rest` + CCGNF_AI_EDITOR=1 in another
#       shell), saves the result to ai-testing-data/<TOURNAMENT>.results.json
#       or the path given via OUT=, then prints the bench-summarize.py
#       summary.
#
#   make bench-summary RESULTS=ai-testing-data/<name>.results.json [BASELINE=...]
#       Pretty-print the per-pair summary (and deltas if BASELINE= is given).
#       Use this on any saved results JSON without re-running the bench.
#
#   make bench-matrix RESULTS=ai-testing-data/AiDeckMatrix.results.json [BASELINE=...]
#       Slice a decks-x-bots matrix result by deck and by AI.
#
# ---------------------------------------------------------------------------

TOURNAMENT ?= PairCorrectly
OUT        ?= ai-testing-data/$(TOURNAMENT).results.json

bench-run:
	@curl -sf http://localhost:19397/api/health > /dev/null || \
	  (echo "bench-run: REST not on :19397. Start it with 'make rest' (export CCGNF_AI_EDITOR=1 first)."; exit 1)
	curl -s -X POST http://localhost:19397/api/ai/tournament/run \
	  -H 'content-type: application/json' \
	  --data-binary @ai-testing-data/$(TOURNAMENT).tournament.json \
	  -o "$(OUT)" \
	  -w 'http=%{http_code} time=%{time_total}s\n'
	python3 tools/bench-summarize.py "$(OUT)"

bench-summary:
	@[ -n "$(RESULTS)" ] || (echo "usage: make bench-summary RESULTS=<path> [BASELINE=<path>]"; exit 1)
	python3 tools/bench-summarize.py "$(RESULTS)" $(if $(BASELINE),--baseline "$(BASELINE)",) $(if $(MATCHUPS),--matchups,)

bench-matrix:
	@[ -n "$(RESULTS)" ] || (echo "usage: make bench-matrix RESULTS=<path> [BASELINE=<path>]"; exit 1)
	python3 tools/bench-matrix.py "$(RESULTS)" $(if $(BASELINE),--baseline "$(BASELINE)",)
