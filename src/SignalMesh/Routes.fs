namespace SignalMesh.Routes

open System
open Microsoft.AspNetCore.Http
open Giraffe
open SignalMesh.Models
open SignalMesh.Services
open SignalMesh.Clients

module Handlers =

    let healthCheck : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            let response = { Status = "healthy"; Timestamp = DateTime.UtcNow }
            json response next ctx

    let createSignal (signalService: SignalService) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let! request = ctx.BindJsonAsync<CreateSignalRequest>()
                if String.IsNullOrWhiteSpace(request.Name) then
                    ctx.SetStatusCode 400
                    return! json { Error = "Name is required" } next ctx
                elif request.DataPoints = null || request.DataPoints.Length = 0 then
                    ctx.SetStatusCode 400
                    return! json { Error = "DataPoints are required" } next ctx
                elif request.SampleRate <= 0.0 then
                    ctx.SetStatusCode 400
                    return! json { Error = "SampleRate must be positive" } next ctx
                else
                    let signal = signalService.Create(request)
                    ctx.SetStatusCode 201
                    return! json signal next ctx
            }

    let listSignals (signalService: SignalService) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            let signals = signalService.GetAll()
            json signals next ctx

    let getSignal (signalService: SignalService) (id: string) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            match signalService.Get(id) with
            | Some signal -> json signal next ctx
            | None ->
                ctx.SetStatusCode 404
                json { Error = "Signal not found" } next ctx

    let deleteSignal (signalService: SignalService) (id: string) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            if signalService.Delete(id) then
                ctx.SetStatusCode 204
                ctx.WriteStringAsync("") |> ignore
                next ctx
            else
                ctx.SetStatusCode 404
                json { Error = "Signal not found" } next ctx

    let analyzeSignal (signalService: SignalService) (id: string) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            match signalService.Get(id) with
            | Some signal ->
                signalService.IncrementAnalyses()
                let result = MockClaudeClient.analyzeSignal signal
                json result next ctx
            | None ->
                ctx.SetStatusCode 404
                json { Error = "Signal not found" } next ctx

    let applyFilter (signalService: SignalService) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let! request = ctx.BindJsonAsync<FilterRequest>()
                match signalService.Get(request.SignalId) with
                | None ->
                    ctx.SetStatusCode 404
                    return! json { Error = "Signal not found" } next ctx
                | Some signal ->
                    match FilterService.applyFilter signal request.FilterType request.WindowSize with
                    | Ok result ->
                        return! json result next ctx
                    | Error msg ->
                        ctx.SetStatusCode 400
                        return! json { Error = msg } next ctx
            }

    let detectAnomalies (signalService: SignalService) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let! request = ctx.BindJsonAsync<AnomalyDetectRequest>()
                match signalService.Get(request.SignalId) with
                | None ->
                    ctx.SetStatusCode 404
                    return! json { Error = "Signal not found" } next ctx
                | Some signal ->
                    let result = AnomalyService.detect signal request.Threshold
                    return! json result next ctx
            }

    let getStats (signalService: SignalService) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            let stats = signalService.GetStats()
            json stats next ctx

    let compareSignals (signalService: SignalService) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let! request = ctx.BindJsonAsync<CompareRequest>()
                match signalService.Get(request.SignalIdA), signalService.Get(request.SignalIdB) with
                | Some a, Some b ->
                    signalService.IncrementAnalyses()
                    let result = MockClaudeClient.compareSignals a b
                    return! json result next ctx
                | None, _ ->
                    ctx.SetStatusCode 404
                    return! json { Error = sprintf "Signal '%s' not found" request.SignalIdA } next ctx
                | _, None ->
                    ctx.SetStatusCode 404
                    return! json { Error = sprintf "Signal '%s' not found" request.SignalIdB } next ctx
            }

    let webApp (signalService: SignalService) : HttpHandler =
        choose [
            GET >=> choose [
                route "/health" >=> healthCheck
                route "/api/signals" >=> listSignals signalService
                routef "/api/signals/%s" (getSignal signalService)
                route "/api/stats" >=> getStats signalService
            ]
            POST >=> choose [
                route "/api/signals" >=> createSignal signalService
                routef "/api/signals/%s/analyze" (analyzeSignal signalService)
                route "/api/filters/apply" >=> applyFilter signalService
                route "/api/anomalies/detect" >=> detectAnomalies signalService
                route "/api/signals/compare" >=> compareSignals signalService
            ]
            DELETE >=> choose [
                routef "/api/signals/%s" (deleteSignal signalService)
            ]
            setStatusCode 404 >=> json { Error = "Not found" }
        ]
