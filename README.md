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

Download single-file AOT-compiled binaries from [Releases](https://github.com/FactusConsulting/inferstat/releases) — Linux x64/arm64, macOS x64/arm64, Windows x64. No runtime required.

Or build from source:

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

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0`  | Success |
| `74` | Endpoint or sub-endpoint unreachable |
| `78` | Configuration error |
| `1`  | Unexpected error |

## License

MIT © Factus Consulting ApS
