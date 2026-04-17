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

.PHONY: all help restore build test clean format ci \
        ccgnf-lint ccgnf-build

all: build

help:
	@echo "CCGNF project — make targets"
	@echo ""
	@echo "  make build      dotnet build ($(CONFIG))"
	@echo "  make test       dotnet test; results in $(RESULTS)/"
	@echo "  make restore    dotnet restore"
	@echo "  make clean      dotnet clean and remove bin/obj/$(RESULTS)/"
	@echo "  make format     dotnet format (whitespace + analyzer fixes)"
	@echo "  make ci         restore + build + test (invoked by GitHub Actions)"
	@echo ""
	@echo "  make ccgnf-lint (future) validate all .ccgnf source files"
	@echo "  make ccgnf-build (future) preprocess .ccgnf into intermediates"
	@echo ""
	@echo "Overrides:"
	@echo "  CONFIG=Release make build"

restore:
	dotnet restore $(SOLUTION)

build: restore
	dotnet build $(SOLUTION) --configuration $(CONFIG) --no-restore

test: build
	dotnet test $(SOLUTION) \
		--configuration $(CONFIG) \
		--no-build \
		--logger "trx;LogFileName=test-results.trx" \
		--logger "console;verbosity=normal" \
		--results-directory $(RESULTS)

clean:
	dotnet clean $(SOLUTION) 2>/dev/null || true
	find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
	rm -rf $(RESULTS)
	rm -rf build
	rm -rf .ccgnf-cache
	find . -type f -name '*.ccgnf.expanded' -delete 2>/dev/null || true

format:
	dotnet format $(SOLUTION)

ci: restore build test

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
