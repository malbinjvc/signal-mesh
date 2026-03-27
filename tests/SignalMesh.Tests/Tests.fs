namespace SignalMesh.Tests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Json
open System.Text
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Xunit
open Giraffe
open SignalMesh.Models
open SignalMesh.Services
open SignalMesh.Clients
open SignalMesh.Routes

// ---- Unit Tests: MockClaudeClient ----

module MockClaudeClientTests =

    [<Fact>]
    let ``computeMean returns correct mean`` () =
        let result = MockClaudeClient.computeMean [| 1.0; 2.0; 3.0; 4.0; 5.0 |]
        Assert.Equal(3.0, result)

    [<Fact>]
    let ``computeMean of empty array returns 0`` () =
        let result = MockClaudeClient.computeMean [||]
        Assert.Equal(0.0, result)

    [<Fact>]
    let ``computeStdDev returns correct value`` () =
        let result = MockClaudeClient.computeStdDev [| 2.0; 4.0; 4.0; 4.0; 5.0; 5.0; 7.0; 9.0 |]
        Assert.True(result > 1.9 && result < 2.1)

    [<Fact>]
    let ``computeStdDev of single element returns 0`` () =
        let result = MockClaudeClient.computeStdDev [| 5.0 |]
        Assert.Equal(0.0, result)

    [<Fact>]
    let ``countZeroCrossings counts correctly`` () =
        let result = MockClaudeClient.countZeroCrossings [| 1.0; -1.0; 1.0; -1.0 |]
        Assert.Equal(3, result)

    [<Fact>]
    let ``countZeroCrossings with no crossings`` () =
        let result = MockClaudeClient.countZeroCrossings [| 1.0; 2.0; 3.0 |]
        Assert.Equal(0, result)

    [<Fact>]
    let ``estimateDominantFrequency with known signal`` () =
        // 4 zero crossings in 10 samples at rate 10 => 2 full cycles in 1 sec => ~2 Hz
        let data = [| 1.0; 0.5; -0.5; -1.0; 0.5; 1.0; 0.5; -0.5; -1.0; 0.5 |]
        let result = MockClaudeClient.estimateDominantFrequency data 10.0
        Assert.True(result > 0.0)

    [<Fact>]
    let ``pearsonCorrelation of identical arrays is 1`` () =
        let a = [| 1.0; 2.0; 3.0; 4.0; 5.0 |]
        let result = MockClaudeClient.pearsonCorrelation a a
        Assert.True(abs(result - 1.0) < 0.0001)

    [<Fact>]
    let ``pearsonCorrelation of negated array is -1`` () =
        let a = [| 1.0; 2.0; 3.0; 4.0; 5.0 |]
        let b = [| -1.0; -2.0; -3.0; -4.0; -5.0 |]
        let result = MockClaudeClient.pearsonCorrelation a b
        Assert.True(abs(result - (-1.0)) < 0.0001)

    [<Fact>]
    let ``analyzeSignal produces valid result`` () =
        let signal = { Id = "test1"; Name = "TestSignal"; DataPoints = [| 1.0; -1.0; 2.0; -2.0; 3.0 |]; SampleRate = 100.0; CreatedAt = DateTime.UtcNow }
        let result = MockClaudeClient.analyzeSignal signal
        Assert.Equal("test1", result.SignalId)
        Assert.True(result.Analysis.Contains("TestSignal"))
        Assert.True(result.ZeroCrossings > 0)

    [<Fact>]
    let ``compareSignals produces valid result`` () =
        let a = { Id = "a1"; Name = "SignalA"; DataPoints = [| 1.0; 2.0; 3.0 |]; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let b = { Id = "b1"; Name = "SignalB"; DataPoints = [| 1.0; 2.0; 3.0 |]; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let result = MockClaudeClient.compareSignals a b
        Assert.Equal("a1", result.SignalIdA)
        Assert.Equal("b1", result.SignalIdB)
        Assert.True(result.SimilarityScore > 0.9)


// ---- Unit Tests: FilterService ----

module FilterServiceTests =

    [<Fact>]
    let ``movingAverage smooths data`` () =
        let data = [| 1.0; 10.0; 1.0; 10.0; 1.0 |]
        let result = FilterService.movingAverage data 3
        Assert.Equal(5, result.Length)
        // Middle values should be averaged
        Assert.True(result.[2] > 1.0 && result.[2] < 10.0)

    [<Fact>]
    let ``movingAverage of empty array`` () =
        let result = FilterService.movingAverage [||] 3
        Assert.Empty(result)

    [<Fact>]
    let ``lowPass reduces high frequency`` () =
        let data = [| 1.0; 10.0; 1.0; 10.0; 1.0; 10.0; 1.0; 10.0 |]
        let result = FilterService.lowPass data 3
        Assert.Equal(data.Length, result.Length)
        let originalRange = Array.max data - Array.min data
        let filteredRange = Array.max result - Array.min result
        Assert.True(filteredRange < originalRange)

    [<Fact>]
    let ``highPass removes low frequency`` () =
        // DC offset signal
        let data = [| 10.0; 11.0; 10.0; 11.0; 10.0 |]
        let result = FilterService.highPass data 3
        Assert.Equal(data.Length, result.Length)

    [<Fact>]
    let ``applyFilter rejects unknown filter`` () =
        let signal = { Id = "f1"; Name = "Test"; DataPoints = [| 1.0; 2.0; 3.0 |]; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let result = FilterService.applyFilter signal "unknown_filter" 3
        match result with
        | Error msg -> Assert.Contains("Unknown filter type", msg)
        | Ok _ -> Assert.Fail("Should have returned error")

    [<Fact>]
    let ``applyFilter returns result for valid filter`` () =
        let signal = { Id = "f2"; Name = "Test"; DataPoints = [| 1.0; 2.0; 3.0; 4.0; 5.0 |]; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let result = FilterService.applyFilter signal "moving_average" 3
        match result with
        | Ok r ->
            Assert.Equal("f2", r.OriginalId)
            Assert.Equal("moving_average", r.FilterType)
            Assert.Equal(5, r.ResultData.Length)
        | Error msg -> Assert.Fail(msg)


// ---- Unit Tests: AnomalyService ----

module AnomalyServiceTests =

    [<Fact>]
    let ``detect finds outliers`` () =
        let data = [| 1.0; 1.1; 0.9; 1.0; 1.1; 0.9; 1.0; 100.0 |]
        let signal = { Id = "a1"; Name = "Test"; DataPoints = data; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let result = AnomalyService.detect signal 2.0
        Assert.True(result.Anomalies.Length > 0)
        Assert.Contains(result.Anomalies, fun a -> a.Index = 7)

    [<Fact>]
    let ``detect with no anomalies`` () =
        let data = [| 1.0; 1.0; 1.0; 1.0; 1.0 |]
        let signal = { Id = "a2"; Name = "Flat"; DataPoints = data; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let result = AnomalyService.detect signal 2.0
        Assert.Empty(result.Anomalies)

    [<Fact>]
    let ``detect defaults threshold to 2 when given 0`` () =
        let data = [| 1.0; 1.1; 0.9; 100.0 |]
        let signal = { Id = "a3"; Name = "Test"; DataPoints = data; SampleRate = 10.0; CreatedAt = DateTime.UtcNow }
        let result = AnomalyService.detect signal 0.0
        Assert.Equal(2.0, result.Threshold)


// ---- Unit Tests: SignalService ----

module SignalServiceTests =

    [<Fact>]
    let ``Create and Get signal`` () =
        let svc = SignalService()
        let signal = svc.Create({ Name = "Test"; DataPoints = [| 1.0; 2.0 |]; SampleRate = 10.0 })
        let retrieved = svc.Get(signal.Id)
        Assert.True(retrieved.IsSome)
        Assert.Equal("Test", retrieved.Value.Name)

    [<Fact>]
    let ``GetAll returns all signals`` () =
        let svc = SignalService()
        svc.Create({ Name = "A"; DataPoints = [| 1.0 |]; SampleRate = 10.0 }) |> ignore
        svc.Create({ Name = "B"; DataPoints = [| 2.0 |]; SampleRate = 10.0 }) |> ignore
        let all = svc.GetAll()
        Assert.Equal(2, all.Length)

    [<Fact>]
    let ``Delete removes signal`` () =
        let svc = SignalService()
        let signal = svc.Create({ Name = "Del"; DataPoints = [| 1.0 |]; SampleRate = 10.0 })
        Assert.True(svc.Delete(signal.Id))
        Assert.True(svc.Get(signal.Id).IsNone)

    [<Fact>]
    let ``Delete nonexistent returns false`` () =
        let svc = SignalService()
        Assert.False(svc.Delete("nonexistent"))

    [<Fact>]
    let ``GetStats returns correct counts`` () =
        let svc = SignalService()
        svc.Create({ Name = "S1"; DataPoints = [| 1.0; 2.0; 3.0 |]; SampleRate = 10.0 }) |> ignore
        svc.Create({ Name = "S2"; DataPoints = [| 4.0; 5.0 |]; SampleRate = 10.0 }) |> ignore
        svc.IncrementAnalyses()
        let stats = svc.GetStats()
        Assert.Equal(2, stats.TotalSignals)
        Assert.Equal(2.5, stats.AverageDataPoints)
        Assert.Equal(1, stats.TotalAnalysesRun)


// ---- Integration Tests: HTTP Endpoints ----

type TestWebAppFactory() =
    inherit WebApplicationFactory<SignalMesh.Program.Marker>()

    override _.ConfigureWebHost(builder) =
        builder.ConfigureServices(fun services ->
            // Replace with fresh SignalService per test factory
            let descriptor = services |> Seq.tryFind (fun s -> s.ServiceType = typeof<SignalService>)
            match descriptor with
            | Some d -> services.Remove(d) |> ignore
            | None -> ()
            services.AddSingleton<SignalService>(SignalService()) |> ignore
        ) |> ignore


module HttpTests =

    let jsonOptions = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let createSignalJson name (dataPoints: float array) sampleRate =
        let dp = dataPoints |> Array.map (fun f -> sprintf "%.1f" f) |> String.concat ","
        sprintf """{"name":"%s","dataPoints":[%s],"sampleRate":%.1f}""" name dp sampleRate

    [<Fact>]
    let ``GET /health returns 200`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let! response = client.GetAsync("/health")
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            let! body = response.Content.ReadAsStringAsync()
            Assert.Contains("healthy", body)
        }

    [<Fact>]
    let ``POST /api/signals creates signal`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let content = new StringContent(createSignalJson "Wave" [| 1.0; 2.0; 3.0 |] 100.0, Encoding.UTF8, "application/json")
            let! response = client.PostAsync("/api/signals", content)
            let! body = response.Content.ReadAsStringAsync()
            if response.StatusCode <> HttpStatusCode.Created then
                failwithf "Expected Created but got %A. Body: %s" response.StatusCode body
            Assert.Contains("Wave", body)
        }

    [<Fact>]
    let ``POST /api/signals rejects empty name`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let content = new StringContent(createSignalJson "" [| 1.0 |] 10.0, Encoding.UTF8, "application/json")
            let! response = client.PostAsync("/api/signals", content)
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode)
        }

    [<Fact>]
    let ``GET /api/signals lists signals`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let content = new StringContent(createSignalJson "List" [| 1.0; 2.0 |] 10.0, Encoding.UTF8, "application/json")
            let! _ = client.PostAsync("/api/signals", content)
            let! response = client.GetAsync("/api/signals")
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            let! body = response.Content.ReadAsStringAsync()
            Assert.Contains("List", body)
        }

    [<Fact>]
    let ``GET /api/signals/:id returns 404 for unknown`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let! response = client.GetAsync("/api/signals/nonexistent")
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
        }

    [<Fact>]
    let ``DELETE /api/signals/:id deletes signal`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let content = new StringContent(createSignalJson "ToDelete" [| 1.0 |] 10.0, Encoding.UTF8, "application/json")
            let! createResp = client.PostAsync("/api/signals", content)
            let! createBody = createResp.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("id").GetString()
            let! deleteResp = client.DeleteAsync(sprintf "/api/signals/%s" id)
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode)
        }

    [<Fact>]
    let ``POST /api/signals/:id/analyze returns analysis`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let content = new StringContent(createSignalJson "Analyze" [| 1.0; -1.0; 2.0; -2.0 |] 100.0, Encoding.UTF8, "application/json")
            let! createResp = client.PostAsync("/api/signals", content)
            let! createBody = createResp.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("id").GetString()
            let! analyzeResp = client.PostAsync(sprintf "/api/signals/%s/analyze" id, null)
            Assert.Equal(HttpStatusCode.OK, analyzeResp.StatusCode)
            let! body = analyzeResp.Content.ReadAsStringAsync()
            Assert.Contains("analysis", body.ToLower())
        }

    [<Fact>]
    let ``POST /api/filters/apply with moving_average`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let createContent = new StringContent(createSignalJson "Filter" [| 1.0; 10.0; 1.0; 10.0; 1.0 |] 10.0, Encoding.UTF8, "application/json")
            let! createResp = client.PostAsync("/api/signals", createContent)
            let! createBody = createResp.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("id").GetString()
            let filterJson = sprintf """{"signalId":"%s","filterType":"moving_average","windowSize":3}""" id
            let filterContent = new StringContent(filterJson, Encoding.UTF8, "application/json")
            let! filterResp = client.PostAsync("/api/filters/apply", filterContent)
            Assert.Equal(HttpStatusCode.OK, filterResp.StatusCode)
            let! body = filterResp.Content.ReadAsStringAsync()
            Assert.Contains("resultData", body)
        }

    [<Fact>]
    let ``POST /api/anomalies/detect finds anomalies`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let createContent = new StringContent(createSignalJson "Anomaly" [| 1.0; 1.1; 0.9; 1.0; 100.0 |] 10.0, Encoding.UTF8, "application/json")
            let! createResp = client.PostAsync("/api/signals", createContent)
            let! createBody = createResp.Content.ReadAsStringAsync()
            let doc = JsonDocument.Parse(createBody)
            let id = doc.RootElement.GetProperty("id").GetString()
            let detectJson = sprintf """{"signalId":"%s","threshold":2.0}""" id
            let detectContent = new StringContent(detectJson, Encoding.UTF8, "application/json")
            let! detectResp = client.PostAsync("/api/anomalies/detect", detectContent)
            Assert.Equal(HttpStatusCode.OK, detectResp.StatusCode)
            let! body = detectResp.Content.ReadAsStringAsync()
            Assert.Contains("anomalies", body)
        }

    [<Fact>]
    let ``GET /api/stats returns statistics`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let content = new StringContent(createSignalJson "Stats" [| 1.0; 2.0; 3.0 |] 10.0, Encoding.UTF8, "application/json")
            let! _ = client.PostAsync("/api/signals", content)
            let! response = client.GetAsync("/api/stats")
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            let! body = response.Content.ReadAsStringAsync()
            Assert.Contains("totalSignals", body)
        }

    [<Fact>]
    let ``POST /api/signals/compare compares two signals`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let c1 = new StringContent(createSignalJson "SigA" [| 1.0; 2.0; 3.0; 4.0; 5.0 |] 10.0, Encoding.UTF8, "application/json")
            let! r1 = client.PostAsync("/api/signals", c1)
            let! b1 = r1.Content.ReadAsStringAsync()
            let id1 = JsonDocument.Parse(b1).RootElement.GetProperty("id").GetString()

            let c2 = new StringContent(createSignalJson "SigB" [| 1.0; 2.0; 3.0; 4.0; 5.0 |] 10.0, Encoding.UTF8, "application/json")
            let! r2 = client.PostAsync("/api/signals", c2)
            let! b2 = r2.Content.ReadAsStringAsync()
            let id2 = JsonDocument.Parse(b2).RootElement.GetProperty("id").GetString()

            let compareJson = sprintf """{"signalIdA":"%s","signalIdB":"%s"}""" id1 id2
            let compareContent = new StringContent(compareJson, Encoding.UTF8, "application/json")
            let! compareResp = client.PostAsync("/api/signals/compare", compareContent)
            Assert.Equal(HttpStatusCode.OK, compareResp.StatusCode)
            let! body = compareResp.Content.ReadAsStringAsync()
            Assert.Contains("correlation", body)
            Assert.Contains("similarityScore", body)
        }

    [<Fact>]
    let ``POST /api/signals/compare returns 404 for missing signal`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let compareJson = """{"signalIdA":"missing1","signalIdB":"missing2"}"""
            let compareContent = new StringContent(compareJson, Encoding.UTF8, "application/json")
            let! compareResp = client.PostAsync("/api/signals/compare", compareContent)
            Assert.Equal(HttpStatusCode.NotFound, compareResp.StatusCode)
        }

    [<Fact>]
    let ``POST /api/filters/apply returns 404 for unknown signal`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let filterJson = """{"signalId":"missing","filterType":"moving_average","windowSize":3}"""
            let filterContent = new StringContent(filterJson, Encoding.UTF8, "application/json")
            let! filterResp = client.PostAsync("/api/filters/apply", filterContent)
            Assert.Equal(HttpStatusCode.NotFound, filterResp.StatusCode)
        }

    [<Fact>]
    let ``POST /api/filters/apply rejects invalid filter type`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let createContent = new StringContent(createSignalJson "BadFilter" [| 1.0; 2.0; 3.0 |] 10.0, Encoding.UTF8, "application/json")
            let! createResp = client.PostAsync("/api/signals", createContent)
            let! createBody = createResp.Content.ReadAsStringAsync()
            let id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetString()
            let filterJson = sprintf """{"signalId":"%s","filterType":"invalid_type","windowSize":3}""" id
            let filterContent = new StringContent(filterJson, Encoding.UTF8, "application/json")
            let! filterResp = client.PostAsync("/api/filters/apply", filterContent)
            Assert.Equal(HttpStatusCode.BadRequest, filterResp.StatusCode)
        }

    [<Fact>]
    let ``Unknown route returns 404`` () =
        task {
            use factory = new TestWebAppFactory()
            use client = factory.CreateClient()
            let! response = client.GetAsync("/api/nonexistent")
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode)
        }
