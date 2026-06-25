# inferstat

[![Build](https://github.com/FactusConsulting/inferstat/actions/workflows/release.yml/badge.svg)](https://github.com/FactusConsulting/inferstat/actions)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

**Agent-friendly CLI for inspecting LLM inference servers.** Detects llama.cpp, vLLM and Ollama; reports loaded models, slot occupancy, Prometheus metrics — colored output for humans, stable JSON for agents.

Sibling tool to [llmprobe](https://github.com/FactusConsulting/llmprobe). Where `llmprobe` tests an endpoint as a *client*, `inferstat` introspects it as an *operator*.

## What it does

```sh
inferstat health http://infer:8080          # detect server kind, version, loaded model
inferstat models http://infer:8080          # list with family + quantization detection
inferstat slots  http://infer:8080          # llama.cpp slot occupancy
inferstat metrics http://infer:8080         # parse Prometheus /metrics to JSON
inferstat help-ai                           # guidance for AI agents
```

## Install

### Homebrew (macOS / Linux)

```sh
brew tap factusconsulting/tools
brew install inferstat
```

The tap lives at [FactusConsulting/homebrew-tools](https://github.com/FactusConsulting/homebrew-tools); `inferstat` is bumped there automatically on every release.

### Chocolatey (Windows, self-hosted feed)

`inferstat` is published to a self-hosted Chocolatey feed on GitHub Pages (not the
community repository). Add the source once, then install:

```powershell
choco source add -n=inferstat -s="https://factusconsulting.github.io/inferstat/chocolatey/index.json"
choco install inferstat --source=inferstat -y
```

Upgrade with `choco upgrade inferstat --source=inferstat`. The package installs a
single self-contained `inferstat.exe` and shims it onto your `PATH`.

### Prebuilt binaries

Download single-file AOT-compiled binaries from [Releases](https://github.com/FactusConsulting/inferstat/releases) — Linux x64/arm64, macOS x64/arm64, Windows x64. No runtime required.

### Build from source

```sh
git clone https://github.com/FactusConsulting/inferstat.git
cd inferstat
dotnet publish src/inferstat -c Release -o ./publish
```

Requires .NET 10 SDK.

## Examples

```sh
# Quick health check across multiple servers
for s in infer1 infer2 infer3; do
  inferstat health "http://$s:8080" --quiet --json | jq -c "{host:\"$s\"} + ."
done

# Find which servers are running Gemma 4
inferstat models http://infer:8080 --json | jq '.models[] | select(.family=="gemma")'

# Monitor slot occupancy in a script
busy=$(inferstat slots http://infer:8080 --quiet | cut -d/ -f1)
if [ "$busy" -ge 8 ]; then echo "infer is at >80% capacity"; fi

# Get vLLM-specific metrics
inferstat metrics http://infer:8000 --json | jq '.metrics | with_entries(select(.key | startswith("vllm_")))'
```

## Authentication

For endpoints that require a bearer token (vLLM started with `--api-key`, secured
gateways, etc.), pass it via flag or environment variable. The flag wins:

```sh
# Flag-based
inferstat health http://infer:8000 --api-key my-vllm-token

# Environment variable (recommended for scripts/CI)
export OPENAI_API_KEY=my-vllm-token
inferstat models http://infer:8000
inferstat slots http://infer:8000

# Per-call without polluting env or shell history
OPENAI_API_KEY=my-vllm-token inferstat metrics http://infer:8000
```

The env var is named `OPENAI_API_KEY` for consistency with llmprobe and the wider
ecosystem — it's used as a generic bearer token, not OpenAI-specific. Local llama.cpp
and Ollama instances usually don't need authentication.

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0`  | Success |
| `74` | Endpoint or sub-endpoint unreachable |
| `78` | Configuration error |
| `1`  | Unexpected error |

## License

MIT © Factus Consulting ApS
