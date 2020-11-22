module JetBrainsDl.App

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Giraffe.Serialization
open JetBrainsDl.Model
open FSharp.Control.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Microsoft.Extensions.Primitives

// ---------------------------------
// Web app
// ---------------------------------

let latestPlugins (model: DownloadPluginsModel) : HttpHandler =
    fun _ ctx ->
        task {
            let! result = getPluginPackage {Products = model.Products; Types = model.Types} model.Plugins
            ctx.Response.ContentType <- "application/zip"
            ctx.Response.Headers.Add("Content-Disposition", StringValues "attachment; filename=\"plugins.zip\"")
            return! ctx.WriteStreamAsync false result None None
        }

let latestProducts (model: QueryLatestProductModel) : HttpHandler =
    fun next ctx ->
        task {
            let result = getLatest model
            return! json result next ctx
        }

let webApp =
    choose [ GET
             >=> subRouteCi
                     "/api/latest"
                     (choose [
                               routeCi "/products" >=> bindQuery None latestProducts
                               routeCi "/plugins" >=> bindQuery None latestPlugins
                             ])
             setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex: Exception) (logger: ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")

    clearResponse
    >=> setStatusCode 500
    >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureApp (app: IApplicationBuilder) =
    let env =
        app.ApplicationServices.GetService<IWebHostEnvironment>()

    (match env.EnvironmentName with
     | "Development" -> app.UseDeveloperExceptionPage()
     | _ -> app.UseGiraffeErrorHandler(errorHandler))
        .UseGiraffe(webApp)

let configureServices (services: IServiceCollection) =
    let jsonOptions = JsonSerializerOptions()
    jsonOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
    jsonOptions.Converters.Add(JsonFSharpConverter())
    services.AddSingleton(jsonOptions) |> ignore
    services.AddSingleton<IJsonSerializer, SystemTextJsonSerializer>() |> ignore
    services.AddGiraffe() |> ignore

let configureLogging (builder: ILoggingBuilder) =
    builder
        .AddFilter(fun l -> l.Equals LogLevel.Information)
        .AddConsole()
        .AddDebug()
    |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()

    Host
        .CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(fun webHostBuilder ->
            webHostBuilder
                .UseContentRoot(contentRoot)
                .Configure(Action<IApplicationBuilder> configureApp)
                .ConfigureServices(configureServices)
                .ConfigureLogging(configureLogging)
            |> ignore)
        .Build()
        .Run()

    0
