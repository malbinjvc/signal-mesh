namespace SignalMesh.Models

open System
open System.Text.Json.Serialization

[<CLIMutable>]
type CreateSignalRequest =
    { Name: string
      DataPoints: float array
      SampleRate: float }

[<CLIMutable>]
type Signal =
    { Id: string
      Name: string
      DataPoints: float array
      SampleRate: float
      CreatedAt: DateTime }

[<CLIMutable>]
type FilterRequest =
    { SignalId: string
      FilterType: string
      WindowSize: int }

[<CLIMutable>]
type FilterResult =
    { OriginalId: string
      FilterType: string
      WindowSize: int
      ResultData: float array }

[<CLIMutable>]
type AnomalyDetectRequest =
    { SignalId: string
      Threshold: float }

[<CLIMutable>]
type AnomalyEntry =
    { Index: int
      Value: float
      ZScore: float }

[<CLIMutable>]
type AnomalyResult =
    { SignalId: string
      Anomalies: AnomalyEntry array
      Threshold: float }

[<CLIMutable>]
type CompareRequest =
    { SignalIdA: string
      SignalIdB: string }

[<CLIMutable>]
type CompareResult =
    { SignalIdA: string
      SignalIdB: string
      Correlation: float
      SimilarityScore: float
      Analysis: string }

[<CLIMutable>]
type AnalysisResult =
    { SignalId: string
      Mean: float
      StdDev: float
      Min: float
      Max: float
      ZeroCrossings: int
      DominantFrequencyEstimate: float
      Analysis: string }

[<CLIMutable>]
type SignalStats =
    { TotalSignals: int
      AverageDataPoints: float
      TotalAnalysesRun: int }

[<CLIMutable>]
type HealthResponse =
    { Status: string
      Timestamp: DateTime }

[<CLIMutable>]
type ErrorResponse =
    { Error: string }
