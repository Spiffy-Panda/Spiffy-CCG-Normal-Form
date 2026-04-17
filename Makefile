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

.PHONY: all help restore build test clean format ci \
        ccgnf-lint ccgnf-build

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
	@echo ""
	@echo "  make ccgnf-lint (future) validate all .ccgnf source files"
	@echo "  make ccgnf-build (future) preprocess .ccgnf into intermediates"
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
