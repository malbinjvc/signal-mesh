namespace SignalMesh

open System
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Giraffe
open SignalMesh.Services
open SignalMesh.Routes

module Program =

    /// Marker type for WebApplicationFactory discovery
    type Marker = class end

    [<EntryPoint>]
    let main args =
        let signalService = SignalService()

        let builder = WebApplication.CreateBuilder(args)

        builder.Services.AddGiraffe() |> ignore
        builder.Services.AddSingleton<SignalService>(signalService) |> ignore

        let app = builder.Build()
        app.UseGiraffe(Handlers.webApp signalService)
        app.Run()
        0
