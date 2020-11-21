module JetBrainsDl.Model

open System
open System.IO
open System.Web
open FsHttp
open FsHttp.DslCE
open ICSharpCode.SharpZipLib.Zip
open Newtonsoft.Json

module ApiModel =

    type DownloadInfo = { Link: string }
    type Downloads = { Windows: DownloadInfo }
    type Release =
        { Date: DateTime
          Type: string
          Downloads: Downloads
          Version: string
          MajorVersion: string
          Build: string }
    type Product =
        { Code: string; Releases: Release list }

    type PluginInfo =
        { Id: int; XmlId: string; Name: string }

[<CLIMutable>]
type QueryLatestProductModel =
    { Products: string list; Types: string list }

[<CLIMutable>]
type DownloadPluginsModel =
    { Products: string list; Types: string list; Plugins: string list }

let getLatestVersion (r: ApiModel.Product) =
    r.Code, r.Releases |> Seq.sortByDescending (fun it -> it.Date) |> Seq.head

let urlEncode (p: string list) =
    String.Join(',', p) |> HttpUtility.UrlEncode

let getLatest (model: QueryLatestProductModel) =
    let products = model.Products
    let releaseTypes = model.Types

    let fields = [ "link"; "code"; "releases"; "code" ]

    let url =
        $"""https://data.services.jetbrains.com/products?code={urlEncode products}&release.type={urlEncode releaseTypes}&fields={urlEncode fields}"""

    http { GET url }
    |> Response.toText
    |> JsonConvert.DeserializeObject<ApiModel.Product list>
    |> List.map getLatestVersion
    |> dict

let getPluginInfo (id: string) =
    http {
        GET $"https://plugins.jetbrains.com/api/plugins/{id}"
    }
    |> Response.toText
    |> JsonConvert.DeserializeObject<ApiModel.PluginInfo>

let getLatestPluginDownloadUrl product buildNo xmlId =
    $"https://plugins.jetbrains.com/pluginManager?action=download&id={xmlId}&build={product}-{buildNo}"

let createZipStream (items: (string * ApiModel.PluginInfo * Stream) seq) =
    let ms = new MemoryStream()
    use outStream = new ZipOutputStream(ms)
    outStream.IsStreamOwner <- false
    for (code, plugin, stream) in items do
        outStream.PutNextEntry(ZipEntry($"{code}/{plugin.Name}.zip"))
        stream.CopyTo outStream
    outStream.Finish()
    ms.Seek(0L, SeekOrigin.Begin) |> ignore
    ms

let download url =
    async {
        let! resp =
            httpLazy {
                GET url
            }
            |> Request.sendAsync
        return! resp |> Response.toStreamAsync
    }

let getPluginPackage (products: QueryLatestProductModel) (pluginIds: string list) =
    let urls =
        seq {
            for KeyValue(code, release) in getLatest products do
                for p in pluginIds |> Seq.map getPluginInfo do
                    code, p, getLatestPluginDownloadUrl code release.Build p.XmlId
        }
    async {
        let! downloaded =
            seq {
                for (code, plugin, url) in urls do
                    async {
                        let! stream = download url
                        return code, plugin, stream
                    }
            }
            |> Async.Parallel
        return createZipStream downloaded
    }

