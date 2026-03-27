namespace SignalMesh.Services

open System
open System.Collections.Concurrent
open SignalMesh.Models
open SignalMesh.Clients

type SignalService() =
    let signals = ConcurrentDictionary<string, Signal>()
    let mutable analysesRun = 0

    member _.Create(request: CreateSignalRequest) =
        let id = Guid.NewGuid().ToString("N").[0..7]
        let signal =
            { Id = id
              Name = request.Name
              DataPoints = request.DataPoints
              SampleRate = request.SampleRate
              CreatedAt = DateTime.UtcNow }
        signals.[id] <- signal
        signal

    member _.GetAll() =
        signals.Values |> Seq.toArray

    member _.Get(id: string) =
        match signals.TryGetValue(id) with
        | true, signal -> Some signal
        | _ -> None

    member _.Delete(id: string) =
        match signals.TryRemove(id) with
        | true, _ -> true
        | _ -> false

    member _.IncrementAnalyses() =
        System.Threading.Interlocked.Increment(&analysesRun) |> ignore

    member _.GetStats() : SignalStats =
        let all = signals.Values |> Seq.toArray
        let avgPoints =
            if all.Length = 0 then 0.0
            else all |> Array.map (fun s -> float s.DataPoints.Length) |> Array.average
        { TotalSignals = all.Length
          AverageDataPoints = avgPoints
          TotalAnalysesRun = analysesRun }


module FilterService =

    let movingAverage (data: float array) (windowSize: int) =
        if data.Length = 0 || windowSize <= 0 then [||]
        else
            let ws = min windowSize data.Length
            [| for i in 0 .. data.Length - 1 do
                let start = max 0 (i - ws / 2)
                let endIdx = min (data.Length - 1) (i + ws / 2)
                let slice = data.[start..endIdx]
                Array.average slice |]

    let lowPass (data: float array) (windowSize: int) =
        // Simple low-pass via repeated moving average
        let first = movingAverage data windowSize
        movingAverage first windowSize

    let highPass (data: float array) (windowSize: int) =
        // High-pass = original - low-pass
        let lp = lowPass data windowSize
        if data.Length <> lp.Length then data
        else Array.map2 (fun orig filtered -> orig - filtered) data lp

    let applyFilter (signal: Signal) (filterType: string) (windowSize: int) : Result<FilterResult, string> =
        let ws = if windowSize <= 0 then 3 else windowSize
        let resultData =
            match filterType.ToLowerInvariant() with
            | "moving_average" -> Ok (movingAverage signal.DataPoints ws)
            | "low_pass" -> Ok (lowPass signal.DataPoints ws)
            | "high_pass" -> Ok (highPass signal.DataPoints ws)
            | other -> Error (sprintf "Unknown filter type: %s" other)

        resultData
        |> Result.map (fun rd ->
            { OriginalId = signal.Id
              FilterType = filterType
              WindowSize = ws
              ResultData = rd })


module AnomalyService =

    let detect (signal: Signal) (threshold: float) : AnomalyResult =
        let data = signal.DataPoints
        let thresh = if threshold <= 0.0 then 2.0 else threshold

        if data.Length < 2 then
            { SignalId = signal.Id; Anomalies = [||]; Threshold = thresh }
        else
            let mean = Array.average data
            let stdDev =
                let variance = data |> Array.map (fun x -> (x - mean) ** 2.0) |> Array.average
                sqrt variance

            let anomalies =
                if stdDev = 0.0 then [||]
                else
                    data
                    |> Array.mapi (fun i v ->
                        let zScore = abs ((v - mean) / stdDev)
                        if zScore > thresh then
                            Some { Index = i; Value = v; ZScore = Math.Round(zScore, 4) }
                        else None)
                    |> Array.choose id

            { SignalId = signal.Id
              Anomalies = anomalies
              Threshold = thresh }
