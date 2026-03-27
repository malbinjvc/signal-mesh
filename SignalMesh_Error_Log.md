# SignalMesh Error Log

## Project: SignalMesh
- **Language**: F# 10.0
- **Framework**: Giraffe 7.0.2 (ASP.NET Core)
- **Date**: 2026-03-27

---

## Error 1: BindJsonAsync Fails to Deserialize CLIMutable Records

**Error**: `POST /api/signals` returns `{"error":"Name is required"}` even when valid JSON `{"Name":"Wave","DataPoints":[1.0,2.0,3.0],"SampleRate":100.0}` is sent.

**Cause**: Giraffe 7.0.2's default JSON serializer uses `System.Text.Json` with `PropertyNamingPolicy = CamelCase`. This means:
- Serialization: F# field `Name` outputs as `"name"` in JSON responses
- Deserialization: expects `"name"` (camelCase) in incoming JSON, not `"Name"` (PascalCase)
- Since `PropertyNameCaseInsensitive` defaults to `false`, PascalCase JSON properties don't match and fields are left as default values (null/0)

**Attempted fixes that failed**:
1. Custom `ISerializer` with `PropertyNameCaseInsensitive = true` - namespace resolution issues with Giraffe's serializer types
2. `FSharpConverter` configuration - `FSharp.SystemTextJson` namespace not resolving
3. `Giraffe.SystemTextJson.Serializer` constructor - type not found in the expected namespace

**Final fix**: Changed all test request JSON to use camelCase property names matching Giraffe's naming policy:
- `"Name"` -> `"name"`, `"DataPoints"` -> `"dataPoints"`, `"SampleRate"` -> `"sampleRate"`
- `"SignalId"` -> `"signalId"`, `"FilterType"` -> `"filterType"`, `"WindowSize"` -> `"windowSize"`
- `"Threshold"` -> `"threshold"`, `"SignalIdA"` -> `"signalIdA"`, `"SignalIdB"` -> `"signalIdB"`

---

## Error 2: Float Formatting in Test JSON

**Error**: Floating-point values serialized without decimal point (e.g., `1` instead of `1.0`), causing JSON deserialization to fail for `float array` fields.

**Cause**: F# `sprintf "%f"` default format includes many decimal places. Array.map with default float-to-string conversion may omit `.0` for whole numbers.

**Fix**: Used `sprintf "%.1f"` to ensure all float values include one decimal place in the test JSON helper function.

---

## Summary
- Total errors encountered: 2
- All resolved successfully
- Tests passing: 40/40
- CI status: Configured
