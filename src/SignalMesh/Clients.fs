namespace SignalMesh.Clients

open System
open SignalMesh.Models

module MockClaudeClient =

    let computeMean (data: float array) =
        if data.Length = 0 then 0.0
        else Array.average data

    let computeStdDev (data: float array) =
        if data.Length < 2 then 0.0
        else
            let mean = computeMean data
            let variance = data |> Array.map (fun x -> (x - mean) ** 2.0) |> Array.average
            sqrt variance

    let countZeroCrossings (data: float array) =
        if data.Length < 2 then 0
        else
            data
            |> Array.pairwise
            |> Array.filter (fun (a, b) -> (a >= 0.0 && b < 0.0) || (a < 0.0 && b >= 0.0))
            |> Array.length

    let estimateDominantFrequency (data: float array) (sampleRate: float) =
        if data.Length < 2 || sampleRate <= 0.0 then 0.0
        else
            let crossings = countZeroCrossings data
            let duration = float data.Length / sampleRate
            float crossings / (2.0 * duration)

    let analyzeSignal (signal: Signal) : AnalysisResult =
        let mean = computeMean signal.DataPoints
        let stdDev = computeStdDev signal.DataPoints
        let minVal = if signal.DataPoints.Length = 0 then 0.0 else Array.min signal.DataPoints
        let maxVal = if signal.DataPoints.Length = 0 then 0.0 else Array.max signal.DataPoints
        let zeroCrossings = countZeroCrossings signal.DataPoints
        let dominantFreq = estimateDominantFrequency signal.DataPoints signal.SampleRate

        let analysis =
            sprintf "AI Analysis for signal '%s': The signal contains %d data points with a mean of %.4f and standard deviation of %.4f. The signal ranges from %.4f to %.4f. Detected %d zero crossings, estimating a dominant frequency of %.4f Hz at sample rate %.1f Hz. The signal appears to be %s."
                signal.Name
                signal.DataPoints.Length
                mean
                stdDev
                minVal
                maxVal
                zeroCrossings
                dominantFreq
                signal.SampleRate
                (if stdDev < 0.1 then "nearly constant"
                 elif stdDev < 1.0 then "low variance"
                 else "high variance")

        { SignalId = signal.Id
          Mean = mean
          StdDev = stdDev
          Min = minVal
          Max = maxVal
          ZeroCrossings = zeroCrossings
          DominantFrequencyEstimate = dominantFreq
          Analysis = analysis }

    let pearsonCorrelation (a: float array) (b: float array) =
        let n = min a.Length b.Length
        if n < 2 then 0.0
        else
            let a' = a.[0..n-1]
            let b' = b.[0..n-1]
            let meanA = Array.average a'
            let meanB = Array.average b'
            let num = Array.map2 (fun x y -> (x - meanA) * (y - meanB)) a' b' |> Array.sum
            let denA = a' |> Array.map (fun x -> (x - meanA) ** 2.0) |> Array.sum |> sqrt
            let denB = b' |> Array.map (fun x -> (x - meanB) ** 2.0) |> Array.sum |> sqrt
            if denA = 0.0 || denB = 0.0 then 0.0
            else num / (denA * denB)

    let compareSignals (signalA: Signal) (signalB: Signal) : CompareResult =
        let correlation = pearsonCorrelation signalA.DataPoints signalB.DataPoints
        let similarity = (correlation + 1.0) / 2.0

        let analysis =
            sprintf "AI Comparison of '%s' and '%s': Pearson correlation is %.4f (similarity score: %.4f). The signals are %s."
                signalA.Name
                signalB.Name
                correlation
                similarity
                (if similarity > 0.8 then "highly similar"
                 elif similarity > 0.5 then "moderately similar"
                 else "dissimilar")

        { SignalIdA = signalA.Id
          SignalIdB = signalB.Id
          Correlation = correlation
          SimilarityScore = similarity
          Analysis = analysis }
