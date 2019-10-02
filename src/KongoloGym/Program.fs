module KongoloGym.App

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection

open Giraffe
open Prometheus
open Serilog
open Serilog.Sinks.Elasticsearch

let log =
    // TODO Make Elasticsearch sink optional
    let elasticOptions = ElasticsearchSinkOptions (Uri("http://elasticsearch-master:9200") )
    elasticOptions.AutoRegisterTemplate <- true
    elasticOptions.AutoRegisterTemplateVersion <- AutoRegisterTemplateVersion.ESv6
    elasticOptions.BufferBaseFilename <- "./logs/buffer"

    Serilog
        .LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.Elasticsearch(elasticOptions)
        .CreateLogger()

// ---------------------------------
// Models
// ---------------------------------

type Message =
    {
        Text : string
    }

// ---------------------------------
// Metrics
// ---------------------------------
let requestCount =
    let counterConfiguration = CounterConfiguration(LabelNames = [| "name" |])
    Metrics.CreateCounter(
        "kongologym_request_count",
        "Number of requests to Kongolo Gym",
        counterConfiguration)

// --------------------ounter -------------
// Views
// ---------------------------------

module Views =
    open GiraffeViewEngine

    let layout (content: XmlNode list) =
        html [] [
            head [] [
                title []  [ encodedText "KongoloGym" ]
                link [ _rel  "stylesheet"
                       _type "text/css"
                       _href "/main.css" ]
            ]
            body [] content
        ]

    let partial () =
        h1 [] [ encodedText "KongoloGym" ]

    let index (model : Message) =
        [
            partial()
            p [] [ encodedText model.Text ]
        ] |> layout

// ---------------------------------
// Web app
// ---------------------------------

let indexHandler (name : string) =
    let greetings = sprintf "Hello %s, from Kongolo. Bob Smith was here!" name
    let model     = { Text = greetings }
    let view      = Views.index model
    requestCount.WithLabels(name).Inc()
    log.Information("/hello called with {name}", name)
    htmlView view

let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> indexHandler "world"
                routef "/hello/%s" indexHandler
            ]
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (_logger : Microsoft.Extensions.Logging.ILogger) =
    //_logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    log.Error(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IHostingEnvironment>()
    
    // Configure Prometheus Metrics (This needs to be done before some of the other config)
    let app = app.UseHttpMetrics()

    (match env.IsDevelopment() with
    | true  -> app.UseDeveloperExceptionPage()
    | false -> app.UseGiraffeErrorHandler errorHandler)
        .UseHttpsRedirection()
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseMetricServer()
        .UseGiraffe(webApp)

let configureServices (services : IServiceCollection) =
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    builder.AddFilter(fun l -> l.Equals LogLevel.Error)
           .AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    WebHostBuilder()
        .UseKestrel()
        .UseContentRoot(contentRoot)
        .UseIISIntegration()
        .UseWebRoot(webRoot)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()
    0
